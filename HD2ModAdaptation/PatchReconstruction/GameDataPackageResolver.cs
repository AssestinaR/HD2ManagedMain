using System.Buffers.Binary;
using System.Text;
using K4os.Compression.LZ4;

namespace HD2ModAdaptation.PatchReconstruction;

// Purpose: Reads legacy, DSAR, and bundled game archives for material dependency fallback resolution.
public sealed class GameDataPackageResolver : IGameDataPackageResolver
{
	private const uint TocMagic = 4026531857;
	private const uint DsarMagic = 0x52415344;
	private const byte ChunkStart = 0x02;
	private const byte Lz4Compression = 0x03;
	private readonly string gameDataDirectory;
	private readonly Dictionary<string, PackageInfo> packages = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Dictionary<long, int>> chunkOffsets = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, byte[]> reconstructedPackages = new(StringComparer.OrdinalIgnoreCase);
	private bool initialized;

	public GameDataPackageResolver(string gameDataDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(gameDataDirectory);
		this.gameDataDirectory = Path.GetFullPath(gameDataDirectory);
	}

	public async ValueTask<GameDataPackageToc?> GetPackageTocAsync(string packageName, CancellationToken cancellationToken = default)
	{
		var path = Path.Combine(gameDataDirectory, Path.GetFileName(packageName));
		var type = await GetStorageTypeAsync(path, cancellationToken).ConfigureAwait(false);
		var data = type switch
		{
			StorageType.Legacy => await ReadLegacyTocAsync(path, cancellationToken).ConfigureAwait(false),
			StorageType.Dsar => await ReadDsarResourceAsync(path, 0, cancellationToken).ConfigureAwait(false),
			StorageType.Bundled => await ReadBundledTocAsync(Path.GetFileName(packageName), cancellationToken).ConfigureAwait(false),
			_ => null
		};
		return data is { Length: > 0 } ? new GameDataPackageToc(data, type is StorageType.Dsar or StorageType.Bundled) : null;
	}

	private async ValueTask<byte[]?> ReadBundledTocAsync(string packageName, CancellationToken cancellationToken)
	{
		var header = await ReadBundledPrefixAsync(packageName, 12, cancellationToken).ConfigureAwait(false);
		if (header is null || header.Length < 12 || ReadUInt32(header, 0) != TocMagic) return null;
		var typeCount = ReadUInt32(header, 4);
		var fileCount = ReadUInt32(header, 8);
		var legacyLength = checked(60 + checked((int)typeCount * 32) + checked((int)fileCount * 80));
		var slimLength = checked(72 + checked((int)typeCount * 32) + checked((int)fileCount * 80));
		return await ReadBundledPrefixAsync(packageName, Math.Max(legacyLength, slimLength), cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask<byte[]?> ReadBundledPrefixAsync(string packageName, int requestedLength, CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		if (!packages.TryGetValue(packageName, out var package) || package.Entries.Count == 0) return null;
		var length = checked((int)Math.Min(package.Size, requestedLength));
		var data = new byte[length];
		var entries = package.Entries.OrderBy(entry => entry.ArchiveOffset).ToArray();
		for (var index = 0; index < entries.Length; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var entry = entries[index];
			if (entry.ArchiveOffset >= length) break;
			var nextOffset = index + 1 < entries.Length ? entries[index + 1].ArchiveOffset : package.Size;
			var available = checked((int)Math.Min(nextOffset - entry.ArchiveOffset, length - entry.ArchiveOffset));
			if (available <= 0) continue;
			var resources = await ReadBundledResourcesAsync(
				Path.Combine(gameDataDirectory, $"bundles.{entry.BundleIndex:00}.nxa"),
				entry.BundleOffset,
				available,
				cancellationToken).ConfigureAwait(false);
			var combined = Combine(resources);
			Buffer.BlockCopy(combined, 0, data, checked((int)entry.ArchiveOffset), Math.Min(combined.Length, available));
		}
		return data;
	}

	public async ValueTask<byte[]?> GetPackageResourceAsync(string packageName, ulong resourceOffset, uint resourceSize, CancellationToken cancellationToken = default)
	{
		if (resourceSize == 0) return Array.Empty<byte>();
		var normalized = Path.GetFileName(packageName);
		var path = Path.Combine(gameDataDirectory, normalized);
		var type = await GetStorageTypeAsync(path, cancellationToken).ConfigureAwait(false);
		var offset = checked((long)resourceOffset);
		var data = type switch
		{
			StorageType.Legacy => await ReadLegacyResourceAsync(path, offset, resourceSize, cancellationToken).ConfigureAwait(false),
			StorageType.Dsar => await ReadDsarResourceAsync(path, offset, cancellationToken).ConfigureAwait(false),
			StorageType.Bundled => await ReadBundledResourceAsync(normalized, offset, resourceSize, cancellationToken).ConfigureAwait(false),
			_ => null
		};
		return data is null || data.Length < resourceSize ? null : data.Length == resourceSize ? data : data.AsSpan(0, checked((int)resourceSize)).ToArray();
	}

	public async ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken cancellationToken = default)
	{
		if (!Directory.Exists(gameDataDirectory)) return Array.Empty<string>();

		var directPackages = Directory.EnumerateFiles(gameDataDirectory)
			.Select(Path.GetFileName)
			.Where(name => name is not null && !name.Contains(".patch", StringComparison.OrdinalIgnoreCase) && (Path.GetExtension(name) is "" or ".stream" or ".gpu_resources"))
			.Cast<string>()
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		// 当前游戏目录可能同时有直接 archive 和 bundles.nxa；只返回前者会遗漏
		// 被 bundle 提供的当前 Unit/Material，从而错误报告“Game Data 中不存在”。
		return directPackages
			.Concat(packages.Keys)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private async ValueTask<byte[]?> ReconstructPackageAsync(string packageName, CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		if (reconstructedPackages.TryGetValue(packageName, out var cached)) return cached;
		if (!packages.TryGetValue(packageName, out var package) || package.Entries.Count == 0) return null;
		var data = new byte[checked((int)package.Size)];
		var entries = package.Entries.OrderBy(entry => entry.ArchiveOffset).ToArray();
		for (var index = 0; index < entries.Length; index++)
		{
			var entry = entries[index];
			var nextOffset = index + 1 < entries.Length ? entries[index + 1].ArchiveOffset : package.Size;
			var size = checked((int)(nextOffset - entry.ArchiveOffset));
			if (size <= 0) continue;
			var resources = await ReadBundledResourcesAsync(Path.Combine(gameDataDirectory, $"bundles.{entry.BundleIndex:00}.nxa"), entry.BundleOffset, size, cancellationToken).ConfigureAwait(false);
			var combined = Combine(resources); Buffer.BlockCopy(combined, 0, data, checked((int)entry.ArchiveOffset), Math.Min(combined.Length, size));
		}
		reconstructedPackages[packageName] = data;
		return data;
	}

	private async ValueTask<byte[]?> ReadBundledResourceAsync(string packageName, long offset, uint size, CancellationToken cancellationToken)
	{
		var data = await ReconstructPackageAsync(packageName, cancellationToken).ConfigureAwait(false);
		if (data is null || offset < 0 || offset + size > data.Length) return null;
		return data.AsSpan(checked((int)offset), checked((int)size)).ToArray();
	}

	private async ValueTask<IReadOnlyList<byte[]>> ReadBundledResourcesAsync(string path, long offset, int size, CancellationToken cancellationToken)
	{
		var result = new List<byte[]>();
		for (var read = 0; read < size; )
		{
			var chunk = await ReadDsarResourceAsync(path, offset + read, cancellationToken).ConfigureAwait(false);
			if (chunk.Length == 0) break;
			read += chunk.Length; result.Add(chunk);
		}
		return result;
	}

	private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
	{
		if (initialized) return;
		initialized = true;
		var database = Path.Combine(gameDataDirectory, "bundles.nxa");
		if (!File.Exists(database)) return;
		await BuildChunkMapsAsync(cancellationToken).ConfigureAwait(false);
		var data = await DecompressDsarAsync(database, cancellationToken).ConfigureAwait(false);
		if (data.Length < 0x18) return;
		var count = ReadInt32(data, 0x10);
		for (var index = 0; index < count; index++)
		{
			var offset = 0x18 + index * 0x18;
			if (offset + 0x18 > data.Length) break;
			var name = ReadNullTerminatedString(data, ReadInt32(data, offset + 8));
			if (string.IsNullOrWhiteSpace(name)) continue;
			var package = new PackageInfo(name, ReadInt64(data, offset));
			var itemCount = ReadInt32(data, offset + 12); var itemsOffset = ReadInt32(data, offset + 16);
			for (var item = 0; item < itemCount && itemsOffset + item * 0x10 + 0x10 <= data.Length; item++)
			{
				var itemOffset = itemsOffset + item * 0x10;
				package.Entries.Add(new BundleEntry(ReadInt64(data, itemOffset), ReadInt32(data, itemOffset + 8), data[itemOffset + 0x0f]));
			}
			packages[name] = package;
		}
	}

	private async ValueTask BuildChunkMapsAsync(CancellationToken cancellationToken)
	{
		if (!Directory.Exists(gameDataDirectory)) return;
		foreach (var path in Directory.EnumerateFiles(gameDataDirectory))
		{
			var name = Path.GetFileName(path); var extension = Path.GetExtension(name);
			if (name.Contains(".patch", StringComparison.OrdinalIgnoreCase) || !(extension.Length == 0 || extension.Equals(".stream", StringComparison.OrdinalIgnoreCase) || extension.Equals(".nxa", StringComparison.OrdinalIgnoreCase) || extension.Equals(".gpu_resources", StringComparison.OrdinalIgnoreCase))) continue;
			if (await TryBuildChunkMapAsync(path, cancellationToken).ConfigureAwait(false) is { } map) chunkOffsets[name] = map;
		}
	}

	private static async ValueTask<Dictionary<long, int>?> TryBuildChunkMapAsync(string path, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
		if (stream.Length < 12) return null;
		var header = new byte[12]; await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
		if (ReadUInt32(header, 0) != DsarMagic) return null;
		var map = new Dictionary<long, int>(); var count = ReadUInt32(header, 8);
		for (var index = 0; index < count; index++) { stream.Position = 0x20 + index * 0x20; var chunk = new byte[8]; await ReadExactlyAsync(stream, chunk, cancellationToken).ConfigureAwait(false); map[(long)ReadUInt64(chunk, 0)] = index; }
		return map;
	}

	private async ValueTask<byte[]> ReadDsarResourceAsync(string path, long offset, CancellationToken cancellationToken)
	{
		var name = Path.GetFileName(path);
		if (!chunkOffsets.TryGetValue(name, out var map)) { map = await TryBuildChunkMapAsync(path, cancellationToken).ConfigureAwait(false) ?? new Dictionary<long, int>(); chunkOffsets[name] = map; }
		if (!map.TryGetValue(offset, out var index)) return Array.Empty<byte>();
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
		var count = await ReadChunkCountAsync(stream, cancellationToken).ConfigureAwait(false); var chunks = new List<byte[]>();
		for (; index < count; index++) { var chunk = await ReadChunkAsync(stream, index, cancellationToken).ConfigureAwait(false); if ((chunk.Type & ChunkStart) != 0 && chunks.Count > 0) break; chunks.Add(chunk.Data); }
		return Combine(chunks);
	}

	private static async ValueTask<byte[]> DecompressDsarAsync(string path, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
		var count = await ReadChunkCountAsync(stream, cancellationToken).ConfigureAwait(false); var chunks = new List<byte[]>();
		for (var index = 0; index < count; index++) chunks.Add((await ReadChunkAsync(stream, index, cancellationToken).ConfigureAwait(false)).Data);
		return Combine(chunks);
	}

	private static async ValueTask<int> ReadChunkCountAsync(Stream stream, CancellationToken cancellationToken)
	{
		stream.Position = 0; var header = new byte[12]; await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
		return ReadUInt32(header, 0) == DsarMagic ? checked((int)ReadUInt32(header, 8)) : 0;
	}

	private static async ValueTask<Chunk> ReadChunkAsync(Stream stream, int index, CancellationToken cancellationToken)
	{
		stream.Position = 0x20 + index * 0x20; var header = new byte[0x20]; await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
		var compressedOffset = checked((long)ReadUInt64(header, 8)); var uncompressedSize = checked((int)ReadUInt32(header, 16)); var compressedSize = checked((int)ReadUInt32(header, 20));
		stream.Position = compressedOffset; var compressed = new byte[compressedSize]; await ReadExactlyAsync(stream, compressed, cancellationToken).ConfigureAwait(false);
		if (header[24] == 0) return new Chunk(header[25], compressed);
		if (header[24] != Lz4Compression) throw new InvalidDataException($"Unsupported DSAR compression type: {header[24]}");
		var output = new byte[uncompressedSize]; if (LZ4Codec.Decode(compressed, output) < 0) throw new InvalidDataException("Failed to decompress DSAR LZ4 chunk.");
		return new Chunk(header[25], output);
	}

	private static async ValueTask<StorageType> GetStorageTypeAsync(string path, CancellationToken cancellationToken)
	{
		if (!File.Exists(path)) return StorageType.Bundled;
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4, FileOptions.Asynchronous | FileOptions.SequentialScan);
		var header = new byte[4]; await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
		return ReadUInt32(header, 0) == DsarMagic ? StorageType.Dsar : StorageType.Legacy;
	}

	private static async ValueTask<byte[]?> ReadLegacyTocAsync(string path, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
		var header = new byte[12]; await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
		if (ReadUInt32(header, 0) != TocMagic) return null;
		var length = checked(60 + checked((int)ReadUInt32(header, 4) * 32) + checked((int)ReadUInt32(header, 8) * 80)); stream.Position = 0;
		var data = new byte[length]; await ReadExactlyAsync(stream, data, cancellationToken).ConfigureAwait(false); return data;
	}

	private static async ValueTask<byte[]?> ReadLegacyResourceAsync(string path, long offset, uint size, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
		if (offset < 0 || offset + size > stream.Length) return null;
		stream.Position = offset; var data = new byte[size]; await ReadExactlyAsync(stream, data, cancellationToken).ConfigureAwait(false); return data;
	}

	private static byte[] Combine(IReadOnlyList<byte[]> chunks) { var data = new byte[chunks.Sum(chunk => chunk.Length)]; var offset = 0; foreach (var chunk in chunks) { Buffer.BlockCopy(chunk, 0, data, offset, chunk.Length); offset += chunk.Length; } return data; }
	private static int ReadInt32(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
	private static long ReadInt64(byte[] data, int offset) => BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset, 8));
	private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
	private static ulong ReadUInt64(byte[] data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
	private static string ReadNullTerminatedString(byte[] data, int offset) { if (offset < 0 || offset >= data.Length) return string.Empty; var end = offset; while (end < data.Length && data[end] != 0) end++; return Encoding.UTF8.GetString(data, offset, end - offset); }
	private static async Task ReadExactlyAsync(Stream stream, byte[] data, CancellationToken cancellationToken) { var read = 0; while (read < data.Length) { var count = await stream.ReadAsync(data.AsMemory(read), cancellationToken).ConfigureAwait(false); if (count == 0) throw new EndOfStreamException(); read += count; } }
	private enum StorageType { Legacy, Dsar, Bundled }
	private sealed record PackageInfo(string Name, long Size) { public List<BundleEntry> Entries { get; } = new(); }
	private readonly record struct BundleEntry(long ArchiveOffset, long BundleOffset, int BundleIndex);
	private readonly record struct Chunk(byte Type, byte[] Data);
}
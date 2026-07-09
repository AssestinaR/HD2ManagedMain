using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：从原版游戏 archive 定位并读取 Unit 资源 payload，再解析为目标 Unit mesh 模板。
// Purpose: Locates and reads Unit resource payloads from vanilla game archives and parses them as target Unit mesh templates.
public sealed class ArchiveUnitMeshReader : IArchiveUnitMeshReader
{
	private const ulong BoneTypeId = 0x18dead01056b72e9;
	private const ulong CompositeUnitTypeId = 0xc4f0f4be7fb0c8d6;

	private readonly Func<string, IGameDataPackageResolver> resolverFactory;
	private readonly IPatchTocScanner tocScanner;
	private readonly IUnitMeshReader unitMeshReader;

	public ArchiveUnitMeshReader(
		Func<string, IGameDataPackageResolver> resolverFactory,
		IPatchTocScanner tocScanner,
		IUnitMeshReader unitMeshReader)
	{
		this.resolverFactory = resolverFactory ?? throw new ArgumentNullException(nameof(resolverFactory));
		this.tocScanner = tocScanner ?? throw new ArgumentNullException(nameof(tocScanner));
		this.unitMeshReader = unitMeshReader ?? throw new ArgumentNullException(nameof(unitMeshReader));
	}

	public async ValueTask<ArchiveUnitMesh> ReadUnitMeshAsync(
		string gameDataDirectory,
		string archiveName,
		AssetKey assetKey,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(gameDataDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameDataDirectory));
		}

		if (string.IsNullOrWhiteSpace(archiveName))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(archiveName));
		}

		if (assetKey.TypeId != PatchUnitMeshReader.UnitTypeId)
		{
			throw new InvalidDataException($"Asset type 0x{assetKey.TypeId:x16} is not a Unit resource.");
		}

		var resolver = resolverFactory(Path.GetFullPath(gameDataDirectory));
		var toc = await resolver.GetPackageTocAsync(archiveName, cancellationToken).ConfigureAwait(false)
			?? throw new FileNotFoundException($"Could not resolve archive TOC '{archiveName}' from game data directory '{gameDataDirectory}'.", archiveName);

		var patchEntries = tocScanner.ScanEntries(toc.Data, archiveName, toc.UsesSlimEntryOffset);
		var tocEntry = FindEntry(patchEntries, archiveName, assetKey);
		var payload = await ReadPayloadAsync(resolver, tocEntry, cancellationToken).ConfigureAwait(false);
		var compositePayload = await TryReadCompositePayloadAsync(resolver, tocScanner, patchEntries, payload, cancellationToken).ConfigureAwait(false);
		var boneNames = await TryReadBoneNamesAsync(resolver, tocScanner, patchEntries, payload, cancellationToken).ConfigureAwait(false);
		var model = compositePayload is null
			? unitMeshReader.Read(payload.TocData, payload.GpuResourceData, boneNames: boneNames)
			: unitMeshReader.Read(payload.TocData, payload.GpuResourceData, compositePayload.TocData, compositePayload.GpuResourceData, boneNames);
		return new ArchiveUnitMesh(tocEntry, payload, model, compositePayload);
	}

	private static async ValueTask<UnitBoneNames?> TryReadBoneNamesAsync(
		IGameDataPackageResolver resolver,
		IPatchTocScanner tocScanner,
		IReadOnlyList<PatchTocEntry> patchEntries,
		ArchiveEntryPayload unitPayload,
		CancellationToken cancellationToken)
	{
		if (unitPayload.TocData.Length < 16)
		{
			return null;
		}

		var bonesRef = ReadUInt64(unitPayload.TocData, 8);
		if (bonesRef == 0)
		{
			return null;
		}

		var boneEntry = await FindReferencedEntryAsync(
			resolver,
			tocScanner,
			patchEntries,
			Path.GetFileName(unitPayload.Entry.ArchiveName),
			new AssetKey(BoneTypeId, bonesRef),
			cancellationToken).ConfigureAwait(false);
		if (boneEntry is null)
		{
			return null;
		}

		try
		{
			var bonePayload = await ReadPayloadAsync(resolver, boneEntry, cancellationToken).ConfigureAwait(false);
			return new UnitBoneNamesReader().Read(bonePayload.TocData);
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static ArchiveTocEntry FindEntry(IReadOnlyList<PatchTocEntry> patchEntries, string archiveName, AssetKey assetKey)
	{
		var patchEntry = patchEntries.FirstOrDefault(entry => entry.AssetKey == assetKey);
		if (patchEntry is null)
		{
			throw new KeyNotFoundException($"Asset 0x{assetKey.TypeId:x16}/0x{assetKey.FileId:x16} was not found in archive '{archiveName}'.");
		}

		return new ArchiveTocEntry(
			patchEntry.AssetKey,
			Path.GetFileName(archiveName),
			patchEntry.TocDataOffset,
			patchEntry.StreamOffset,
			patchEntry.GpuResourceOffset,
			patchEntry.TocDataSize,
			patchEntry.StreamSize,
			patchEntry.GpuResourceSize,
			patchEntry.EntryIndex);
	}

	private static async ValueTask<ArchiveEntryPayload?> TryReadCompositePayloadAsync(
		IGameDataPackageResolver resolver,
		IPatchTocScanner tocScanner,
		IReadOnlyList<PatchTocEntry> patchEntries,
		ArchiveEntryPayload unitPayload,
		CancellationToken cancellationToken)
	{
		if (unitPayload.TocData.Length < 24)
		{
			return null;
		}

		var compositeRef = ReadUInt64(unitPayload.TocData, 16);
		if (compositeRef == 0)
		{
			return null;
		}

		var compositeEntry = await FindReferencedEntryAsync(
			resolver,
			tocScanner,
			patchEntries,
			Path.GetFileName(unitPayload.Entry.ArchiveName),
			new AssetKey(CompositeUnitTypeId, compositeRef),
			cancellationToken).ConfigureAwait(false);
		if (compositeEntry is null)
		{
			return null;
		}

		return await ReadPayloadAsync(resolver, compositeEntry, cancellationToken).ConfigureAwait(false);
	}

	private static async ValueTask<ArchiveTocEntry?> FindReferencedEntryAsync(
		IGameDataPackageResolver resolver,
		IPatchTocScanner tocScanner,
		IReadOnlyList<PatchTocEntry> localEntries,
		string localArchiveName,
		AssetKey assetKey,
		CancellationToken cancellationToken)
	{
		var localEntry = localEntries.FirstOrDefault(entry => entry.AssetKey == assetKey);
		if (localEntry is not null)
		{
			return ToArchiveEntry(localEntry, localArchiveName);
		}

		foreach (var packageName in await resolver.GetPackageNamesAsync(cancellationToken).ConfigureAwait(false))
		{
			if (string.Equals(packageName, localArchiveName, StringComparison.OrdinalIgnoreCase)
				|| packageName.EndsWith(".stream", StringComparison.OrdinalIgnoreCase)
				|| packageName.EndsWith(".gpu_resources", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var toc = await resolver.GetPackageTocAsync(packageName, cancellationToken).ConfigureAwait(false);
			if (toc is null)
			{
				continue;
			}

			IReadOnlyList<PatchTocEntry> entries;
			try
			{
				entries = tocScanner.ScanEntries(toc.Data, packageName, toc.UsesSlimEntryOffset);
			}
			catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
			{
				continue;
			}

			var entry = entries.FirstOrDefault(candidate => candidate.AssetKey == assetKey);
			if (entry is not null)
			{
				return ToArchiveEntry(entry, Path.GetFileName(packageName));
			}
		}

		return null;
	}

	private static ArchiveTocEntry ToArchiveEntry(PatchTocEntry entry, string archiveName)
		=> new(
			entry.AssetKey,
			Path.GetFileName(archiveName),
			entry.TocDataOffset,
			entry.StreamOffset,
			entry.GpuResourceOffset,
			entry.TocDataSize,
			entry.StreamSize,
			entry.GpuResourceSize,
			entry.EntryIndex);

	private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset)
	{
		return (ulong)data[offset]
			| ((ulong)data[offset + 1] << 8)
			| ((ulong)data[offset + 2] << 16)
			| ((ulong)data[offset + 3] << 24)
			| ((ulong)data[offset + 4] << 32)
			| ((ulong)data[offset + 5] << 40)
			| ((ulong)data[offset + 6] << 48)
			| ((ulong)data[offset + 7] << 56);
	}

	private static async ValueTask<ArchiveEntryPayload> ReadPayloadAsync(
		IGameDataPackageResolver resolver,
		ArchiveTocEntry entry,
		CancellationToken cancellationToken)
	{
		var tocData = await ReadRequiredResourceAsync(resolver, entry.ArchiveName, entry.TocDataOffset, entry.TocDataSize, "TOC", cancellationToken).ConfigureAwait(false);
		var streamData = await ReadOptionalResourceAsync(resolver, entry.ArchiveName + ".stream", entry.StreamOffset, entry.StreamSize, cancellationToken).ConfigureAwait(false);
		var gpuResourceData = await ReadOptionalResourceAsync(resolver, entry.ArchiveName + ".gpu_resources", entry.GpuResourceOffset, entry.GpuResourceSize, cancellationToken).ConfigureAwait(false);
		return new ArchiveEntryPayload(entry, tocData, streamData, gpuResourceData);
	}

	private static async ValueTask<byte[]> ReadRequiredResourceAsync(
		IGameDataPackageResolver resolver,
		string archiveName,
		ulong offset,
		uint size,
		string label,
		CancellationToken cancellationToken)
	{
		if (size == 0)
		{
			throw new InvalidDataException($"Archive entry has empty {label} payload.");
		}

		var data = await resolver.GetPackageResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);
		if (data is null || data.Length < size)
		{
			throw new EndOfStreamException($"Could not read {label} payload at offset {offset} size {size} from archive '{archiveName}'.");
		}

		return data.Length == size ? data : data.AsSpan(0, checked((int)size)).ToArray();
	}

	private static async ValueTask<byte[]> ReadOptionalResourceAsync(
		IGameDataPackageResolver resolver,
		string archiveName,
		ulong offset,
		uint size,
		CancellationToken cancellationToken)
	{
		if (size == 0)
		{
			return Array.Empty<byte>();
		}

		var data = await resolver.GetPackageResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);
		if (data is null || data.Length < size)
		{
			throw new EndOfStreamException($"Could not read optional payload at offset {offset} size {size} from archive '{archiveName}'.");
		}

		return data.Length == size ? data : data.AsSpan(0, checked((int)size)).ToArray();
	}
}

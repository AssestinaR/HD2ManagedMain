using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：从原版游戏 archive 定位并读取 Unit 资源 payload，再解析为目标 Unit mesh 模板。
// Purpose: Locates and reads Unit resource payloads from vanilla game archives and parses them as target Unit mesh templates.
public sealed class ArchiveUnitMeshReader : IArchiveUnitMeshReader
{
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
		var compositePayload = await TryReadCompositePayloadAsync(resolver, patchEntries, payload, cancellationToken).ConfigureAwait(false);
		var model = compositePayload is null
			? unitMeshReader.Read(payload.TocData, payload.GpuResourceData)
			: unitMeshReader.Read(payload.TocData, payload.GpuResourceData, compositePayload.TocData, compositePayload.GpuResourceData);
		return new ArchiveUnitMesh(tocEntry, payload, model);
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

		var compositePatchEntry = patchEntries.FirstOrDefault(entry =>
			entry.AssetKey.TypeId == CompositeUnitTypeId && entry.AssetKey.FileId == compositeRef);
		if (compositePatchEntry is null)
		{
			return null;
		}

		var compositeEntry = new ArchiveTocEntry(
			compositePatchEntry.AssetKey,
			Path.GetFileName(unitPayload.Entry.ArchiveName),
			compositePatchEntry.TocDataOffset,
			compositePatchEntry.StreamOffset,
			compositePatchEntry.GpuResourceOffset,
			compositePatchEntry.TocDataSize,
			compositePatchEntry.StreamSize,
			compositePatchEntry.GpuResourceSize,
			compositePatchEntry.EntryIndex);

		return await ReadPayloadAsync(resolver, compositeEntry, cancellationToken).ConfigureAwait(false);
	}

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

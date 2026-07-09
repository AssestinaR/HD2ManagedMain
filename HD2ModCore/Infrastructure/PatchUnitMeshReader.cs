using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：组合 patch payload reader 与 Unit mesh reader，读取单个 Unit patch entry 的 mesh 模型。
// Purpose: Combines the patch payload reader and Unit mesh reader to parse a mesh model from one Unit patch entry.
public sealed class PatchUnitMeshReader : IPatchUnitMeshReader
{
	public const ulong UnitTypeId = 0xe0a48d0be9a7453f;
	private const ulong BoneTypeId = 0x18dead01056b72e9;
	private const ulong CompositeUnitTypeId = 0xc4f0f4be7fb0c8d6;

	private readonly IPatchEntryPayloadReader payloadReader;
	private readonly IUnitMeshReader unitMeshReader;
	private readonly IPatchTocScanner? patchTocScanner;
	private readonly IReadOnlyList<PatchTocEntry>? entries;

	public PatchUnitMeshReader(IPatchEntryPayloadReader payloadReader, IUnitMeshReader unitMeshReader, IPatchTocScanner? patchTocScanner = null, IReadOnlyList<PatchTocEntry>? entries = null)
	{
		this.payloadReader = payloadReader ?? throw new ArgumentNullException(nameof(payloadReader));
		this.unitMeshReader = unitMeshReader ?? throw new ArgumentNullException(nameof(unitMeshReader));
		this.patchTocScanner = patchTocScanner;
		this.entries = entries;
	}

	public PatchUnitMeshReader WithEntries(IReadOnlyList<PatchTocEntry> entries)
	{
		return new PatchUnitMeshReader(payloadReader, unitMeshReader, patchTocScanner, entries);
	}

	public async ValueTask<PatchUnitMesh> ReadUnitMeshAsync(PatchTocEntry entry, CancellationToken cancellationToken = default)
		=> await ReadUnitMeshCoreAsync(entry, entries, cancellationToken).ConfigureAwait(false);

	public async ValueTask<PatchUnitMesh> ReadUnitMeshAsync(PatchTocEntry entry, IReadOnlyList<PatchTocEntry> entries, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entries);
		return await ReadUnitMeshCoreAsync(entry, entries, cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask<PatchUnitMesh> ReadUnitMeshCoreAsync(PatchTocEntry entry, IReadOnlyList<PatchTocEntry>? contextEntries, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);
		if (entry.AssetKey.TypeId != UnitTypeId)
		{
			throw new InvalidDataException($"Patch entry type 0x{entry.AssetKey.TypeId:x16} is not a Unit resource.");
		}

		var payload = await payloadReader.ReadPayloadAsync(entry, cancellationToken).ConfigureAwait(false);
		var compositePayload = await TryReadCompositePayloadAsync(payload, contextEntries, cancellationToken).ConfigureAwait(false);
		var boneNames = await TryReadBoneNamesAsync(payload, contextEntries, cancellationToken).ConfigureAwait(false);
		var model = compositePayload is null
			? unitMeshReader.Read(payload.TocData, payload.GpuResourceData, boneNames: boneNames)
			: unitMeshReader.Read(payload.TocData, payload.GpuResourceData, compositePayload.TocData, compositePayload.GpuResourceData, boneNames);
		return new PatchUnitMesh(entry, payload, model, compositePayload);
	}

	private async ValueTask<UnitBoneNames?> TryReadBoneNamesAsync(PatchEntryPayload payload, IReadOnlyList<PatchTocEntry>? contextEntries, CancellationToken cancellationToken)
	{
		if (payload.TocData.Length < 16)
		{
			return null;
		}

		var bonesRef = ReadUInt64(payload.TocData, 8);
		if (bonesRef == 0)
		{
			return null;
		}

		var candidateEntries = contextEntries ?? entries;
		if (candidateEntries is null)
		{
			if (patchTocScanner is null)
			{
				return null;
			}

			candidateEntries = await patchTocScanner.ScanEntriesAsync(payload.Entry.SourceFilePath, cancellationToken).ConfigureAwait(false);
		}

		var boneEntry = candidateEntries.FirstOrDefault(entry =>
			entry.AssetKey.TypeId == BoneTypeId && entry.AssetKey.FileId == bonesRef);
		if (boneEntry is null)
		{
			return null;
		}

		try
		{
			var bonePayload = await payloadReader.ReadPayloadAsync(boneEntry, cancellationToken).ConfigureAwait(false);
			return new UnitBoneNamesReader().Read(bonePayload.TocData);
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private async ValueTask<PatchEntryPayload?> TryReadCompositePayloadAsync(PatchEntryPayload payload, IReadOnlyList<PatchTocEntry>? contextEntries, CancellationToken cancellationToken)
	{
		if (payload.TocData.Length < 24)
		{
			return null;
		}

		var compositeRef = ReadUInt64(payload.TocData, 16);
		if (compositeRef == 0)
		{
			return null;
		}

		var candidateEntries = contextEntries ?? entries;
		if (candidateEntries is null)
		{
			if (patchTocScanner is null)
			{
				return null;
			}

			candidateEntries = await patchTocScanner.ScanEntriesAsync(payload.Entry.SourceFilePath, cancellationToken).ConfigureAwait(false);
		}

		var compositeEntry = candidateEntries.FirstOrDefault(entry =>
			entry.AssetKey.TypeId == CompositeUnitTypeId && entry.AssetKey.FileId == compositeRef);
		if (compositeEntry is null)
		{
			return null;
		}

		return await payloadReader.ReadPayloadAsync(compositeEntry, cancellationToken).ConfigureAwait(false);
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

}

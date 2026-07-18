namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Reads the fixed SDK/autofix player avatar Unit resource without requiring its mesh Composite to parse.
public sealed class SdkStyleAvatarRigReader
{
	private const int BonesReferenceOffset = 8;
	private const int StateMachineReferenceOffset = 32;
	private readonly IGameDataPackageResolver resolver;
	private readonly IPatchTocScanner tocScanner;

	public SdkStyleAvatarRigReader(IGameDataPackageResolver resolver, IPatchTocScanner? tocScanner = null)
	{
		this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
		this.tocScanner = tocScanner ?? new PatchTocScanner();
	}

	public async ValueTask<SdkStyleAvatarRigResource> ReadAsync(
		string archiveName = SdkStyleAvatarRigConstants.AvatarArchiveName,
		AssetKey? avatarUnitAssetKey = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
		var key = avatarUnitAssetKey ?? SdkStyleAvatarRigConstants.AvatarUnitAssetKey;
		if (key.TypeId != PatchUnitMeshReader.UnitTypeId)
		{
			throw new InvalidDataException("The requested avatar rig asset is not a Unit resource.");
		}

		var toc = await resolver.GetPackageTocAsync(archiveName, cancellationToken).ConfigureAwait(false)
			?? throw new FileNotFoundException($"Could not resolve avatar archive '{archiveName}'.", archiveName);
		var entries = tocScanner.ScanEntries(toc.Data, archiveName, toc.UsesSlimEntryOffset);
		var entry = entries.SingleOrDefault(candidate => candidate.AssetKey == key)
			?? throw new KeyNotFoundException($"Avatar Unit 0x{key.TypeId:x16}/0x{key.FileId:x16} was not found in archive '{archiveName}'.");
		var tocData = await ReadRequiredResourceAsync(archiveName, entry.TocDataOffset, entry.TocDataSize, cancellationToken).ConfigureAwait(false);
		var streamData = await ReadOptionalResourceAsync(archiveName + ".stream", entry.StreamOffset, entry.StreamSize, cancellationToken).ConfigureAwait(false);
		var gpuData = await ReadOptionalResourceAsync(archiveName + ".gpu_resources", entry.GpuResourceOffset, entry.GpuResourceSize, cancellationToken).ConfigureAwait(false);
		var payload = new PatchEntryPayload(entry, tocData, streamData, gpuData);
		return new SdkStyleAvatarRigResource(
			key,
			archiveName,
			payload,
			ReadReference(tocData, BonesReferenceOffset, "Bones"),
			ReadReference(tocData, StateMachineReferenceOffset, "StateMachine"),
			UnitMeshReader.ReadTransformInfoFromUnitToc(tocData));
	}

	private async ValueTask<byte[]> ReadRequiredResourceAsync(string archiveName, ulong offset, uint size, CancellationToken cancellationToken)
	{
		if (size == 0)
		{
			throw new InvalidDataException("Avatar Unit entry has an empty TOC payload.");
		}

		return await ReadResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask<byte[]> ReadOptionalResourceAsync(string archiveName, ulong offset, uint size, CancellationToken cancellationToken)
		=> size == 0 ? Array.Empty<byte>() : await ReadResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);

	private async ValueTask<byte[]> ReadResourceAsync(string archiveName, ulong offset, uint size, CancellationToken cancellationToken)
	{
		var data = await resolver.GetPackageResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);
		if (data is null || data.Length < size)
		{
			throw new EndOfStreamException($"Could not read payload at offset {offset} size {size} from archive '{archiveName}'.");
		}

		return data.Length == size ? data : data.AsSpan(0, checked((int)size)).ToArray();
	}

	private static ulong ReadReference(ReadOnlySpan<byte> tocData, int offset, string name)
	{
		if (tocData.Length < offset + sizeof(ulong))
		{
			throw new InvalidDataException($"Avatar Unit TocData is too short to read its {name} reference.");
		}

		return BitConverter.ToUInt64(tocData.Slice(offset, sizeof(ulong)));
	}
}
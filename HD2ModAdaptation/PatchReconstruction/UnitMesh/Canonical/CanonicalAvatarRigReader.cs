namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Reads the authoritative player Avatar TransformInfo for the self-contained Canonical reconstruction route.
public sealed class CanonicalAvatarRigReader
{
	public const string AvatarArchiveName = "18235e0c9ec0e636";
	public const ulong AvatarUnitFileId = 5556372446766824087;

	private readonly IGameDataPackageResolver resolver;
	private readonly IPatchTocScanner tocScanner;

	public CanonicalAvatarRigReader(IGameDataPackageResolver resolver, IPatchTocScanner? tocScanner = null)
	{
		this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
		this.tocScanner = tocScanner ?? new PatchTocScanner();
	}

	public async ValueTask<UnitTransformInfo> ReadTransformInfoAsync(
		string archiveName = AvatarArchiveName,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
		var toc = await resolver.GetPackageTocAsync(archiveName, cancellationToken).ConfigureAwait(false)
			?? throw new FileNotFoundException($"Could not resolve Canonical Avatar archive '{archiveName}'.", archiveName);
		var entries = tocScanner.ScanEntries(toc.Data, archiveName, toc.UsesSlimEntryOffset);
		var key = new AssetKey(PatchUnitMeshReader.UnitTypeId, AvatarUnitFileId);
		var entry = entries.SingleOrDefault(candidate => candidate.AssetKey == key)
			?? throw new KeyNotFoundException($"Canonical Avatar Unit 0x{AvatarUnitFileId:x16} was not found in archive '{archiveName}'.");
		if (entry.TocDataSize == 0) throw new InvalidDataException("Canonical Avatar Unit has an empty TocData payload.");
		var data = await resolver.GetPackageResourceAsync(archiveName, entry.TocDataOffset, entry.TocDataSize, cancellationToken).ConfigureAwait(false);
		if (data is null || data.Length < entry.TocDataSize)
			throw new EndOfStreamException("Could not read Canonical Avatar Unit TocData.");
		return UnitMeshReader.ReadTransformInfoFromUnitToc(data.AsSpan(0, checked((int)entry.TocDataSize)));
	}
}
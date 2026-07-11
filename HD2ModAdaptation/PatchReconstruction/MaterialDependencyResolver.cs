namespace HD2ModAdaptation.PatchReconstruction;

// Purpose: Resolves complete material and texture payloads from source patches or installed game archives.
public sealed class MaterialDependencyResolver
{
	public const ulong MaterialTypeId = 0xeac0b497876adedf;
	public const ulong TextureTypeId = 0xcd4238c6a0c69e32;
	private readonly IPatchTocScanner tocScanner;
	private readonly IPatchEntryPayloadReader patchPayloadReader;
	private readonly StingrayMaterialReferenceReader materialReader;
	private readonly Func<string, IGameDataPackageResolver> gameResolverFactory;

	public MaterialDependencyResolver(
		IPatchTocScanner? tocScanner = null,
		IPatchEntryPayloadReader? patchPayloadReader = null,
		StingrayMaterialReferenceReader? materialReader = null,
		Func<string, IGameDataPackageResolver>? gameResolverFactory = null)
	{
		this.tocScanner = tocScanner ?? new PatchTocScanner();
		this.patchPayloadReader = patchPayloadReader ?? new PatchEntryPayloadReader();
		this.materialReader = materialReader ?? new StingrayMaterialReferenceReader();
		this.gameResolverFactory = gameResolverFactory ?? (directory => new GameDataPackageResolver(directory));
	}

	public async ValueTask<MaterialDependencyResolutionResult> ResolveAsync(
		IReadOnlyCollection<ulong> materialIds,
		IReadOnlyList<PatchTocEntry> sourcePatchEntries,
		string gameDataDirectory,
		IReadOnlyDictionary<AssetKey, IReadOnlyList<string>> preferredArchivesByAsset,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(materialIds);
		ArgumentNullException.ThrowIfNull(sourcePatchEntries);
		ArgumentNullException.ThrowIfNull(preferredArchivesByAsset);
		ArgumentException.ThrowIfNullOrWhiteSpace(gameDataDirectory);
		var sourceEntries = sourcePatchEntries.ToDictionary(entry => entry.AssetKey);
		var resolver = gameResolverFactory(gameDataDirectory);
		var entries = new Dictionary<AssetKey, PatchArchiveAdditionalEntry>();
		var texturesByMaterial = new Dictionary<ulong, IReadOnlyList<ulong>>();
		var failures = new Dictionary<ulong, string>();
		var origins = new Dictionary<AssetKey, MaterialDependencyOrigin>();
		foreach (var materialId in materialIds.Distinct().OrderBy(id => id))
		{
			var materialKey = new AssetKey(MaterialTypeId, materialId);
			var material = await ResolvePayloadAsync(materialKey, sourceEntries, resolver, preferredArchivesByAsset, cancellationToken).ConfigureAwait(false);
			if (material is null) { failures[materialId] = "Material entry was not found in source patch or game archives."; continue; }
			IReadOnlyList<ulong> textureIds;
			try { textureIds = materialReader.ReadTextureIds(material.TocData); }
			catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException) { failures[materialId] = $"Material texture references could not be read: {exception.Message}"; continue; }
			var missing = new List<ulong>();
			foreach (var textureId in textureIds.Distinct().OrderBy(id => id))
			{
				var textureKey = new AssetKey(TextureTypeId, textureId);
				var texture = await ResolvePayloadAsync(textureKey, sourceEntries, resolver, preferredArchivesByAsset, cancellationToken).ConfigureAwait(false);
				if (texture is null) { missing.Add(textureId); continue; }
				if (entries.TryAdd(textureKey, ToAdditionalEntry(texture))) origins[textureKey] = texture.Origin;
			}
			if (missing.Count > 0) { failures[materialId] = $"Missing texture entries: {string.Join(", ", missing.Select(id => $"0x{id:x16}"))}."; continue; }
			texturesByMaterial[materialId] = textureIds;
			if (entries.TryAdd(materialKey, ToAdditionalEntry(material))) origins[materialKey] = material.Origin;
		}
		return new MaterialDependencyResolutionResult(entries.Values.ToArray(), texturesByMaterial, failures, origins);
	}

	private async ValueTask<ResolvedPayload?> ResolvePayloadAsync(AssetKey key, IReadOnlyDictionary<AssetKey, PatchTocEntry> sourceEntries, IGameDataPackageResolver resolver, IReadOnlyDictionary<AssetKey, IReadOnlyList<string>> preferredArchives, CancellationToken cancellationToken)
	{
		if (sourceEntries.TryGetValue(key, out var entry))
		{
			var payload = await patchPayloadReader.ReadPayloadAsync(entry, cancellationToken).ConfigureAwait(false);
			return new ResolvedPayload(key, payload.TocData, payload.StreamData, payload.GpuResourceData, entry.Unknown1, entry.Unknown2, entry.Unknown3, entry.Unknown4, new MaterialDependencyOrigin(MaterialDependencyOriginKind.SourcePatch, entry.SourceFilePath));
		}
		if (preferredArchives.TryGetValue(key, out var names))
		{
			foreach (var name in names) { var payload = await TryReadGamePayloadAsync(key, resolver, name, cancellationToken).ConfigureAwait(false); if (payload is not null) return payload; }
		}
		foreach (var name in await resolver.GetPackageNamesAsync(cancellationToken).ConfigureAwait(false))
		{
			if (name.EndsWith(".stream", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".gpu_resources", StringComparison.OrdinalIgnoreCase)) continue;
			var payload = await TryReadGamePayloadAsync(key, resolver, name, cancellationToken).ConfigureAwait(false);
			if (payload is not null) return payload;
		}
		return null;
	}

	private async ValueTask<ResolvedPayload?> TryReadGamePayloadAsync(AssetKey key, IGameDataPackageResolver resolver, string packageName, CancellationToken cancellationToken)
	{
		GameDataPackageToc? toc;
		try { toc = await resolver.GetPackageTocAsync(packageName, cancellationToken).ConfigureAwait(false); }
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException) { return null; }
		if (toc is null) return null;
		IReadOnlyList<PatchTocEntry> entries;
		try { entries = tocScanner.ScanEntries(toc.Data, Path.GetFileName(packageName), toc.UsesSlimEntryOffset); }
		catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException) { return null; }
		var entry = entries.FirstOrDefault(candidate => candidate.AssetKey == key);
		if (entry is null) return null;
		var tocData = await ReadRequiredAsync(resolver, entry.SourceFileName, entry.TocDataOffset, entry.TocDataSize, cancellationToken).ConfigureAwait(false);
		if (tocData is null) return null;
		var streamData = await ReadOptionalAsync(resolver, entry.SourceFileName + ".stream", entry.StreamOffset, entry.StreamSize, cancellationToken).ConfigureAwait(false);
		var gpuData = await ReadOptionalAsync(resolver, entry.SourceFileName + ".gpu_resources", entry.GpuResourceOffset, entry.GpuResourceSize, cancellationToken).ConfigureAwait(false);
		return new ResolvedPayload(key, tocData, streamData, gpuData, entry.Unknown1, entry.Unknown2, entry.Unknown3, entry.Unknown4, new MaterialDependencyOrigin(MaterialDependencyOriginKind.GameArchive, packageName));
	}

	private static async ValueTask<byte[]?> ReadRequiredAsync(IGameDataPackageResolver resolver, string packageName, ulong offset, uint size, CancellationToken cancellationToken) => size == 0 ? null : Trim(await resolver.GetPackageResourceAsync(packageName, offset, size, cancellationToken).ConfigureAwait(false), size);
	private static async ValueTask<byte[]> ReadOptionalAsync(IGameDataPackageResolver resolver, string packageName, ulong offset, uint size, CancellationToken cancellationToken) => size == 0 ? Array.Empty<byte>() : Trim(await resolver.GetPackageResourceAsync(packageName, offset, size, cancellationToken).ConfigureAwait(false), size) ?? Array.Empty<byte>();
	private static byte[]? Trim(byte[]? data, uint size) => data is null || data.Length < size ? null : data.Length == size ? data : data.AsSpan(0, checked((int)size)).ToArray();
	private static PatchArchiveAdditionalEntry ToAdditionalEntry(ResolvedPayload payload) => new(payload.Key, payload.TocData, payload.StreamData, payload.GpuData, payload.Unknown1, payload.Unknown2, payload.Unknown3, payload.Unknown4);
	private sealed record ResolvedPayload(AssetKey Key, byte[] TocData, byte[] StreamData, byte[] GpuData, ulong Unknown1, ulong Unknown2, uint Unknown3, uint Unknown4, MaterialDependencyOrigin Origin);
}

public enum MaterialDependencyOriginKind { SourcePatch, GameArchive }

public sealed record MaterialDependencyOrigin(MaterialDependencyOriginKind Kind, string Name);

public sealed record MaterialDependencyResolutionResult(
	IReadOnlyList<PatchArchiveAdditionalEntry> Entries,
	IReadOnlyDictionary<ulong, IReadOnlyList<ulong>> TextureIdsByMaterial,
	IReadOnlyDictionary<ulong, string> RejectedMaterialReasons,
	IReadOnlyDictionary<AssetKey, MaterialDependencyOrigin> Origins);
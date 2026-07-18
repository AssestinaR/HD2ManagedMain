using HD2ModCore.Application;
using HD2ModCore.Domain;
using AdaptationPatchArchiveAdditionalEntry = HD2ModAdaptation.PatchReconstruction.PatchArchiveAdditionalEntry;

namespace HD2ModCore.Infrastructure;

// Purpose: Resolves material and texture payloads from patch entries first, then from game archives like the SDK global TOC lookup.
public sealed class ArchiveDependencyResolver
{
	private readonly IPatchTocScanner tocScanner;
	private readonly IPatchEntryPayloadReader patchPayloadReader;
	private readonly StingrayMaterialReferenceReader materialReferenceReader;

	public ArchiveDependencyResolver(
		IPatchTocScanner tocScanner,
		IPatchEntryPayloadReader patchPayloadReader,
		StingrayMaterialReferenceReader materialReferenceReader)
	{
		this.tocScanner = tocScanner ?? throw new ArgumentNullException(nameof(tocScanner));
		this.patchPayloadReader = patchPayloadReader ?? throw new ArgumentNullException(nameof(patchPayloadReader));
		this.materialReferenceReader = materialReferenceReader ?? throw new ArgumentNullException(nameof(materialReferenceReader));
	}

	public async ValueTask<ArchiveDependencyResolutionResult> ResolveMaterialClosureAsync(
		IReadOnlyCollection<ulong> materialIds,
		IReadOnlyList<PatchTocEntry> patchEntries,
		string gameDataDirectory,
		IReadOnlyDictionary<AssetKey, IReadOnlyList<string>> preferredArchivesByAsset,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(materialIds);
		ArgumentNullException.ThrowIfNull(patchEntries);
		ArgumentNullException.ThrowIfNull(preferredArchivesByAsset);

		if (string.IsNullOrWhiteSpace(gameDataDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameDataDirectory));
		}

		var resolver = new GameDataPackageResolver(gameDataDirectory);
		var patchEntryByKey = patchEntries.ToDictionary(entry => entry.AssetKey);
		var resolved = new Dictionary<AssetKey, AdaptationPatchArchiveAdditionalEntry>();
		var origins = new Dictionary<AssetKey, ArchiveDependencyPayloadOrigin>();
		var materialTextureIds = new Dictionary<ulong, IReadOnlyList<ulong>>();
		var rejectedReasons = new Dictionary<ulong, string>();

		foreach (var materialId in materialIds.Distinct().OrderBy(id => id))
		{
			var materialKey = new AssetKey(MaterialDependencyValidator.MaterialTypeId, materialId);
			var materialPayload = await ResolvePayloadAsync(materialKey, patchEntryByKey, resolver, preferredArchivesByAsset, cancellationToken).ConfigureAwait(false);
			if (materialPayload is null)
			{
				rejectedReasons[materialId] = "Material entry was not found in source patch or game archives.";
				continue;
			}

			IReadOnlyList<ulong> textureIds;
			try
			{
				textureIds = materialReferenceReader.ReadTextureIds(materialPayload.TocData);
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
			{
				rejectedReasons[materialId] = $"Material texture references could not be read: {ex.Message}";
				continue;
			}

			var missingTextures = new List<ulong>();
			foreach (var textureId in textureIds.Distinct().OrderBy(id => id))
			{
				var textureKey = new AssetKey(MaterialDependencyValidator.TextureTypeId, textureId);
				var texturePayload = await ResolvePayloadAsync(textureKey, patchEntryByKey, resolver, preferredArchivesByAsset, cancellationToken).ConfigureAwait(false);
				if (texturePayload is null)
				{
					missingTextures.Add(textureId);
					continue;
				}

				if (resolved.TryAdd(textureKey, ToAdditionalEntry(texturePayload)))
				{
					origins[textureKey] = texturePayload.Origin;
				}
			}

			if (missingTextures.Count > 0)
			{
				rejectedReasons[materialId] = $"Missing texture entries: {string.Join(", ", missingTextures.Select(textureId => $"0x{textureId:x16}"))}.";
				continue;
			}

			materialTextureIds[materialId] = textureIds;
			if (resolved.TryAdd(materialKey, ToAdditionalEntry(materialPayload)))
			{
				origins[materialKey] = materialPayload.Origin;
			}
		}

		return new ArchiveDependencyResolutionResult(resolved.Values.ToArray(), materialTextureIds, rejectedReasons, origins);
	}

	private async ValueTask<ResolvedArchivePayload?> ResolvePayloadAsync(
		AssetKey assetKey,
		IReadOnlyDictionary<AssetKey, PatchTocEntry> patchEntryByKey,
		GameDataPackageResolver resolver,
		IReadOnlyDictionary<AssetKey, IReadOnlyList<string>> preferredArchivesByAsset,
		CancellationToken cancellationToken)
	{
		if (patchEntryByKey.TryGetValue(assetKey, out var patchEntry))
		{
			var payload = await patchPayloadReader.ReadPayloadAsync(patchEntry, cancellationToken).ConfigureAwait(false);
			var origin = new ArchiveDependencyPayloadOrigin(ArchiveDependencyPayloadOriginKind.SourcePatch, patchEntry.SourceFilePath);
			return new ResolvedArchivePayload(assetKey, payload.TocData, payload.StreamData, payload.GpuResourceData, patchEntry.Unknown1, patchEntry.Unknown2, patchEntry.Unknown3, patchEntry.Unknown4, origin);
		}

		if (preferredArchivesByAsset.TryGetValue(assetKey, out var archiveNames))
		{
			foreach (var archiveName in archiveNames)
			{
				var payload = await TryResolveGamePayloadAsync(assetKey, resolver, archiveName, cancellationToken).ConfigureAwait(false);
				if (payload is not null)
				{
					return payload;
				}
			}
		}

		foreach (var packageName in await resolver.GetPackageNamesAsync(cancellationToken).ConfigureAwait(false))
		{
			if (packageName.EndsWith(".stream", StringComparison.OrdinalIgnoreCase)
				|| packageName.EndsWith(".gpu_resources", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var payload = await TryResolveGamePayloadAsync(assetKey, resolver, packageName, cancellationToken).ConfigureAwait(false);
			if (payload is not null)
			{
				return payload;
			}
		}

		return null;
	}

	private async ValueTask<ResolvedArchivePayload?> TryResolveGamePayloadAsync(
		AssetKey assetKey,
		GameDataPackageResolver resolver,
		string archiveName,
		CancellationToken cancellationToken)
	{
		GameDataPackageToc? toc;
		try
		{
			toc = await resolver.GetPackageTocAsync(archiveName, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
		{
			return null;
		}

		if (toc is null)
		{
			return null;
		}

		IReadOnlyList<PatchTocEntry> entries;
		try
		{
			entries = tocScanner.ScanEntries(toc.Data, Path.GetFileName(archiveName), toc.UsesSlimEntryOffset);
		}
		catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
		{
			return null;
		}

		var entry = entries.FirstOrDefault(candidate => candidate.AssetKey == assetKey);
		if (entry is null)
		{
			return null;
		}

		var tocData = await ReadRequiredResourceAsync(resolver, entry.SourceFileName, entry.TocDataOffset, entry.TocDataSize, cancellationToken).ConfigureAwait(false);
		if (tocData is null)
		{
			return null;
		}

		var streamData = await ReadOptionalResourceAsync(resolver, entry.SourceFileName + ".stream", entry.StreamOffset, entry.StreamSize, cancellationToken).ConfigureAwait(false);
		var gpuResourceData = await ReadOptionalResourceAsync(resolver, entry.SourceFileName + ".gpu_resources", entry.GpuResourceOffset, entry.GpuResourceSize, cancellationToken).ConfigureAwait(false);
		var origin = new ArchiveDependencyPayloadOrigin(ArchiveDependencyPayloadOriginKind.GameArchive, archiveName);
		return new ResolvedArchivePayload(assetKey, tocData, streamData, gpuResourceData, entry.Unknown1, entry.Unknown2, entry.Unknown3, entry.Unknown4, origin);
	}

	private static async ValueTask<byte[]?> ReadRequiredResourceAsync(GameDataPackageResolver resolver, string archiveName, ulong offset, uint size, CancellationToken cancellationToken)
	{
		if (size == 0)
		{
			return null;
		}

		var data = await resolver.GetPackageResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);
		return data is null || data.Length < size ? null : Trim(data, size);
	}

	private static async ValueTask<byte[]> ReadOptionalResourceAsync(GameDataPackageResolver resolver, string archiveName, ulong offset, uint size, CancellationToken cancellationToken)
	{
		if (size == 0)
		{
			return Array.Empty<byte>();
		}

		var data = await resolver.GetPackageResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);
		return data is null || data.Length < size ? Array.Empty<byte>() : Trim(data, size);
	}

	private static byte[] Trim(byte[] data, uint size)
		=> data.Length == size ? data : data.AsSpan(0, checked((int)size)).ToArray();

	private static AdaptationPatchArchiveAdditionalEntry ToAdditionalEntry(ResolvedArchivePayload payload)
		=> new(
			new HD2ModAdaptation.PatchReconstruction.AssetKey(payload.AssetKey.TypeId, payload.AssetKey.FileId),
			payload.TocData,
			payload.StreamData,
			payload.GpuResourceData,
			payload.Unknown1,
			payload.Unknown2,
			payload.Unknown3,
			payload.Unknown4);

	private sealed record ResolvedArchivePayload(
		AssetKey AssetKey,
		byte[] TocData,
		byte[] StreamData,
		byte[] GpuResourceData,
		ulong Unknown1,
		ulong Unknown2,
		uint Unknown3,
		uint Unknown4,
		ArchiveDependencyPayloadOrigin Origin);
}

public enum ArchiveDependencyPayloadOriginKind
{
	SourcePatch,
	GameArchive
}

public sealed record ArchiveDependencyPayloadOrigin(ArchiveDependencyPayloadOriginKind Kind, string Name);

public sealed record ArchiveDependencyResolutionResult(
	IReadOnlyList<AdaptationPatchArchiveAdditionalEntry> Entries,
	IReadOnlyDictionary<ulong, IReadOnlyList<ulong>> MaterialTextureIds,
	IReadOnlyDictionary<ulong, string> RejectedMaterialReasons,
	IReadOnlyDictionary<AssetKey, ArchiveDependencyPayloadOrigin> Origins);
using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using CoreAssetKey = HD2ModCore.Domain.AssetKey;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using AdaptationGameDataPackageResolver = HD2ModAdaptation.PatchReconstruction.GameDataPackageResolver;
using AdaptationGameDataUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.GameDataUnitMeshReader;

namespace HD2ModCore.Infrastructure;

// Purpose: Resolves one persisted Mod mesh section to its current Game Data material without copying original resources.
public sealed class CurrentGameMaterialFallbackResolver
{
	private readonly IAssetArchiveIndexService indexService;
	private readonly Dictionary<SectionKey, SectionResolution?> cache = new();

	public CurrentGameMaterialFallbackResolver(IAssetArchiveIndexService indexService)
	{
		this.indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
	}

	public async ValueTask<SectionResolution?> ResolveAsync(PatchAssetReference reference, CancellationToken cancellationToken = default)
	{
		if (reference.Kind != PatchReferenceKind.UnitMaterial || reference.MeshInfoIndex is null || reference.ReferenceIndex is null)
		{
			return null;
		}

		var unitKey = new CoreAssetKey(reference.SourceAssetKey.TypeId, reference.SourceAssetKey.FileId);
		var key = new SectionKey(unitKey, reference.MeshInfoIndex.Value, reference.ReferenceIndex.Value);
		if (cache.TryGetValue(key, out var cached)) return cached;

		var fingerprint = await indexService.GetFingerprintAsync(cancellationToken).ConfigureAwait(false);
		if (fingerprint is null || string.IsNullOrWhiteSpace(fingerprint.GameDataDirectory) || !Directory.Exists(fingerprint.GameDataDirectory))
		{
			cache[key] = null;
			return null;
		}

		var matches = await indexService.FindAssetArchivesAsync(new HashSet<CoreAssetKey> { unitKey }, cancellationToken).ConfigureAwait(false);
		var archive = matches.SingleOrDefault()?.Archives.FirstOrDefault();
		if (archive is null)
		{
			cache[key] = null;
			return null;
		}

		try
		{
			var target = await new AdaptationGameDataUnitMeshReader(new AdaptationGameDataPackageResolver(fingerprint.GameDataDirectory)).ReadAsync(
				archive.ArchiveId,
				new AdaptationAssetKey(unitKey.TypeId, unitKey.FileId),
				allowGlobalDependencySearch: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			var mesh = target.Model.Meshes.FirstOrDefault(item => item.Index == reference.MeshInfoIndex.Value);
			if (mesh is null || reference.ReferenceIndex.Value < 0 || reference.ReferenceIndex.Value >= mesh.Sections.Count)
			{
				cache[key] = null;
				return null;
			}

			var slot = mesh.Sections[reference.ReferenceIndex.Value].MaterialSlotId;
			var materialIds = target.Model.Materials.Where(binding => binding.SectionId == slot).Select(binding => binding.MaterialId).Distinct().ToArray();
			if (materialIds.Length != 1)
			{
				cache[key] = null;
				return null;
			}

			var result = new SectionResolution(new CoreAssetKey(MaterialDependencyResolver.MaterialTypeId, materialIds[0]), archive.DisplayName, reference.IsPlaceholderMesh);
			cache[key] = result;
			return result;
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
		{
			cache[key] = null;
			return null;
		}
	}

	public sealed record SectionResolution(CoreAssetKey MaterialAssetKey, string ArchiveName, bool IsPlaceholderMesh);
	private sealed record SectionKey(CoreAssetKey UnitAssetKey, int MeshInfoIndex, int SectionIndex);
}
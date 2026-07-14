using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Analysis;

// Purpose: Builds the first conservative Item resource relation table from explicit archive-index facts.
public sealed class GameItemResourceRelationBuilder : IGameItemResourceRelationBuilder
{
	private const ulong BoneTypeId = PatchUnitMeshReader.BoneTypeId;

	public ValueTask<IReadOnlyList<GameItemResourceInfo>> BuildAsync(
		GameDataArchiveIndex archiveIndex,
		IReadOnlyList<GameItemInput> items,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(archiveIndex);
		ArgumentNullException.ThrowIfNull(items);
		var result = new List<GameItemResourceInfo>(items.Count);
		foreach (var item in items)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ArgumentException.ThrowIfNullOrWhiteSpace(item.ItemName);
			var issues = new List<PatchAnalysisIssue>();
			var resources = new List<ResourceDependencyFact>();
			var archives = item.ArchiveNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			if (archives.Length == 0)
			{
				issues.Add(new PatchAnalysisIssue("MissingItemArchive", $"Item '{item.ItemName}' has no explicit archive.", item.ItemName));
			}
			foreach (var asset in item.DirectAssets ?? Array.Empty<AssetKey>())
			{
				var matches = archiveIndex.FindArchivesByAsset(asset).Where(entry => archives.Contains(entry.PackageName, StringComparer.OrdinalIgnoreCase)).ToArray();
				if (matches.Length == 0)
				{
					issues.Add(new PatchAnalysisIssue("MissingDirectAsset", $"Direct asset 0x{asset.TypeId:x16}/0x{asset.FileId:x16} was not found in the item's explicit archives.", item.ItemName, asset));
				}
				else
				{
					resources.Add(new ResourceDependencyFact(asset, GetResourceKind(asset.TypeId), matches[0].PackageName, true, true));
				}
			}

			var candidates = (item.CandidateUnitAssets ?? Array.Empty<AssetKey>()).Distinct().ToArray();
			if (candidates.Length == 0)
			{
				candidates = archiveIndex.FindEntriesByType(PatchUnitMeshReader.UnitTypeId)
					.Where(entry => archives.Contains(entry.PackageName, StringComparer.OrdinalIgnoreCase))
					.Select(entry => entry.AssetKey).Distinct().ToArray();
			}
			if (candidates.Length == 0)
			{
				issues.Add(new PatchAnalysisIssue("MissingUnitCandidate", $"No Unit candidate was found for item '{item.ItemName}'.", item.ItemName));
			}
			else if (candidates.Length > 1)
			{
				issues.Add(new PatchAnalysisIssue("AmbiguousUnitCandidate", $"Item '{item.ItemName}' has {candidates.Length} Unit candidates; no candidate was selected implicitly.", item.ItemName));
			}
			foreach (var candidate in candidates)
			{
				var match = archiveIndex.FindArchivesByAsset(candidate).FirstOrDefault(entry => archives.Contains(entry.PackageName, StringComparer.OrdinalIgnoreCase));
				resources.Add(new ResourceDependencyFact(candidate, "Unit", match?.PackageName, true, match is not null));
				if (match is null) issues.Add(new PatchAnalysisIssue("MissingUnitCandidate", $"Unit candidate 0x{candidate.FileId:x16} was not found in the item's explicit archives.", item.ItemName, candidate));
			}
			result.Add(new GameItemResourceInfo(item.ItemName, item.Category, archives, resources, candidates, issues));
		}
		return ValueTask.FromResult<IReadOnlyList<GameItemResourceInfo>>(result);
	}

	private static string GetResourceKind(ulong typeId) => typeId switch
	{
		PatchUnitMeshReader.UnitTypeId => "Unit",
		PatchUnitMeshReader.CompositeUnitTypeId => "Composite",
		BoneTypeId => "Bone",
		MaterialDependencyResolver.MaterialTypeId => "Material",
		MaterialDependencyResolver.TextureTypeId => "Texture",
		_ => "Asset"
	};
}

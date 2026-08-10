using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Coordinates safe Adaptation material packaging operations for flat Core library nodes.
public sealed class MaterialPackagingApplicationService : IMaterialPackagingApplicationService
{
	private readonly IPatchFileNameParser fileNameParser;
	private readonly MaterialPackagingService packagingService;
	private readonly IModInformationCenter? informationCenter;

	public MaterialPackagingApplicationService(IPatchFileNameParser fileNameParser, MaterialPackagingService? packagingService = null, IModInformationCenter? informationCenter = null)
	{
		this.fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		this.packagingService = packagingService ?? new MaterialPackagingService();
		this.informationCenter = informationCenter;
	}

	public string? GetSinglePatchTocPath(ModNode source, string modsRootDirectory)
	{
		ArgumentNullException.ThrowIfNull(source);
		return FindBasePatchPaths(source, modsRootDirectory).SingleOrDefault();
	}

	public async ValueTask<ModMaterialPackagingState> InspectAsync(ModNode source, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var patchPaths = FindBasePatchPaths(source, modsRootDirectory);
		if (patchPaths.Count != 1)
		{
			return new ModMaterialPackagingState(source.Id, null, false, false, false, 0, 0, 0, 0, new[] { patchPaths.Count == 0 ? "Mod 没有 Patch 主文件。" : "当前版本仅支持只含一个 Patch 文件组的 Mod。" });
		}
		var graph = await TryLoadGraphAsync(source, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		if (graph is not null)
			return BuildStateFromGraph(source, patchPaths[0], graph);
		var inspection = await packagingService.InspectAsync(patchPaths[0], cancellationToken).ConfigureAwait(false);
		var split = packagingService.PlanSplit(inspection);
		return new ModMaterialPackagingState(source.Id, patchPaths[0], split.IsApproved, inspection.EmbeddedMaterialAssetKeys.Count != 0, inspection.ExternalMaterialAssetKeys.Count != 0, inspection.RequiredMaterialAssetKeys.Count, inspection.EmbeddedMaterialAssetKeys.Count, inspection.ExternalMaterialAssetKeys.Count, inspection.EmbeddedTextureClosureAssetKeys.Count, split.Blockers);
	}

	public async ValueTask<IReadOnlyList<MaterialPackageCandidate>> FindCandidatesAsync(ModNode source, IReadOnlyCollection<ModNode> libraryNodes, string modsRootDirectory, bool requireAllExternalMaterials, CancellationToken cancellationToken = default)
	{
		var sourceState = await InspectAsync(source, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		if (sourceState.PatchTocPath is null) return Array.Empty<MaterialPackageCandidate>();
		var sourceGraph = await TryLoadGraphAsync(source, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		var results = new List<MaterialPackageCandidate>();
		foreach (var candidate in libraryNodes.Where(node => node.Id != source.Id).OrderBy(node => node.Metadata.Name, StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var paths = FindBasePatchPaths(candidate, modsRootDirectory);
			if (paths.Count != 1) continue;
			var candidateGraph = await TryLoadGraphAsync(candidate, modsRootDirectory, cancellationToken).ConfigureAwait(false);
			if (sourceGraph is not null && candidateGraph is not null)
			{
				var graphCompatibility = CompareGraphs(sourceGraph, candidateGraph, requireAllExternalMaterials);
				if (graphCompatibility.MatchingMaterialCount != 0)
				{
					results.Add(new MaterialPackageCandidate(candidate.Id, candidate.Metadata.Name, true, graphCompatibility.MatchingMaterialCount, graphCompatibility.MissingMaterialCount, graphCompatibility.MissingTextureCount, graphCompatibility.Blockers));
					continue;
				}
				// A cached graph can be structurally valid while omitting an asset from its
				// asset inventory. Recheck only this false-negative case using the authoritative
				// Patch parser instead of rescanning every candidate.
			}
			var compatibility = await packagingService.CheckCandidateAsync(sourceState.PatchTocPath, paths[0], requireAllExternalMaterials, cancellationToken).ConfigureAwait(false);
			if (compatibility.MatchingMaterialAssetKeys.Count == 0) continue;
			results.Add(new MaterialPackageCandidate(candidate.Id, candidate.Metadata.Name, compatibility.IsCompatible, compatibility.MatchingMaterialAssetKeys.Count, compatibility.MissingMaterialAssetKeys.Count, compatibility.MissingTextureAssetKeys.Count, compatibility.Blockers));
		}
		return results.OrderByDescending(candidate => candidate.IsCompatible).ThenByDescending(candidate => candidate.MatchingMaterialCount).ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public async ValueTask<MaterialPackagingOperationResult> SplitAsync(ModNode source, string modsRootDirectory, string outputRootDirectory, CancellationToken cancellationToken = default)
	{
		var state = await InspectAsync(source, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		if (state.PatchTocPath is null || !state.CanSplit) return Failure(source.Id, state.Blockers);
		try
		{
			var result = await packagingService.SplitAsync(state.PatchTocPath, Path.Combine(outputRootDirectory, Sanitize(source.Metadata.Name) + "-模型"), Path.Combine(outputRootDirectory, Sanitize(source.Metadata.Name) + "-材质"), cancellationToken).ConfigureAwait(false);
			return ToResult(result);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
		{
			return Failure(source.Id, new[] { exception.Message });
		}
	}

	public async ValueTask<MaterialPackagingOperationResult> MergeAsync(ModNode source, ModNode candidate, string modsRootDirectory, string outputDirectory, bool requireAllExternalMaterials, bool onlyReferencedAssets = true, bool replaceExistingMaterials = true, CancellationToken cancellationToken = default)
	{
		var sourcePaths = FindBasePatchPaths(source, modsRootDirectory);
		var candidatePaths = FindBasePatchPaths(candidate, modsRootDirectory);
		if (sourcePaths.Count != 1 || candidatePaths.Count != 1) return Failure(source.Id, new[] { "源 Mod 和材质包都必须只含一个 Patch 文件组。" });
		try
		{
			var result = await packagingService.MergeAsync(sourcePaths[0], candidatePaths[0], outputDirectory, requireAllExternalMaterials, onlyReferencedAssets, replaceExistingMaterials, cancellationToken).ConfigureAwait(false);
			return ToResult(result);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
		{
			return Failure(source.Id, new[] { exception.Message });
		}
	}

	private async ValueTask<ReferenceGraphFacts?> TryLoadGraphAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken)
	{
		if (informationCenter is null) return null;
		var result = await informationCenter.RequestReferenceGraphAsync(
			node,
			modsRootDirectory,
			new ModInformationRequest(ModInformationKind.ReferenceGraph, "MaterialPackaging"),
			cancellationToken).ConfigureAwait(false);
		return result.Data;
	}

	private static ModMaterialPackagingState BuildStateFromGraph(ModNode source, string patchPath, ReferenceGraphFacts graph)
	{
		var assets = graph.Analyses.SelectMany(analysis => analysis.Assets).Select(asset => asset.AssetKey).ToHashSet();
		var materials = assets.Where(key => key.TypeId == MaterialDependencyResolver.MaterialTypeId).ToHashSet();
		var textures = assets.Where(key => key.TypeId == MaterialDependencyResolver.TextureTypeId).ToHashSet();
		var references = graph.Analyses.SelectMany(analysis => analysis.References).ToArray();
		var requiredMaterials = references.Where(reference => reference.Kind == PatchReferenceKind.UnitMaterial).Select(reference => reference.TargetAssetKey).ToHashSet();
		var embeddedMaterials = requiredMaterials.Where(materials.Contains).ToHashSet();
		var externalMaterials = requiredMaterials.Where(material => !materials.Contains(material)).ToHashSet();
		var embeddedTextures = references
			.Where(reference => reference.Kind == PatchReferenceKind.MaterialTexture && embeddedMaterials.Contains(reference.SourceAssetKey) && textures.Contains(reference.TargetAssetKey))
			.Select(reference => reference.TargetAssetKey)
			.ToHashSet();
		var blockers = new List<string>();
		if (materials.Count == 0) blockers.Add("当前 Patch 不包含 Material，无法生成材质包。");
		blockers.AddRange(graph.Issues.Where(issue => issue.Severity == CoreIssueSeverity.Error).Select(issue => issue.Message));
		return new ModMaterialPackagingState(source.Id, patchPath, materials.Count != 0 && blockers.Count == 0, embeddedMaterials.Count != 0, externalMaterials.Count != 0, requiredMaterials.Count, embeddedMaterials.Count, externalMaterials.Count, embeddedTextures.Count, blockers);
	}

	private static GraphCompatibility CompareGraphs(ReferenceGraphFacts source, ReferenceGraphFacts candidate, bool requireAllExternalMaterials)
	{
		var sourceAssets = source.Analyses.SelectMany(analysis => analysis.Assets).Select(asset => asset.AssetKey).ToHashSet();
		var candidateAssets = candidate.Analyses.SelectMany(analysis => analysis.Assets).Select(asset => asset.AssetKey).ToHashSet();
		var candidateMaterials = candidateAssets.Where(key => key.TypeId == MaterialDependencyResolver.MaterialTypeId).ToHashSet();
		var sourceReferences = source.Analyses.SelectMany(analysis => analysis.References).ToArray();
		var candidateReferences = candidate.Analyses.SelectMany(analysis => analysis.References).ToArray();
		var required = sourceReferences.Where(reference => reference.Kind == PatchReferenceKind.UnitMaterial).Select(reference => reference.TargetAssetKey).ToHashSet();
		var matching = required.Intersect(candidateMaterials).ToHashSet();
		var external = required.Where(key => !sourceAssets.Contains(key)).ToHashSet();
		var missingMaterials = requireAllExternalMaterials ? external.Except(candidateMaterials).Count() : 0;
		var missingTextures = candidateReferences
			.Where(reference => reference.Kind == PatchReferenceKind.MaterialTexture && matching.Contains(reference.SourceAssetKey))
			.Select(reference => reference.TargetAssetKey)
			.Where(key => !candidateAssets.Contains(key) && !sourceAssets.Contains(key))
			.ToHashSet()
			.Count;
		var blockers = new List<string>();
		if (missingMaterials != 0) blockers.Add($"候选材质包缺少 {missingMaterials} 个外部 Material。");
		if (missingTextures != 0) blockers.Add($"候选材质包缺少 {missingTextures} 个外部 Texture。");
		blockers.AddRange(candidate.Issues.Where(issue => issue.Severity == CoreIssueSeverity.Error).Select(issue => issue.Message));
		return new GraphCompatibility(matching.Count, missingMaterials, missingTextures, blockers);
	}

	private sealed record GraphCompatibility(int MatchingMaterialCount, int MissingMaterialCount, int MissingTextureCount, IReadOnlyList<string> Blockers);

	private IReadOnlyList<string> FindBasePatchPaths(ModNode node, string modsRootDirectory)
	{
		var directory = Path.Combine(modsRootDirectory, node.RelativePath);
		if (!Directory.Exists(directory)) return Array.Empty<string>();
		return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Where(path => fileNameParser.TryParse(Path.GetFileName(path), out var info) && info?.SidecarKind == PatchSidecarKind.Base).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static MaterialPackagingOperationResult ToResult(MaterialPackagingWriteResult result) => new(result.Verification.IsSuccessful, result.Outputs.Select(output => output.OutputDirectoryPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), result.Verification.ActualAssetCount, result.Verification.ActualGraphEdgeCount, result.Verification.Failures.Select(message => new CoreIssue(CoreIssueSeverity.Error, "MaterialPackagingVerificationFailed", message)).ToArray());
	private static MaterialPackagingOperationResult Failure(ModNodeId nodeId, IReadOnlyList<string> messages) => new(false, Array.Empty<string>(), 0, 0, messages.Select(message => new CoreIssue(CoreIssueSeverity.Error, "MaterialPackagingBlocked", message, NodeId: nodeId)).ToArray());
	private static string Sanitize(string name) => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
}

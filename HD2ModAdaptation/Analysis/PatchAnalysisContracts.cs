using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.Analysis;

// 作用：定义 Patch 的基础资产清单与完整结构分析两种可缓存层级。

// Purpose: Defines neutral read-only contracts for patch-group semantic analysis.
public sealed record PatchGroupInput(
	string PatchTocFilePath,
	string? StreamFilePath = null,
	string? GpuResourcesFilePath = null,
	IReadOnlyList<string>? RelatedMaterialTocFilePaths = null);

public sealed record PatchAssetFact(
	AssetKey AssetKey,
	string SourceFilePath,
	uint TocDataSize,
	uint StreamSize,
	uint GpuResourceSize,
	bool IsUnit,
	bool IsCompositeUnit,
	bool IsMaterial,
	bool IsTexture);

public sealed record PatchAnalysisIssue(
	string Code,
	string Message,
	string? SourceFilePath = null,
	AssetKey? AssetKey = null);

public enum PatchReferenceKind
{
	UnitMaterial,
	MaterialTexture
}

public enum PatchAnalysisDepth
{
	Inventory,
	DependencyGraph,
	Full,
}

public sealed record PatchAssetReference(
	AssetKey SourceAssetKey,
	AssetKey TargetAssetKey,
	PatchReferenceKind Kind,
	uint PayloadRelativeOffset,
	uint? SlotId = null,
	int? ReferenceIndex = null,
	int? MeshInfoIndex = null,
	bool IsPlaceholderMesh = false);

public sealed record PatchGroupAnalysis(
	PatchGroupInput Input,
	IReadOnlyList<PatchAssetFact> Assets,
	IReadOnlyList<PatchAssetReference> References,
	IReadOnlyList<PatchAnalysisIssue> Issues,
	DateTimeOffset AnalyzedUtc,
	string AnalyzerVersion,
	PatchAnalysisDepth Depth = PatchAnalysisDepth.Full)
{
	public bool IsSuccessful => Issues.All(issue => issue.Code is not "InvalidToc" and not "MissingToc");
}

public interface IPatchGroupAnalyzer
{
	ValueTask<PatchGroupAnalysis> AnalyzeAsync(PatchGroupInput input, CancellationToken cancellationToken = default);
}

public interface IInventoryPatchGroupAnalyzer : IPatchGroupAnalyzer
{
	ValueTask<PatchGroupAnalysis> AnalyzeInventoryAsync(PatchGroupInput input, CancellationToken cancellationToken = default);
}

public interface IDependencyGraphPatchGroupAnalyzer : IPatchGroupAnalyzer
{
	ValueTask<PatchGroupAnalysis> AnalyzeDependencyGraphAsync(PatchGroupInput input, CancellationToken cancellationToken = default);
}

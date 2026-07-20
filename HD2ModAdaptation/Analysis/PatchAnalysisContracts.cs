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

// Purpose: Identifies a readable source Unit and summarizes its rebuild-relevant mesh structure without retaining vertex or GPU payload bytes.
public sealed record SourceUnitPreparation(
	PatchTocEntry Entry,
	AssetKey? CompositeAssetKey,
	IReadOnlyList<SourceMeshPreparation> Meshes,
	string? ReadError = null)
{
	public bool IsReadable => string.IsNullOrWhiteSpace(ReadError);
}

// Purpose: Stores the stable source mesh facts needed to offer transfer candidates before payload is read again for output.
public sealed record SourceMeshPreparation(
	int MeshInfoIndex,
	uint MeshId,
	int LodIndex,
	bool IsVisual,
	bool IsTransferable,
	string SemanticName,
	string BodyType,
	string Slot,
	string PieceType,
	uint VertexCount,
	uint TriangleCount,
	uint SectionCount,
	uint VertexStride,
	IReadOnlyList<uint> MaterialSlotIds,
	IReadOnlyList<ulong> MaterialIds);

public sealed record PatchGroupAnalysis(
	PatchGroupInput Input,
	IReadOnlyList<PatchAssetFact> Assets,
	IReadOnlyList<PatchAssetReference> References,
	IReadOnlyList<PatchAnalysisIssue> Issues,
	DateTimeOffset AnalyzedUtc,
	string AnalyzerVersion,
	PatchAnalysisDepth Depth = PatchAnalysisDepth.Full,
	IReadOnlyList<PatchTocEntry>? EntryCatalog = null,
	IReadOnlyList<SourceUnitPreparation>? SourceUnits = null)
{
	public bool IsSuccessful => Issues.All(issue => issue.Code is not "InvalidToc" and not "MissingToc");
	public IReadOnlyList<PatchTocEntry> Entries => EntryCatalog ?? Array.Empty<PatchTocEntry>();
	public IReadOnlyList<SourceUnitPreparation> PreparedSourceUnits => SourceUnits ?? Array.Empty<SourceUnitPreparation>();
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

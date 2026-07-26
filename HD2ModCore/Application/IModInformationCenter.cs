using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：统一编排 Mod 派生信息请求；业务代码不直接启动派生缓存生产。
// Purpose: Orchestrates Mod-derived information requests so consumers do not start production directly.
public interface IModInformationCenter : IAsyncDisposable
{
	event EventHandler<ModInformationDiagnostic>? DiagnosticRecorded;
	event EventHandler<ModInformationProductionStarted>? ProductionStarted;

	ValueTask<ModInformationResult<PatchFileIndex>> RequestFileFactsAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		ModInformationRequest request,
		CancellationToken cancellationToken = default);

	ValueTask<ModInformationResult<ModContentFacts>> RequestAssetInventoryAsync(
		ModNode node,
		string modsRootDirectory,
		ModInformationRequest request,
		CancellationToken cancellationToken = default);

	ValueTask<ModInformationResult<ReferenceGraphFacts>> RequestReferenceGraphAsync(
		ModNode node,
		string modsRootDirectory,
		ModInformationRequest request,
		CancellationToken cancellationToken = default);

	ValueTask<ModInformationResult<MaintenanceAnalysisFacts>> RequestMaintenanceAnalysisAsync(
		ModNode node,
		string modsRootDirectory,
		ModInformationRequest request,
		CancellationToken cancellationToken = default);

	ValueTask<ModInformationResult<ModUnitVersionFacts>> RequestUnitVersionAsync(
		ModNode node,
		string modsRootDirectory,
		ModInformationRequest request,
		CancellationToken cancellationToken = default);

	ValueTask<ModInformationResult<AdvancedUnitAnalysisFacts>> RequestAdvancedUnitAnalysisAsync(
		ModNode node,
		string modsRootDirectory,
		ModInformationRequest request,
		CancellationToken cancellationToken = default);

	ValueTask<ModInformationResult<ModThumbnailFacts>> RequestThumbnailAsync(
		ModNode node,
		string modsRootDirectory,
		ModInformationRequest request,
		CancellationToken cancellationToken = default);

	ValueTask<ModDataIndexSummary> GetAssetRelationSummaryAsync(
		       IReadOnlyCollection<AssetKey> assetKeys,
		       ModNodeId? excludedNodeId = null,
		       CancellationToken cancellationToken = default);

	ValueTask InvalidateNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default);
}
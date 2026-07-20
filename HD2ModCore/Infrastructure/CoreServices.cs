using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using System.Net.Http;

namespace HD2ModCore.Infrastructure;

// 作用：提供 Core 服务实现的简单工厂方法，便于上层快速组装。
// Purpose: Simple factory helpers for core service implementations.
public static class CoreServices
{
	public static IPatchFileNameParser CreatePatchFileNameParser() => new PatchFileNameParser();
	public static IPatchTocFileCollector CreatePatchTocFileCollector() => new PatchTocFileCollector();
	public static IPatchFileIndexBuilder CreatePatchFileIndexBuilder()
		=> new PatchFileIndexBuilder(CreatePatchFileNameParser());
	public static IPatchStateScanner CreatePatchStateScanner()
		=> new PatchStateScanner(CreatePatchFileNameParser());
	public static IPatchTocScanner CreatePatchTocScanner() => new PatchTocScanner();
 	public static IPatchEntryPayloadReader CreatePatchEntryPayloadReader()
		=> new PatchEntryPayloadReader();
	public static ISameKeyReconstructionPlanningService CreateSameKeyReconstructionPlanningService(StoragePaths paths)
		=> new SameKeyReconstructionPlanningService(
			CreateAssetArchiveIndexService(paths));
	public static IModSameKeyReconstructionService CreateModSameKeyReconstructionService(StoragePaths paths)
		=> new ModSameKeyReconstructionService(
			CreatePatchFileNameParser(),
			CreateSameKeyReconstructionPlanningService(paths),
			CreateAssetArchiveIndexService(paths),
			CreateFileSystemArchiveHashesProvider(paths),
			CreateAdvancedModAnalysisService(paths));
   public static IAssetArchiveIndexService CreateAssetArchiveIndexService(StoragePaths paths)
		=> new AssetArchiveIndexService(paths);
	public static IGameDataLocator CreateGameDataLocator(IGameDataSettings settings)
		=> new GameDataLocator(settings);
	public static IArchiveHashesProvider CreateFileSystemArchiveHashesProvider(StoragePaths paths)
		=> new FileSystemArchiveHashesProvider(paths);
	public static IAssetMetadataCatalogProvider CreateAssetMetadataCatalogProvider(StoragePaths paths)
		=> new FileSystemAssetMetadataCatalogProvider(paths);
	public static IAssetMetadataSyncService CreateAssetMetadataSyncService(StoragePaths paths)
		=> new GitHubAssetMetadataSyncService(new HttpClient(), paths);
	public static IPatchGroupAnalysisProvider CreatePatchGroupAnalysisProvider(StoragePaths paths)
		=> new CachedPatchGroupAnalysisProvider(
			new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), PatchAnalysisDepth.DependencyGraph),
			CreateModFactsStore(paths),
			CreatePatchFileNameParser());
	public static IModFactsStore CreateModFactsStore(StoragePaths paths) => new SqliteModFactsStore(paths);
	public static IModContentFactsService CreateModContentFactsService(StoragePaths paths)
		=> new ModContentFactsService(CreatePatchFileNameParser(), CreatePatchGroupAnalysisProvider(paths));
	public static IAdvancedModAnalysisService CreateAdvancedModAnalysisService(StoragePaths paths)
		=> new AdvancedModAnalysisService(
			CreateModFactsStore(paths),
			new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), HD2ModAdaptation.Analysis.PatchAnalysisDepth.Full),
			CreatePatchFileNameParser());

	public static IPatchGroupAnalysisProvider CreateDependencyGraphAnalysisProvider()
		=> new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), HD2ModAdaptation.Analysis.PatchAnalysisDepth.DependencyGraph);

	public static IPatchGroupAnalysisProvider CreateFullPatchAnalysisProvider()
		=> new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), HD2ModAdaptation.Analysis.PatchAnalysisDepth.Full);
	public static IGameDataMappingFactsService CreateGameDataMappingFactsService(StoragePaths paths)
		=> new GameDataMappingFactsService(CreateAssetArchiveIndexService(paths), CreateAssetMetadataCatalogProvider(paths), paths);
	public static IProfileOverrideGraphService CreateProfileOverrideGraphService(StoragePaths paths)
		=> new ProfileOverrideGraphService(CreateModContentFactsService(paths), CreateGameDataMappingFactsService(paths));
	public static IProfileMaterialDiagnosticsService CreateProfileMaterialDiagnosticsService(StoragePaths paths)
		=> new ProfileMaterialDiagnosticsService(CreateModFactsStore(paths), CreateGameDataMappingFactsService(paths), CreateAssetArchiveIndexService(paths));
	public static IMaterialDeliveryFactsService CreateMaterialDeliveryFactsService(StoragePaths paths)
		=> new MaterialDeliveryFactsService(CreateModFactsStore(paths));
	public static IEquipmentUnitCatalogService CreateEquipmentUnitCatalogService(StoragePaths paths)
		=> new EquipmentUnitCatalogService(paths);
	public static ICrossArmorTransferCandidateService CreateCrossArmorTransferCandidateService()
		=> new CrossArmorTransferCandidateService();
	public static IAdvancedModAssetQueryService CreateAdvancedModAssetQueryService(StoragePaths paths)
		=> new AdvancedModAssetQueryService(CreateModFactsStore(paths), CreateGameDataMappingFactsService(paths), CreateAssetArchiveIndexService(paths));
	public static IMaterialPackagingApplicationService CreateMaterialPackagingApplicationService()
		=> new MaterialPackagingApplicationService(CreatePatchFileNameParser());
	public static IMaterialDependencyValidator CreateMaterialDependencyValidator()
		=> new MaterialDependencyValidator(CreatePatchEntryPayloadReader(), new StingrayMaterialReferenceReader());
	public static ILibraryDerivedDataService CreateLibraryDerivedDataService(StoragePaths paths)
		=> new LibraryDerivedDataService(CreateModContentFactsService(paths), new ModAssetSummaryProjector(CreateGameDataMappingFactsService(paths), CreateAssetMetadataCatalogProvider(paths)));
   public static IReplacementTargetDeriver CreateReplacementTargetDeriver(StoragePaths paths)
		=> new ReplacementTargetDeriver(paths, CreateAssetArchiveIndexService(paths));
   public static IModCompatibilityAnalyzer CreateModCompatibilityAnalyzer(StoragePaths paths)
		=> new ModCompatibilityAnalyzer(CreateAssetArchiveIndexService(paths));
   public static IObjectTreeImporter CreateObjectTreeImporter()
		=> new ObjectTreeImporter(CreatePatchFileNameParser());
   public static IArchiveObjectTreeImporter CreateArchiveObjectTreeImporter()
		=> new ArchiveObjectTreeImporter(CreateObjectTreeImporter());
   public static IModFileResolver CreateModFileResolver()
		=> new ModFileResolver(CreatePatchFileNameParser());
	public static IApplyPlanner CreateApplyPlanner()
		=> new ApplyPlanner(CreatePatchFileNameParser());
	public static IActivationStateStore CreateActivationStateStore()
		=> new JsonActivationStateStore();
	public static IDeployedOverrideGraphService CreateDeployedOverrideGraphService()
		=> new DeployedOverrideGraphService(CreateActivationStateStore(), CreatePatchFileNameParser());
	public static IModUserStatusService CreateModUserStatusService(StoragePaths paths)
		=> new ModUserStatusService(CreateModContentFactsService(paths), CreateProfileOverrideGraphService(paths), CreateDeployedOverrideGraphService());
	public static IGameDataArchiveBrowserService CreateGameDataArchiveBrowserService(StoragePaths paths)
		=> new GameDataArchiveBrowserService(CreateAssetArchiveIndexService(paths), CreateModContentFactsService(paths), CreateGameDataMappingFactsService(paths), CreateDeployedOverrideGraphService());
	public static IApplyExecutor CreateApplyExecutor()
		=> new ApplyExecutor(CreatePatchStateScanner(), CreatePatchFileNameParser(), CreateActivationStateStore());
	public static DeploymentCapabilityService CreateDeploymentCapabilityService() => new();
	public static IProfileApplyService CreateProfileApplyService(StoragePaths paths)
		=> new ProfileApplyService(CreateModContentFactsService(paths), CreateApplyPlanner(), CreateApplyExecutor(), CreateDeploymentCapabilityService());
	public static IProfileDeploymentCoordinator CreateProfileDeploymentCoordinator(StoragePaths paths, Func<string?> gameDataDirectoryProvider, IDeploymentDelay? delay = null, TimeSpan? bufferDuration = null)
		=> new ProfileDeploymentCoordinator(
			CreateModLibraryManager(paths),
			CreateProfileApplyService(paths),
			CreateApplyExecutor(),
			paths,
			gameDataDirectoryProvider,
			delay,
			bufferDuration);
	public static IAssetKeySetProvider CreateAssetKeySetProvider(StoragePaths paths)
		=> new AssetKeySetProvider(CreatePatchGroupAnalysisProvider(paths));
	public static IConflictDetector CreateConflictDetector(StoragePaths paths)
		=> new ConflictDetector(CreateAssetKeySetProvider(paths));
   public static IModLibraryStore CreateModLibraryStore(StoragePaths paths)
		=> new JsonModLibraryStore(paths);
   public static IModLibraryImporter CreateModLibraryImporter(StoragePaths paths)
		=> new ModLibraryImporter(
			paths,
			CreateObjectTreeImporter(),
			CreateArchiveObjectTreeImporter(),
			CreateModLibraryStore(paths),
			CreatePatchGroupAnalysisProvider(paths),
			CreateModFactsStore(paths));
   public static IModLibraryManager CreateModLibraryManager(StoragePaths paths)
		=> new ModLibraryManager(paths, CreateModLibraryStore(paths), CreateModFactsStore(paths));
   public static IModExporter CreateModExporter(StoragePaths paths)
		=> new ModExporter(paths);
   public static IModManifestImporter CreateModManifestImporter(StoragePaths paths)
		=> new ModManifestImporter(paths, CreateObjectTreeImporter(), CreateModLibraryStore(paths));
}

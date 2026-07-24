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
	public static IModFileFactsProducer CreateModFileFactsProducer()
		=> new ModFileFactsProducer(CreatePatchFileIndexBuilder());
	public static IModInformationCenter CreateModInformationCenter(StoragePaths paths)
		=> new ModInformationCenter(CreateModFileFactsProducer(), CreateAssetInventoryProducer(), new JsonModFileFactsCache(paths), CreateReferenceGraphProducer(paths), CreateMaintenanceAnalysisProducer(paths), CreateUnitVersionInformationProducer(paths), new JsonModInformationCache(paths), CreateAdvancedUnitAnalysisProducer(paths), CreateModThumbnailProducer(), CreateModDataIndex(paths));
	public static IAdvancedUnitAnalysisProducer CreateAdvancedUnitAnalysisProducer(StoragePaths paths)
		=> new AdvancedUnitAnalysisProducer(new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), HD2ModAdaptation.Analysis.PatchAnalysisDepth.Full));
	public static IModThumbnailProducer CreateModThumbnailProducer() => new ModThumbnailProducer();
	public static IModDataIndex CreateModDataIndex(StoragePaths paths) => new ModDataIndex(paths);
	public static IModDataIndex CreateModDataIndex() => new ModDataIndex();
	public static IPatchStateScanner CreatePatchStateScanner()
		=> new PatchStateScanner(CreatePatchFileNameParser());
	public static IPatchTocScanner CreatePatchTocScanner() => new PatchTocScanner();
 	public static IPatchEntryPayloadReader CreatePatchEntryPayloadReader()
		=> new PatchEntryPayloadReader();
	public static ISameKeyReconstructionPlanningService CreateSameKeyReconstructionPlanningService(StoragePaths paths)
		=> new SameKeyReconstructionPlanningService(
			CreateAssetArchiveIndexService(paths));
	public static IModSameKeyReconstructionService CreateModSameKeyReconstructionService(StoragePaths paths)
		=> CreateModSameKeyReconstructionService(paths, CreateModInformationCenter(paths));
	public static IModSameKeyReconstructionService CreateModSameKeyReconstructionService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new ModSameKeyReconstructionService(
			CreatePatchFileNameParser(),
			CreateSameKeyReconstructionPlanningService(paths),
			CreateAssetArchiveIndexService(paths),
			CreateFileSystemArchiveHashesProvider(paths),
			CreateAdvancedModAnalysisService(paths, informationCenter));
	public static IModRepairBatchService CreateModRepairBatchService(StoragePaths paths)
		=> new ModRepairBatchService(paths, CreateModSameKeyReconstructionService(paths), CreateAdvancedModAnalysisService(paths), CreatePatchFileNameParser());
   public static IAssetArchiveIndexService CreateAssetArchiveIndexService(StoragePaths paths)
		=> new AssetArchiveIndexService(paths);
	public static IAdvancedEquipmentIndexService CreateAdvancedEquipmentIndexService(StoragePaths paths)
		=> new AdvancedEquipmentIndexService(paths);
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
	public static IAssetInventoryProducer CreateAssetInventoryProducer()
		=> new ModContentFactsService(CreatePatchFileNameParser(), new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), PatchAnalysisDepth.Inventory));
	public static IPatchGroupAnalysisProvider CreateInventoryAnalysisProvider()
		=> new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), PatchAnalysisDepth.Inventory);
	public static IUnitVersionInformationProducer CreateUnitVersionInformationProducer(StoragePaths paths)
		=> new UnitVersionInformationProducer(CreatePatchGroupAnalysisProvider(paths));
	public static IReferenceGraphProducer CreateReferenceGraphProducer(StoragePaths paths)
		=> new ReferenceGraphProducer(CreatePatchGroupAnalysisProvider(paths));
	public static IMaintenanceAnalysisProducer CreateMaintenanceAnalysisProducer(StoragePaths paths)
		=> new MaintenanceAnalysisProducer(CreateModCompatibilityAnalyzer(paths));
	public static IAdvancedModAnalysisService CreateAdvancedModAnalysisService(StoragePaths paths)
		=> CreateAdvancedModAnalysisService(paths, CreateModInformationCenter(paths));
	public static IAdvancedModAnalysisService CreateAdvancedModAnalysisService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new AdvancedModAnalysisService(informationCenter, new JsonModInformationCache(paths));
	public static IAdvancedModAnalysisCacheStore CreateAdvancedModAnalysisCacheStore(StoragePaths paths) => new SqliteModFactsStore(paths);

	public static IPatchGroupAnalysisProvider CreateDependencyGraphAnalysisProvider()
		=> new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), HD2ModAdaptation.Analysis.PatchAnalysisDepth.DependencyGraph);

	public static IPatchGroupAnalysisProvider CreateFullPatchAnalysisProvider()
		=> new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), HD2ModAdaptation.Analysis.PatchAnalysisDepth.Full);
	public static IGameDataMappingFactsService CreateGameDataMappingFactsService(StoragePaths paths)
		=> new GameDataMappingFactsService(CreateAssetArchiveIndexService(paths), CreateAssetMetadataCatalogProvider(paths), paths);
	public static IProfileOverrideGraphService CreateProfileOverrideGraphService(StoragePaths paths)
	{
		var contentFacts = CreateModContentFactsService(paths);
		var informationCenter = CreateModInformationCenter(paths);
		return CreateProfileOverrideGraphService(paths, informationCenter);
	}
	public static IProfileOverrideGraphService CreateProfileOverrideGraphService(StoragePaths paths, IModInformationCenter informationCenter)
		=> CreateProfileOverrideGraphService(paths, CreateModContentFactsService(paths), informationCenter);
	public static IProfileOverrideGraphService CreateProfileOverrideGraphService(StoragePaths paths, IModContentFactsService contentFacts, IModInformationCenter informationCenter)
		=> new ProfileOverrideGraphService(contentFacts, CreateGameDataMappingFactsService(paths), informationCenter);
	public static IProfileMaterialDiagnosticsService CreateProfileMaterialDiagnosticsService(StoragePaths paths)
		=> new ProfileMaterialDiagnosticsService(CreateReferenceGraphProducer(paths), CreateGameDataMappingFactsService(paths), CreateAssetArchiveIndexService(paths));
	public static IProfileMaterialDiagnosticsService CreateProfileMaterialDiagnosticsService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new ProfileMaterialDiagnosticsService(informationCenter, CreateGameDataMappingFactsService(paths), CreateAssetArchiveIndexService(paths));
	public static IMaterialDeliveryFactsService CreateMaterialDeliveryFactsService(StoragePaths paths)
		=> new MaterialDeliveryFactsService(CreateModFactsStore(paths));
	public static IEquipmentUnitCatalogService CreateEquipmentUnitCatalogService(StoragePaths paths)
		=> new EquipmentUnitCatalogService(paths);
	public static ICrossArmorTransferCandidateService CreateCrossArmorTransferCandidateService(StoragePaths paths)
		=> new CrossArmorTransferCandidateService(CreateAssetArchiveIndexService(paths));
	public static IAdvancedModAssetQueryService CreateAdvancedModAssetQueryService(StoragePaths paths)
		=> new AdvancedModAssetQueryService(CreateModFactsStore(paths), CreateGameDataMappingFactsService(paths), CreateAssetArchiveIndexService(paths));
	public static IMaterialPackagingApplicationService CreateMaterialPackagingApplicationService()
		=> new MaterialPackagingApplicationService(CreatePatchFileNameParser());
	public static IMaterialDependencyValidator CreateMaterialDependencyValidator()
		=> new MaterialDependencyValidator(CreatePatchEntryPayloadReader(), new StingrayMaterialReferenceReader());
	public static ILibraryDerivedDataService CreateLibraryDerivedDataService(StoragePaths paths)
		=> CreateLibraryDerivedDataService(paths, null);
	public static ILibraryDerivedDataService CreateLibraryDerivedDataService(StoragePaths paths, IModInformationCenter? informationCenter)
	{
		var contentFacts = CreateModContentFactsService(paths);
		return new LibraryDerivedDataService(contentFacts, new ModAssetSummaryProjector(CreateGameDataMappingFactsService(paths), CreateAssetMetadataCatalogProvider(paths)), informationCenter);
	}
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
	{
		var contentFacts = CreateModContentFactsService(paths);
		var center = CreateModInformationCenter(paths);
		return new ModUserStatusService(contentFacts, CreateProfileOverrideGraphService(paths, contentFacts, center), CreateDeployedOverrideGraphService(), center);
	}
	public static IGameDataArchiveBrowserService CreateGameDataArchiveBrowserService(StoragePaths paths)
		=> new GameDataArchiveBrowserService(CreateAssetArchiveIndexService(paths), CreateModContentFactsService(paths), CreateGameDataMappingFactsService(paths), CreateDeployedOverrideGraphService());
	public static IApplyExecutor CreateApplyExecutor()
		=> new ApplyExecutor(CreatePatchStateScanner(), CreatePatchFileNameParser(), CreateActivationStateStore());
	public static DeploymentCapabilityService CreateDeploymentCapabilityService() => new();
	public static IProfileApplyService CreateProfileApplyService(StoragePaths paths)
		=> new ProfileApplyService(CreateModInformationCenter(paths), CreateApplyPlanner(), CreateApplyExecutor(), CreateDeploymentCapabilityService());
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

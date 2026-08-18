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
	{
		var referenceIndex = new SqliteModFactsStore(paths);
		return new ModInformationCenter(CreateModFileFactsProducer(), CreateAssetInventoryProducer(), new JsonModFileFactsCache(paths), CreateReferenceGraphProducer(paths), CreateMaintenanceAnalysisProducer(paths), CreateUnitVersionInformationProducer(paths), new JsonModInformationCache(paths), CreateAdvancedUnitAnalysisProducer(paths), CreateModThumbnailProducer(), CreateModDataIndex(paths), referenceIndex);
	}
	public static IAdvancedUnitAnalysisProducer CreateAdvancedUnitAnalysisProducer(StoragePaths paths)
		=> new AdvancedUnitAnalysisProducer(new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), HD2ModAdaptation.Analysis.PatchAnalysisDepth.Full));
	public static ISourceUnitEligibilityService CreateSourceUnitEligibilityService()
		=> new SourceUnitEligibilityService();
	public static IModThumbnailProducer CreateModThumbnailProducer() => new ModThumbnailProducer();
	public static IModDataIndex CreateModDataIndex(StoragePaths paths) => new ModDataIndex(paths);
	public static IModDataIndex CreateModDataIndex() => new ModDataIndex();
	public static IPatchStateScanner CreatePatchStateScanner()
		=> new PatchStateScanner(CreatePatchFileNameParser());
	public static IPatchTocScanner CreatePatchTocScanner() => new PatchTocScanner();
 	public static IPatchEntryPayloadReader CreatePatchEntryPayloadReader()
		=> new PatchEntryPayloadReader();
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IModSameKeyReconstructionService CreateModSameKeyReconstructionService(StoragePaths paths)
		=> CreateModSameKeyReconstructionService(paths, CreateModInformationCenter(paths));
	public static IModSameKeyReconstructionService CreateModSameKeyReconstructionService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new CanonicalSameKeyReconstructionService(
			CreatePatchFileNameParser(),
			CreateAssetArchiveIndexService(paths),
			CreateFileSystemArchiveHashesProvider(paths));
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IModRepairBatchService CreateModRepairBatchService(StoragePaths paths)
		=> new ModRepairBatchService(paths, CreateModSameKeyReconstructionService(paths), CreatePatchFileNameParser());
	public static IModRepairBatchService CreateModRepairBatchService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new ModRepairBatchService(paths, CreateModSameKeyReconstructionService(paths, informationCenter), CreatePatchFileNameParser());
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
	public static ModAssetSummaryProjector CreateModAssetSummaryProjector(StoragePaths paths)
		=> new ModAssetSummaryProjector(CreateGameDataMappingFactsService(paths), CreateAssetMetadataCatalogProvider(paths));
	public static IAssetMetadataSyncService CreateAssetMetadataSyncService(StoragePaths paths)
		=> new GitHubAssetMetadataSyncService(new HttpClient(), paths);
	public static IPatchGroupAnalysisProvider CreatePatchGroupAnalysisProvider(StoragePaths paths)
		=> new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), PatchAnalysisDepth.DependencyGraph);
	public static IAssetInventoryProducer CreateAssetInventoryProducer(StoragePaths paths)
		=> new ModContentFactsService(CreatePatchFileNameParser(), CreatePatchGroupAnalysisProvider(paths));
	public static IAssetInventoryProducer CreateAssetInventoryProducer()
		=> new ModContentFactsService(CreatePatchFileNameParser(), new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), PatchAnalysisDepth.Inventory));
	public static IPatchGroupAnalysisProvider CreateInventoryAnalysisProvider()
		=> new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), PatchAnalysisDepth.Inventory);
	public static IUnitVersionInformationProducer CreateUnitVersionInformationProducer(StoragePaths paths)
		=> new UnitVersionInformationProducer(CreatePatchGroupAnalysisProvider(paths));
	public static IReferenceGraphProducer CreateReferenceGraphProducer(StoragePaths paths)
		=> new ReferenceGraphProducer(CreatePatchGroupAnalysisProvider(paths));
	public static IReferenceGraphQueryIndex CreateReferenceGraphQueryIndex(StoragePaths paths)
		=> new SqliteModFactsStore(paths);
	public static IModAssetRoleFactsService CreateModAssetRoleFactsService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new ModAssetRoleFactsService(informationCenter, CreateReferenceGraphQueryIndex(paths), CreateGameDataMappingFactsService(paths));
	public static IMaintenanceAnalysisProducer CreateMaintenanceAnalysisProducer(StoragePaths paths)
		=> new MaintenanceAnalysisProducer(CreateModCompatibilityAnalyzer(paths));
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IAdvancedModAnalysisService CreateAdvancedModAnalysisService(StoragePaths paths)
		=> CreateAdvancedModAnalysisService(paths, CreateModInformationCenter(paths));
	public static IAdvancedModAnalysisService CreateAdvancedModAnalysisService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new AdvancedModAnalysisService(informationCenter);
	public static IPatchGroupAnalysisProvider CreateDependencyGraphAnalysisProvider()
		=> new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), HD2ModAdaptation.Analysis.PatchAnalysisDepth.DependencyGraph);

	public static IPatchGroupAnalysisProvider CreateFullPatchAnalysisProvider()
		=> new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer(), HD2ModAdaptation.Analysis.PatchAnalysisDepth.Full);
	public static IPatchGraphDiagnosticsService CreatePatchGraphDiagnosticsService()
		=> new PatchGraphDiagnosticsService(CreateDependencyGraphAnalysisProvider(), CreateFullPatchAnalysisProvider());
	public static IGameDataMappingFactsService CreateGameDataMappingFactsService(StoragePaths paths)
		=> new GameDataMappingFactsService(CreateAssetArchiveIndexService(paths), CreateAssetMetadataCatalogProvider(paths), paths);
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IProfileOverrideGraphService CreateProfileOverrideGraphService(StoragePaths paths)
		=> CreateProfileOverrideGraphService(paths, CreateModInformationCenter(paths));
	public static IProfileOverrideGraphService CreateProfileOverrideGraphService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new ProfileOverrideGraphService(informationCenter, CreateGameDataMappingFactsService(paths), CreateReferenceGraphQueryIndex(paths));
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IProfileMaterialDiagnosticsService CreateProfileMaterialDiagnosticsService(StoragePaths paths)
		=> CreateProfileMaterialDiagnosticsService(paths, CreateModInformationCenter(paths));
	public static IProfileMaterialDiagnosticsService CreateProfileMaterialDiagnosticsService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new ProfileMaterialDiagnosticsService(informationCenter, CreateGameDataMappingFactsService(paths), CreateAssetArchiveIndexService(paths), CreateReferenceGraphQueryIndex(paths));
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IMaterialDeliveryFactsService CreateMaterialDeliveryFactsService(StoragePaths paths)
		=> CreateMaterialDeliveryFactsService(paths, CreateModInformationCenter(paths));
	public static IMaterialDeliveryFactsService CreateMaterialDeliveryFactsService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new MaterialDeliveryFactsService(informationCenter, paths, CreateGameDataMappingFactsService(paths), CreateReferenceGraphQueryIndex(paths));
	public static IEquipmentUnitCatalogService CreateEquipmentUnitCatalogService(StoragePaths paths)
		=> new EquipmentUnitCatalogService(paths);
	public static CanonicalCrossArmorOrchestrator CreateCanonicalCrossArmorOrchestrator()
		=> new();
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IAdvancedModAssetQueryService CreateAdvancedModAssetQueryService(StoragePaths paths)
		=> CreateAdvancedModAssetQueryService(paths, CreateModInformationCenter(paths));
	public static IAdvancedModAssetQueryService CreateAdvancedModAssetQueryService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new AdvancedModAssetQueryService(informationCenter, paths, CreateReferenceGraphQueryIndex(paths), CreateGameDataMappingFactsService(paths), CreateAssetArchiveIndexService(paths));
	public static IMaterialPackagingApplicationService CreateMaterialPackagingApplicationService()
		=> new MaterialPackagingApplicationService(CreatePatchFileNameParser());
	public static IMaterialPackagingApplicationService CreateMaterialPackagingApplicationService(IModInformationCenter informationCenter)
		=> new MaterialPackagingApplicationService(CreatePatchFileNameParser(), informationCenter: informationCenter);
	public static IMaterialDependencyValidator CreateMaterialDependencyValidator()
		=> new MaterialDependencyValidator(CreatePatchEntryPayloadReader(), new StingrayMaterialReferenceReader());
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static ILibraryDerivedDataService CreateLibraryDerivedDataService(StoragePaths paths)
		=> CreateLibraryDerivedDataService(paths, CreateModInformationCenter(paths));
	public static ILibraryDerivedDataService CreateLibraryDerivedDataService(StoragePaths paths, IModInformationCenter informationCenter)
	{
		return new LibraryDerivedDataService(informationCenter, CreateModAssetSummaryProjector(paths));
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
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IModUserStatusService CreateModUserStatusService(StoragePaths paths)
	{
		var center = CreateModInformationCenter(paths);
		return new ModUserStatusService(center, CreateProfileOverrideGraphService(paths, center), CreateDeployedOverrideGraphService());
	}
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IGameDataArchiveBrowserService CreateGameDataArchiveBrowserService(StoragePaths paths)
		=> CreateGameDataArchiveBrowserService(paths, CreateModInformationCenter(paths));
	public static IGameDataArchiveBrowserService CreateGameDataArchiveBrowserService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new GameDataArchiveBrowserService(CreateAssetArchiveIndexService(paths), informationCenter, CreateGameDataMappingFactsService(paths), CreateDeployedOverrideGraphService());
	public static IApplyExecutor CreateApplyExecutor()
		=> new ApplyExecutor(CreatePatchStateScanner(), CreatePatchFileNameParser(), CreateActivationStateStore());
	public static DeploymentCapabilityService CreateDeploymentCapabilityService() => new();
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IProfileApplyService CreateProfileApplyService(StoragePaths paths)
		=> CreateProfileApplyService(paths, CreateModInformationCenter(paths));
	public static IProfileApplyService CreateProfileApplyService(StoragePaths paths, IModInformationCenter informationCenter)
		=> new ProfileApplyService(informationCenter, CreateApplyPlanner(), CreateApplyExecutor(), CreateDeploymentCapabilityService(), paths);
	[Obsolete("迁移状态：生产组合根应传入共享 IModInformationCenter；此无中心便捷工厂仅保留给测试和隔离场景。")]
	public static IProfileDeploymentCoordinator CreateProfileDeploymentCoordinator(StoragePaths paths, Func<string?> gameDataDirectoryProvider, IDeploymentDelay? delay = null, TimeSpan? bufferDuration = null)
		=> CreateProfileDeploymentCoordinator(paths, gameDataDirectoryProvider, CreateModInformationCenter(paths), delay, bufferDuration);
	public static IProfileDeploymentCoordinator CreateProfileDeploymentCoordinator(StoragePaths paths, Func<string?> gameDataDirectoryProvider, IModInformationCenter informationCenter, IDeploymentDelay? delay = null, TimeSpan? bufferDuration = null)
		=> new ProfileDeploymentCoordinator(
			CreateModLibraryManager(paths),
			CreateProfileApplyService(paths, informationCenter),
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
	[Obsolete("Use the overload accepting IModInformationCenter so the application can share one center.")]
   public static IModLibraryImporter CreateModLibraryImporter(StoragePaths paths)
		=> CreateModLibraryImporter(paths, CreateModInformationCenter(paths));
   public static IModLibraryImporter CreateModLibraryImporter(StoragePaths paths, IModInformationCenter informationCenter)
		=> new ModLibraryImporter(
			paths,
			CreateObjectTreeImporter(),
			CreateArchiveObjectTreeImporter(),
			CreateModLibraryStore(paths),
			informationCenter: informationCenter);
   public static IModLibraryManager CreateModLibraryManager(StoragePaths paths)
		=> new ModLibraryManager(paths, CreateModLibraryStore(paths));
	public static IModLibrarySynchronizer CreateModLibrarySynchronizer()
		=> new ModLibrarySynchronizer(CreatePatchFileNameParser());
   public static IModExporter CreateModExporter(StoragePaths paths)
		=> new ModExporter(paths);
   public static IModManifestImporter CreateModManifestImporter(StoragePaths paths)
		=> new ModManifestImporter(paths, CreateObjectTreeImporter(), CreateModLibraryStore(paths));
}

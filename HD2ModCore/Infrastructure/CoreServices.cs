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
	public static IPatchFileGroupFingerprintScanner CreatePatchFileGroupFingerprintScanner()
		=> new PatchFileGroupFingerprintScanner(CreatePatchFileNameParser());
	public static IPatchStorageIntegrityValidator CreatePatchStorageIntegrityValidator()
		=> new PatchStorageIntegrityValidator(CreatePatchFileGroupFingerprintScanner());
	public static IPatchFileGroupFingerprintStore CreatePatchFileGroupFingerprintStore(StoragePaths paths)
		=> new FileSystemPatchFileGroupFingerprintStore(paths);
	public static IPatchStateScanner CreatePatchStateScanner()
		=> new PatchStateScanner(CreatePatchFileNameParser());
	public static IPatchTocScanner CreatePatchTocScanner() => new PatchTocScanner();
 	public static IPatchEntryPayloadReader CreatePatchEntryPayloadReader()
		=> new PatchEntryPayloadReader();
	public static IPatchUnitMeshReader CreatePatchUnitMeshReader()
		=> new PatchUnitMeshReader(CreatePatchEntryPayloadReader(), CreateUnitMeshReader(), CreatePatchTocScanner());
	public static IPatchUnitMeshEditor CreatePatchUnitMeshEditor()
		=> new PatchUnitMeshEditor(CreatePatchUnitMeshReader(), CreateUnitMeshMinifier(), CreateUnitMeshRetargeter(), CreateUnitMeshWriter());
	public static IPatchArchiveDryWriter CreatePatchArchiveDryWriter()
		=> new PatchArchiveDryWriter(CreatePatchTocScanner(), CreatePatchEntryPayloadReader());
	public static IPatchArchiveFileWriter CreatePatchArchiveFileWriter()
		=> new PatchArchiveFileWriter();
	public static IPatchArchiveBatchPlanner CreatePatchArchiveBatchPlanner()
		=> new PatchArchiveBatchPlanner(CreatePatchTocScanner(), CreatePatchArchiveDryWriter());
	public static IPatchUnitMeshReplacementPlanner CreatePatchUnitMeshReplacementPlanner()
		=> new PatchUnitMeshReplacementPlanner(CreatePatchArchiveBatchPlanner(), CreatePatchUnitMeshReader(), CreatePatchUnitMeshEditor(), CreateUnitMeshReplacementStrategy());
	public static IPatchUnitMeshAutomationReporter CreatePatchUnitMeshAutomationReporter()
		=> new PatchUnitMeshAutomationReporter(CreatePatchUnitMeshReplacementPlanner());
	public static IPatchUnitMeshFolderAutomationReporter CreatePatchUnitMeshFolderAutomationReporter()
		=> new PatchUnitMeshFolderAutomationReporter(CreatePatchTocFileCollector(), CreatePatchUnitMeshAutomationReporter());
	public static IPatchUnitMeshSourceCatalogBuilder CreatePatchUnitMeshSourceCatalogBuilder()
		=> new PatchUnitMeshSourceCatalogBuilder(CreatePatchTocFileCollector(), CreatePatchTocScanner(), CreatePatchUnitMeshReader());
	public static IArchiveUnitMeshReader CreateArchiveUnitMeshReader()
		=> new ArchiveUnitMeshReader(gameDataDirectory => new GameDataPackageResolver(gameDataDirectory), CreatePatchTocScanner(), CreateUnitMeshReader());
	public static IUnitMeshAdaptationPlanner CreateUnitMeshAdaptationPlanner()
		=> new UnitMeshAdaptationPlanner(new UnitMeshReplacementStrategy(allowExperimentalFallback: true), CreateUnitMeshMinifier(), new UnitMeshRetargeter(allowExperimentalLayoutFallback: true, propagateSourceMaterials: true), CreateUnitMeshWriter());
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
	public static IModAssetAnalyzer CreateModAssetAnalyzer(StoragePaths paths)
		=> new CachedModAssetAnalyzer(
			CreateUncachedModAssetAnalyzer(paths),
			CreateModAssetAnalysisCacheStore(paths),
			CreatePatchFileNameParser(),
			paths);
	public static IModAssetAnalyzer CreateUncachedModAssetAnalyzer(StoragePaths paths)
		=> new ModAssetAnalyzer(
			CreatePatchFileNameParser(),
			CreatePatchTocScanner(),
			CreateAssetMetadataCatalogProvider(paths),
			CreateAssetArchiveIndexService(paths),
			adaptationAnalyzer: null,
			patchGroupAnalysisProvider: CreatePatchGroupAnalysisProvider(paths));
	public static IPatchGroupAnalysisProvider CreatePatchGroupAnalysisProvider(StoragePaths paths)
		=> new CachedPatchGroupAnalysisProvider(
			new AdaptationPatchGroupAnalysisProvider(CreatePatchFileNameParser(), new PatchGroupAnalyzer()),
			new FileSystemPatchGroupAnalysisCacheStore(paths),
			CreatePatchFileNameParser());
	public static IModAssetAnalysisCacheStore CreateModAssetAnalysisCacheStore(StoragePaths paths)
		=> new FileSystemModAssetAnalysisCacheStore(paths);
	public static IModAssetOverrideAnalyzer CreateModAssetOverrideAnalyzer(StoragePaths paths)
		=> new ModAssetOverrideAnalyzer(CreateModAssetAnalyzer(paths));
	public static IModUnitCompatibilityAnalyzer CreateModUnitCompatibilityAnalyzer()
		=> new ModUnitCompatibilityAnalyzer(CreatePatchFileNameParser());
	public static IUnitMeshReader CreateUnitMeshReader()
		=> new UnitMeshReader();
	public static IUnitMeshWriter CreateUnitMeshWriter()
		=> new UnitMeshWriter();
	public static IMaterialDependencyValidator CreateMaterialDependencyValidator()
		=> new MaterialDependencyValidator(CreatePatchEntryPayloadReader(), new StingrayMaterialReferenceReader());
	public static ArchiveDependencyResolver CreateArchiveDependencyResolver()
		=> new ArchiveDependencyResolver(CreatePatchTocScanner(), CreatePatchEntryPayloadReader(), new StingrayMaterialReferenceReader());
	public static IUnitMeshMinifier CreateUnitMeshMinifier()
		=> new UnitMeshMinifier();
	public static IUnitMeshRetargeter CreateUnitMeshRetargeter()
		=> new UnitMeshRetargeter();
	public static IUnitMeshReplacementStrategy CreateUnitMeshReplacementStrategy()
		=> new UnitMeshReplacementStrategy();
	public static IModUnitRepairService CreateModUnitRepairService()
		=> new ModUnitRepairService(CreatePatchFileNameParser(), CreateModUnitCompatibilityAnalyzer());
	public static ILibraryDerivedDataService CreateLibraryDerivedDataService(StoragePaths paths)
		// Compatibility analysis is deliberately opt-in until its Adaptation implementation is ready.
		=> new LibraryDerivedDataService(CreatePatchFileIndexBuilder(), CreateModAssetAnalyzer(paths), unitCompatibilityAnalyzer: null);
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
	public static IApplyExecutor CreateApplyExecutor()
		=> new ApplyExecutor(CreatePatchStateScanner());
	public static IProfileApplyService CreateProfileApplyService()
		=> new ProfileApplyService(CreatePatchFileIndexBuilder(), CreateApplyPlanner(), CreateApplyExecutor());
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
			CreateModLibraryStore(paths));
   public static IModLibraryManager CreateModLibraryManager(StoragePaths paths)
		=> new ModLibraryManager(paths, CreateModLibraryStore(paths), CreatePatchFileGroupFingerprintStore(paths));
   public static IModExporter CreateModExporter(StoragePaths paths)
		=> new ModExporter(paths);
   public static IModManifestImporter CreateModManifestImporter(StoragePaths paths)
		=> new ModManifestImporter(paths, CreateObjectTreeImporter(), CreateModLibraryStore(paths));
}

using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Exposes safe material packaging analysis and writes for flat library mods.
public interface IMaterialPackagingApplicationService
{
	ValueTask<ModMaterialPackagingState> InspectAsync(ModNode source, string modsRootDirectory, CancellationToken cancellationToken = default);
	ValueTask<IReadOnlyList<MaterialPackageCandidate>> FindCandidatesAsync(ModNode source, IReadOnlyCollection<ModNode> libraryNodes, string modsRootDirectory, bool requireAllExternalMaterials, CancellationToken cancellationToken = default);
	ValueTask<MaterialPackagingOperationResult> SplitAsync(ModNode source, string modsRootDirectory, string outputRootDirectory, CancellationToken cancellationToken = default);
	ValueTask<MaterialPackagingOperationResult> MergeAsync(ModNode source, ModNode candidate, string modsRootDirectory, string outputDirectory, bool requireAllExternalMaterials, CancellationToken cancellationToken = default);
}
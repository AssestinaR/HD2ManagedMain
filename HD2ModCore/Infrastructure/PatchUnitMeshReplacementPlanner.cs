using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：把 Unit mesh 替换策略接入 patch 批量 dry-run 计划，自动选择每个 entry 的最佳 RawMesh 替换候选。
// Purpose: Connects Unit mesh replacement strategy to patch batch dry-run planning, selecting the best RawMesh replacement candidate per entry.
public sealed class PatchUnitMeshReplacementPlanner : IPatchUnitMeshReplacementPlanner
{
	private readonly IPatchArchiveBatchPlanner batchPlanner;
	private readonly IPatchUnitMeshReader unitMeshReader;
	private readonly IPatchUnitMeshEditor unitMeshEditor;
	private readonly IUnitMeshReplacementStrategy replacementStrategy;

	public PatchUnitMeshReplacementPlanner(
		IPatchArchiveBatchPlanner batchPlanner,
		IPatchUnitMeshReader unitMeshReader,
		IPatchUnitMeshEditor unitMeshEditor,
		IUnitMeshReplacementStrategy replacementStrategy)
	{
		this.batchPlanner = batchPlanner ?? throw new ArgumentNullException(nameof(batchPlanner));
		this.unitMeshReader = unitMeshReader ?? throw new ArgumentNullException(nameof(unitMeshReader));
		this.unitMeshEditor = unitMeshEditor ?? throw new ArgumentNullException(nameof(unitMeshEditor));
		this.replacementStrategy = replacementStrategy ?? throw new ArgumentNullException(nameof(replacementStrategy));
	}

	public async ValueTask<PatchUnitMeshReplacementPlan> BuildReplacementPlanAsync(
		IReadOnlyCollection<string> patchTocFilePaths,
		PatchTocEntry sourceEntry,
		int? sourceMeshInfoIndex = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(patchTocFilePaths);
		ArgumentNullException.ThrowIfNull(sourceEntry);
		if (sourceMeshInfoIndex is < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceMeshInfoIndex), "Source mesh info index cannot be negative.");
		}

		var sourceUnit = await unitMeshReader.ReadUnitMeshAsync(sourceEntry, cancellationToken).ConfigureAwait(false);
		if (sourceMeshInfoIndex.HasValue && !sourceUnit.Model.RawMeshData.Any(mesh => mesh.MeshInfoIndex == sourceMeshInfoIndex.Value))
		{
			throw new ArgumentOutOfRangeException(nameof(sourceMeshInfoIndex), "Source mesh info index does not exist in the source Unit.");
		}

		var candidates = new List<PatchUnitMeshReplacementCandidate>();
		var batchPlan = await batchPlanner.BuildBatchPlanAsync(
			patchTocFilePaths,
			async (targetEntry, token) =>
			{
				if (IsSameEntry(targetEntry, sourceEntry))
				{
					return null;
				}

				var targetUnit = await unitMeshReader.ReadUnitMeshAsync(targetEntry, token).ConfigureAwait(false);
				var candidate = replacementStrategy
					.FindCandidates(targetUnit.Model, sourceUnit.Model)
					.Where(candidate => !sourceMeshInfoIndex.HasValue || candidate.SourceMeshInfoIndex == sourceMeshInfoIndex.Value)
					.FirstOrDefault();
				if (candidate is null)
				{
					return null;
				}

				candidates.Add(new PatchUnitMeshReplacementCandidate(targetEntry, sourceEntry, candidate));
				var edit = await unitMeshEditor.ReplaceRawMeshAsync(
					targetEntry,
					candidate.TargetMeshInfoIndex,
					sourceEntry,
					candidate.SourceMeshInfoIndex,
					token).ConfigureAwait(false);
				return edit with
				{
					AdaptationSteps =
					[
						new UnitMeshAdaptationStep(
							UnitMeshAdaptationStepKind.ReplaceWithSource,
							candidate.TargetMeshInfoIndex,
							candidate.SourceMeshInfoIndex,
							candidate.Reason,
							candidate)
					]
				};
			},
			cancellationToken: cancellationToken).ConfigureAwait(false);

		return new PatchUnitMeshReplacementPlan(sourceEntry, sourceMeshInfoIndex, candidates, batchPlan);
	}

	private static bool IsSameEntry(PatchTocEntry left, PatchTocEntry right)
	{
		return left.EntryIndex == right.EntryIndex
			&& left.AssetKey == right.AssetKey
			&& Path.GetFullPath(left.SourceFilePath).Equals(Path.GetFullPath(right.SourceFilePath), StringComparison.OrdinalIgnoreCase);
	}
}

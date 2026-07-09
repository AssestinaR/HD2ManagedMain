using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：把 source mod Unit RawMesh dry-run 适配到原版 archive target Unit 模板，并生成可解释计划。
// Purpose: Dry-run adapts source mod Unit RawMeshes onto a vanilla archive target Unit template and produces an explainable plan.
public sealed class UnitMeshAdaptationPlanner : IUnitMeshAdaptationPlanner
{
	private readonly IUnitMeshReplacementStrategy replacementStrategy;
	private readonly IUnitMeshMinifier minifier;
	private readonly IUnitMeshRetargeter retargeter;
	private readonly IUnitMeshWriter writer;

	public UnitMeshAdaptationPlanner(
		IUnitMeshReplacementStrategy replacementStrategy,
		IUnitMeshMinifier minifier,
		IUnitMeshRetargeter retargeter,
		IUnitMeshWriter writer)
	{
		this.replacementStrategy = replacementStrategy ?? throw new ArgumentNullException(nameof(replacementStrategy));
		this.minifier = minifier ?? throw new ArgumentNullException(nameof(minifier));
		this.retargeter = retargeter ?? throw new ArgumentNullException(nameof(retargeter));
		this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
	}

	public UnitMeshAdaptationPlan BuildPlan(
		PatchUnitMesh sourceUnit,
		ArchiveUnitMesh targetTemplate,
		int? sourceMeshInfoIndex = null)
	{
		ArgumentNullException.ThrowIfNull(sourceUnit);
		ArgumentNullException.ThrowIfNull(targetTemplate);
		if (sourceMeshInfoIndex is < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceMeshInfoIndex), "Source mesh info index cannot be negative.");
		}

		var intent = new UnitMeshAdaptationIntent(sourceUnit.Entry, targetTemplate.Entry, sourceMeshInfoIndex);
		if (sourceMeshInfoIndex.HasValue && !sourceUnit.Model.RawMeshData.Any(mesh => mesh.MeshInfoIndex == sourceMeshInfoIndex.Value))
		{
			throw new ArgumentOutOfRangeException(nameof(sourceMeshInfoIndex), "Source mesh info index does not exist in the source Unit.");
		}

		var candidates = replacementStrategy
			.FindCandidates(targetTemplate.Model, sourceUnit.Model)
			.Where(candidate => !sourceMeshInfoIndex.HasValue || candidate.SourceMeshInfoIndex == sourceMeshInfoIndex.Value)
			.ToArray();
		var selected = candidates.Length == 0
			? Array.Empty<UnitMeshReplacementCandidate>()
			: SelectNonConflictingCandidates(candidates);
		var selectedTargetIndexes = selected.Select(candidate => candidate.TargetMeshInfoIndex).ToHashSet();
		var meshIndexesToMinify = GetTargetMeshIndexesToMinify(targetTemplate.Model, selectedTargetIndexes);
		var editedModel = targetTemplate.Model;
		var steps = new List<UnitMeshAdaptationStep>();
		foreach (var meshInfoIndex in meshIndexesToMinify)
		{
			editedModel = minifier.MinifyRawMesh(editedModel, meshInfoIndex);
			steps.Add(new UnitMeshAdaptationStep(
				UnitMeshAdaptationStepKind.MinifyTarget,
				meshInfoIndex,
				SourceMeshInfoIndex: null,
				"Target mesh is minified before selected source replacements are applied."));
		}

		foreach (var candidate in selected)
		{
			editedModel = retargeter.ReplaceRawMesh(
				editedModel,
				candidate.TargetMeshInfoIndex,
				sourceUnit.Model,
				candidate.SourceMeshInfoIndex);
			steps.Add(new UnitMeshAdaptationStep(
				UnitMeshAdaptationStepKind.ReplaceWithSource,
				candidate.TargetMeshInfoIndex,
				candidate.SourceMeshInfoIndex,
				candidate.Reason,
				candidate));
		}

		try
		{
			var writeResult = targetTemplate.CompositePayload is null
				? writer.Write(editedModel, targetTemplate.Payload.TocData)
				: writer.Write(editedModel, targetTemplate.Payload.TocData, targetTemplate.CompositePayload.TocData);
			return new UnitMeshAdaptationPlan(
				intent,
				CanWrite: true,
				candidates,
				steps,
				editedModel,
				writeResult,
				BuildSuccessReason(selected.Count, meshIndexesToMinify.Count, candidates.Length));
		}
		catch (Exception ex) when (ex is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
		{
			return new UnitMeshAdaptationPlan(
				intent,
				CanWrite: false,
				candidates,
				steps,
				editedModel,
				WriteResult: null,
				$"Dry-run adaptation found candidates but failed to serialize edited target Unit: {ex.Message}");
		}
	}

	private static IReadOnlyList<UnitMeshReplacementCandidate> SelectNonConflictingCandidates(IReadOnlyList<UnitMeshReplacementCandidate> candidates)
	{
		var selected = new List<UnitMeshReplacementCandidate>();
		var usedTargets = new HashSet<int>();
		var usedSources = new HashSet<int>();
		foreach (var candidate in candidates)
		{
			if (!usedTargets.Add(candidate.TargetMeshInfoIndex) || !usedSources.Add(candidate.SourceMeshInfoIndex))
			{
				continue;
			}

			selected.Add(candidate);
		}

		return selected;
	}

	private static IReadOnlyList<int> GetTargetMeshIndexesToMinify(UnitMeshModel model, IReadOnlySet<int> replacementMeshIndexes)
		=> model.RawMeshData
			.Select(mesh => mesh.MeshInfoIndex)
			.Where(meshInfoIndex => !replacementMeshIndexes.Contains(meshInfoIndex))
			.ToArray();

	private static string BuildSuccessReason(int replacementCount, int minifiedCount, int candidateCount)
	{
		return replacementCount == 0
			? $"Dry-run adaptation produced a minify-only target Unit with {minifiedCount} minified target slot(s) because no source replacement candidates were found."
			: $"Dry-run adaptation produced {replacementCount} replacement(s) and {minifiedCount} minified target slot(s) from {candidateCount} candidate(s).";
	}
}

using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Resolves each source Unit to a current game-data Unit with the exact same AssetKey and produces a read-only reconstruction plan.
public sealed class SameKeyReconstructionPlanningService : ISameKeyReconstructionPlanningService
{
	private readonly IPatchTocScanner tocScanner;
	private readonly IPatchUnitMeshReader patchUnitReader;
	private readonly IAssetArchiveIndexService assetIndex;
	private readonly IArchiveUnitMeshReader archiveUnitReader;
	private readonly IUnitMeshAdaptationPlanner adaptationPlanner;

	public SameKeyReconstructionPlanningService(
		IPatchTocScanner tocScanner,
		IPatchUnitMeshReader patchUnitReader,
		IAssetArchiveIndexService assetIndex,
		IArchiveUnitMeshReader archiveUnitReader,
		IUnitMeshAdaptationPlanner adaptationPlanner)
	{
		this.tocScanner = tocScanner ?? throw new ArgumentNullException(nameof(tocScanner));
		this.patchUnitReader = patchUnitReader ?? throw new ArgumentNullException(nameof(patchUnitReader));
		this.assetIndex = assetIndex ?? throw new ArgumentNullException(nameof(assetIndex));
		this.archiveUnitReader = archiveUnitReader ?? throw new ArgumentNullException(nameof(archiveUnitReader));
		this.adaptationPlanner = adaptationPlanner ?? throw new ArgumentNullException(nameof(adaptationPlanner));
	}

	public async ValueTask<SameKeyReconstructionPlan> CreatePlanAsync(
		SameKeyReconstructionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		request.Validate();
		var sourcePath = Path.GetFullPath(request.SourcePatchTocPath);
		var gameDataDirectory = Path.GetFullPath(request.GameDataDirectory);
		if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source patch TOC does not exist.", sourcePath);
		if (!Directory.Exists(gameDataDirectory)) throw new DirectoryNotFoundException($"Game data directory does not exist: {gameDataDirectory}");

		var sourceEntries = await tocScanner.ScanEntriesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
		var sourceUnits = sourceEntries
			.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId)
			.GroupBy(entry => entry.AssetKey)
			.Select(group => group.First())
			.OrderBy(entry => entry.AssetKey.FileId)
			.Take(request.MaxSourceUnitCount ?? int.MaxValue)
			.ToArray();
		var archiveMatches = await assetIndex.FindAssetArchivesAsync(
			sourceUnits.Select(entry => entry.AssetKey).ToHashSet(), cancellationToken).ConfigureAwait(false);
		var archivesByKey = archiveMatches.ToDictionary(match => match.AssetKey, match => OrderArchives(match.Archives));
		var unitPlans = new List<SameKeyUnitReconstructionPlan>(sourceUnits.Length);

		foreach (var sourceEntry in sourceUnits)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var issues = new List<CoreIssue>();
			archivesByKey.TryGetValue(sourceEntry.AssetKey, out var matchingArchives);
			matchingArchives ??= Array.Empty<ArchiveMetadata>();
			if (matchingArchives.Count == 0)
			{
				issues.Add(Error("CurrentTargetMissing", "Game Data index has no current archive for this same-AssetKey Unit.", sourceEntry));
				unitPlans.Add(new SameKeyUnitReconstructionPlan(sourceEntry.AssetKey, sourceEntry, null, matchingArchives, null, issues));
				continue;
			}
			if (matchingArchives.Count > 1)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "SharedCurrentTarget", $"The same Unit AssetKey is present in {matchingArchives.Count} current archives; the first readable archive is used only for this feasibility plan.", sourceEntry.SourceFilePath));
			}

			PatchUnitMesh sourceUnit;
			try
			{
				sourceUnit = await patchUnitReader.ReadUnitMeshAsync(sourceEntry, sourceEntries, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or OverflowException or KeyNotFoundException)
			{
				issues.Add(Error("SourceUnitUnreadable", exception.Message, sourceEntry, exception));
				unitPlans.Add(new SameKeyUnitReconstructionPlan(sourceEntry.AssetKey, sourceEntry, null, matchingArchives, null, issues));
				continue;
			}

			ArchiveMetadata? selectedArchive = null;
			ArchiveUnitMesh? targetUnit = null;
			foreach (var archive in matchingArchives)
			{
				try
				{
					targetUnit = await archiveUnitReader.ReadUnitMeshAsync(gameDataDirectory, archive.ArchiveId, sourceEntry.AssetKey, cancellationToken).ConfigureAwait(false);
					selectedArchive = archive;
					break;
				}
				catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or OverflowException or KeyNotFoundException)
				{
					issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "CurrentTargetUnreadable", $"{archive.ArchiveId}: {exception.Message}", sourceEntry.SourceFilePath, ExceptionMessage: exception.ToString()));
				}
			}
			if (targetUnit is null || selectedArchive is null)
			{
				issues.Add(Error("CurrentTargetUnreadable", "No indexed current archive yielded a readable same-AssetKey Unit.", sourceEntry));
				unitPlans.Add(new SameKeyUnitReconstructionPlan(sourceEntry.AssetKey, sourceEntry, null, matchingArchives, null, issues));
				continue;
			}

			var dryRun = adaptationPlanner.BuildPlan(sourceUnit, targetUnit);
			// A library/UI plan needs the decision and diagnostics, not a retained full GPU model for every Unit.
			// The Adaptation reconstruction pipeline re-reads and rebuilds the selected targets on demand.
			var adaptation = dryRun with { EditedModel = null, WriteResult = null };
			var evidence = BuildMeshEvidence(sourceUnit, targetUnit, adaptation);
			var targetMeshCount = targetUnit.Model.RawMeshData.Count;
			var coveredTargetMeshCount = adaptation.Steps
				.Select(step => step.TargetMeshInfoIndex)
				.Distinct()
				.Count();
			if (adaptation.Steps.Any(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource && step.Candidate?.Kind == UnitMeshReplacementCandidateKind.ExperimentalFallback) && !request.AllowExperimentalCandidates)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "ExperimentalMeshCandidate", "One or more selected mesh mappings need experimental fallback and are not eligible for automatic test-copy output.", sourceEntry.SourceFilePath));
			}
			if (adaptation.ReplacementCount == 0)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "MinifyOnlyPlan", "No source mesh was selected for replacement; this Unit is a minify-only target-shell plan.", sourceEntry.SourceFilePath));
			}
			if (!adaptation.CanWrite)
			{
				issues.Add(Error("TargetSerializationFailed", adaptation.Reason, sourceEntry));
			}
			unitPlans.Add(new SameKeyUnitReconstructionPlan(sourceEntry.AssetKey, sourceEntry, selectedArchive, matchingArchives, adaptation, issues, evidence, targetMeshCount, coveredTargetMeshCount));
		}

		var globalIssues = new List<CoreIssue>();
		if (sourceUnits.Length == 0)
		{
			globalIssues.Add(new CoreIssue(CoreIssueSeverity.Error, "NoSourceUnits", "The source patch does not contain Unit entries.", sourcePath));
		}
		return new SameKeyReconstructionPlan(request with { SourcePatchTocPath = sourcePath, GameDataDirectory = gameDataDirectory }, unitPlans, globalIssues);
	}

	private static IReadOnlyList<ArchiveMetadata> OrderArchives(IReadOnlyList<ArchiveMetadata> archives)
		=> archives.OrderBy(archive => archive.CategoryOrder).ThenBy(archive => archive.ArchiveOrder).ThenBy(archive => archive.ArchiveId, StringComparer.OrdinalIgnoreCase).ToArray();

	private static IReadOnlyList<SameKeyMeshEvidence> BuildMeshEvidence(
		PatchUnitMesh sourceUnit,
		ArchiveUnitMesh targetUnit,
		UnitMeshAdaptationPlan adaptation)
	{
		var replacementTargets = adaptation.Steps
			.Where(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource)
			.Select(step => step.TargetMeshInfoIndex)
			.ToHashSet();
		var minifiedTargets = adaptation.Steps
			.Where(step => step.Kind == UnitMeshAdaptationStepKind.MinifyTarget)
			.Select(step => step.TargetMeshInfoIndex)
			.ToHashSet();
		return adaptation.Candidates.Select(candidate =>
		{
			var source = sourceUnit.Model.RawMeshData.First(mesh => mesh.MeshInfoIndex == candidate.SourceMeshInfoIndex);
			var target = targetUnit.Model.RawMeshData.First(mesh => mesh.MeshInfoIndex == candidate.TargetMeshInfoIndex);
			var sourceStream = sourceUnit.Model.Streams.First(stream => stream.Index == source.StreamIndex);
			var targetStream = targetUnit.Model.Streams.First(stream => stream.Index == target.StreamIndex);
			var sourceBones = GetRealBoneIndices(sourceUnit.Model, source);
			var targetBones = GetRealBoneIndices(targetUnit.Model, target);
			return new SameKeyMeshEvidence(
				candidate.SourceMeshInfoIndex,
				candidate.TargetMeshInfoIndex,
				candidate.Kind,
				candidate.Score,
				candidate.SourceSemanticName,
				candidate.TargetSemanticName,
				source.LodIndex,
				target.LodIndex,
				source.Vertices.Count,
				target.Vertices.Count,
				source.Triangles.Count,
				target.Triangles.Count,
				source.Sections.Count,
				target.Sections.Count,
				sourceStream.VertexStride,
				targetStream.VertexStride,
				DescribeComponents(sourceStream),
				DescribeComponents(targetStream),
				sourceBones,
				targetBones,
				sourceBones.Intersect(targetBones).Count(),
				replacementTargets.Contains(candidate.TargetMeshInfoIndex),
				minifiedTargets.Contains(candidate.TargetMeshInfoIndex),
				candidate.Reason);
		}).ToArray();
	}

	private static IReadOnlyList<string> DescribeComponents(UnitStreamInfo stream)
		=> stream.Components.Select(component => $"{component.TypeName}[{component.Index}]={component.FormatName}:{component.Size}").ToArray();

	private static IReadOnlyList<uint> GetRealBoneIndices(UnitMeshModel model, UnitRawMeshData mesh)
	{
		if (model.BoneInfos.Count == 0)
		{
			return Array.Empty<uint>();
		}
		var boneInfoIndex = mesh.LodIndex is >= 0 and < int.MaxValue && mesh.LodIndex < model.BoneInfos.Count ? mesh.LodIndex : 0;
		var boneInfo = model.BoneInfos[boneInfoIndex];
		var fakeToReal = boneInfo.Remaps
			.SelectMany(remap => remap.FakeIndices)
			.Where(index => index < boneInfo.RealIndices.Count)
			.Select(index => boneInfo.RealIndices[(int)index])
			.ToHashSet();
		return mesh.Vertices
			.SelectMany(vertex => vertex.Components.Where(component => component.Type == 6).SelectMany(component => component.UIntValues))
			.Select(index => index < boneInfo.RealIndices.Count ? boneInfo.RealIndices[(int)index] : index)
			.Where(index => fakeToReal.Count == 0 || fakeToReal.Contains(index))
			.Distinct()
			.OrderBy(index => index)
			.ToArray();
	}

	private static CoreIssue Error(string code, string message, PatchTocEntry sourceEntry, Exception? exception = null)
		=> new(CoreIssueSeverity.Error, code, message, sourceEntry.SourceFilePath, ExceptionMessage: exception?.ToString());
}
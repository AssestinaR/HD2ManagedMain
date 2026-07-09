using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：把 patch-level Unit RawMesh 替换计划转换为 entry 级 dry-run 自动化报告。
// Purpose: Converts patch-level Unit RawMesh replacement plans into entry-level dry-run automation reports.
public sealed class PatchUnitMeshAutomationReporter : IPatchUnitMeshAutomationReporter
{
	private readonly IPatchUnitMeshReplacementPlanner replacementPlanner;

	public PatchUnitMeshAutomationReporter(IPatchUnitMeshReplacementPlanner replacementPlanner)
	{
		this.replacementPlanner = replacementPlanner ?? throw new ArgumentNullException(nameof(replacementPlanner));
	}

	public async ValueTask<PatchUnitMeshAutomationReport> BuildReportAsync(
		IReadOnlyCollection<string> patchTocFilePaths,
		PatchTocEntry sourceEntry,
		int? sourceMeshInfoIndex = null,
		CancellationToken cancellationToken = default)
	{
		var plan = await replacementPlanner.BuildReplacementPlanAsync(
			patchTocFilePaths,
			sourceEntry,
			sourceMeshInfoIndex,
			cancellationToken).ConfigureAwait(false);

		var candidatesByEntry = plan.Candidates
			.GroupBy(candidate => CreateEntryKey(candidate.TargetEntry))
			.ToDictionary(group => group.Key, group => group.First());

		var entryReports = plan.BatchPlan.EntryResults
			.Select(result => CreateEntryReport(result, candidatesByEntry))
			.ToArray();

		return new PatchUnitMeshAutomationReport(plan, entryReports);
	}

	private static PatchUnitMeshAutomationEntryReport CreateEntryReport(
		PatchArchiveBatchEntryResult result,
		IReadOnlyDictionary<EntryKey, PatchUnitMeshReplacementCandidate> candidatesByEntry)
	{
		candidatesByEntry.TryGetValue(CreateEntryKey(result.Entry), out var candidate);
		return new PatchUnitMeshAutomationEntryReport(
			result.Entry,
			result.Status,
			BuildReason(result, candidate),
			candidate,
			result.Exception);
	}

	private static string BuildReason(PatchArchiveBatchEntryResult result, PatchUnitMeshReplacementCandidate? candidate)
	{
		if (candidate is not null)
		{
			return $"{result.Reason} Candidate: {candidate.MeshCandidate.Kind}; {candidate.MeshCandidate.Reason}";
		}

		return result.Reason;
	}

	private static EntryKey CreateEntryKey(PatchTocEntry entry)
		=> new(Path.GetFullPath(entry.SourceFilePath), entry.EntryIndex, entry.AssetKey);

	private sealed record EntryKey(string SourceFilePath, uint EntryIndex, AssetKey AssetKey);
}

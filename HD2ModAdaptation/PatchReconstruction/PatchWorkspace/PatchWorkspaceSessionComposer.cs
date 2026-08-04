using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

namespace HD2ModAdaptation.PatchReconstruction.PatchWorkspace;

// Purpose: Composes payload-owned workspace entries into one validated Canonical patch session.
public interface IPatchWorkspaceSessionComposer
{
	CanonicalPatchSessionValidation Compose(
		CanonicalPatchSession session,
		IEnumerable<CanonicalPatchSessionEntry> entries,
		CanonicalDependencyClosureValidation dependencyClosureValidation);

	CanonicalPatchSessionValidation ComposeJobs(
		CanonicalPatchSession session,
		IEnumerable<PatchWorkspaceJobResult> jobs,
		IEnumerable<CanonicalPatchSessionEntry> additionalEntries,
		CanonicalDependencyClosureValidation dependencyClosureValidation);
}

public sealed class PatchWorkspaceSessionComposer : IPatchWorkspaceSessionComposer
{
	public CanonicalPatchSessionValidation Compose(
		CanonicalPatchSession session,
		IEnumerable<CanonicalPatchSessionEntry> entries,
		CanonicalDependencyClosureValidation dependencyClosureValidation)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(entries);
		foreach (var entry in entries)
			session.AddEntry(entry);
		return session.Finalize(dependencyClosureValidation);
	}

	public CanonicalPatchSessionValidation ComposeJobs(
		CanonicalPatchSession session,
		IEnumerable<PatchWorkspaceJobResult> jobs,
		IEnumerable<CanonicalPatchSessionEntry> additionalEntries,
		CanonicalDependencyClosureValidation dependencyClosureValidation)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(jobs);
		ArgumentNullException.ThrowIfNull(additionalEntries);
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		foreach (var job in jobs)
		{
			ArgumentNullException.ThrowIfNull(job);
			diagnostics.AddRange(job.Diagnostics);
			foreach (var output in job.Outputs) session.AddEntry(output);
		}
		foreach (var entry in additionalEntries) session.AddEntry(entry);
		if (diagnostics.Count != 0)
			return new(false, diagnostics, CanonicalDependencyClosureValidation.Invalid);
		return session.Finalize(dependencyClosureValidation);
	}
}
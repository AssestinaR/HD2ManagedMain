using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

namespace HD2ModAdaptation.PatchReconstruction.PatchWorkspace;

// Purpose: Carries one discrete resource job's payload-owned outputs to the Patch workspace layer.
public sealed record PatchWorkspaceJobResult(
	IReadOnlyList<CanonicalPatchSessionEntry> Outputs,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics,
	string JobKind,
	string? JobKey = null)
{
	public bool IsValid => Diagnostics.Count == 0 && Outputs.Count > 0;

	public static PatchWorkspaceJobResult Unit(
		CanonicalPatchSessionEntry output,
		string? jobKey = null)
		=> new([output], Array.Empty<CanonicalPlanDiagnostic>(), "Unit", jobKey);
}
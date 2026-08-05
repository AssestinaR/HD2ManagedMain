using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.PatchReconstruction.Validation;

// Purpose: Defines the standalone Patch validation result and configurable validation policy.
public enum PatchValidationSeverity
{
	Info,
	Warning,
	Error
}

public sealed record PatchValidationIssue(
	PatchValidationSeverity Severity,
	string Code,
	string Message,
	AssetKey? AssetKey = null,
	string? FilePath = null,
	Exception? Exception = null);

public sealed record PatchValidationOptions(
	bool ReadUnitPayloads = true,
	bool RequirePatchLocalComposite = false,
	bool RequirePatchLocalBone = false,
	bool ReportEmptyUnitGeometry = true,
	uint? ExpectedUnitVersion = null,
	bool TreatOutdatedUnitVersionAsError = false,
	string? SourcePatchTocFilePath = null,
	bool RequireSourceGeometryPreservation = false,
	bool RequireFiniteVisiblePositions = false,
	bool RequireBoundVisibleMaterialSlots = false);

public sealed record PatchValidationResult(
	string PatchTocFilePath,
	IReadOnlyList<PatchTocEntry> Entries,
	IReadOnlyList<PatchValidationIssue> Issues,
	int UnitsChecked,
	int UnitsReadable,
	DateTimeOffset ValidatedUtc)
{
	public bool IsValid => Issues.All(issue => issue.Severity != PatchValidationSeverity.Error);
	public bool HasWarnings => Issues.Any(issue => issue.Severity == PatchValidationSeverity.Warning);
}

public interface IPatchValidator
{
	ValueTask<PatchValidationResult> ValidateAsync(
		string patchTocFilePath,
		PatchValidationOptions? options = null,
		CancellationToken cancellationToken = default);
}
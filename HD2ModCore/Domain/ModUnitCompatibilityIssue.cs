namespace HD2ModCore.Domain;

// Purpose: Records one unit-level compatibility finding with enough context for UI and repair planning.
public sealed record ModUnitCompatibilityIssue(
	ModUnitIssueKind Kind,
	ulong FileId,
	string FileIdHex,
	string SourceFileName,
	string Message,
	bool IsHighConfidenceOutdated,
	bool IsRepairable,
	UnitStructureSummary? ModUnit,
	UnitStructureSummary? GameUnit);
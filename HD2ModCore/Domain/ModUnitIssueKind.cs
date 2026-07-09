namespace HD2ModCore.Domain;

// Purpose: Classifies structural unit compatibility findings for diagnostics and future repair planning.
public enum ModUnitIssueKind
{
	VersionMismatch,
	OldLayout,
	InvalidModUnit,
	InvalidGameUnit,
	MissingInGame,
	LodSizeMismatch,
	ScanFailed,
}
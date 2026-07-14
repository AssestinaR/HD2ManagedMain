namespace HD2ModCore.Domain;

// Purpose: Public completed deployment state persisted atomically after verification.
public sealed record ActivationState(
	int Version,
	ProfileId? ProfileId,
	long ProfileRevision,
	DateTimeOffset AppliedUtc,
	bool Completed,
	IReadOnlyList<ActivationStateFileEntry> Files,
	IReadOnlyList<CoreIssue> Issues);

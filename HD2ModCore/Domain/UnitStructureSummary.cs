namespace HD2ModCore.Domain;

// Purpose: Captures the small set of unit header fields used for compatibility checks.
public sealed record UnitStructureSummary(
	bool IsValid,
	int Size,
	uint? Version,
	string? VersionHex,
	uint? LodGroupOffset,
	uint? JointListOffset,
	int? LodGroupSize,
	bool IsOldLayout,
	string? Reason);
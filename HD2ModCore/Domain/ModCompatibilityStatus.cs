namespace HD2ModCore.Domain;

// Purpose: Coarse compatibility status for a mod against the current game data asset index.
public enum ModCompatibilityStatus
{
	Unknown = 0,
	Compatible = 1,
	Partial = 2,
	LikelyOutdated = 3,
}
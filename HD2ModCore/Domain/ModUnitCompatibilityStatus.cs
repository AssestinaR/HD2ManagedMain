namespace HD2ModCore.Domain;

// Purpose: Describes the high-level unit-structure compatibility state for one mod node.
public enum ModUnitCompatibilityStatus
{
	Unknown,
	NoUnitAssets,
	Current,
	Outdated,
	Invalid,
}
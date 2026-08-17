namespace HD2ModCore.Domain;

// Purpose: Player-facing projection that hides Patch, AssetKey and archive implementation details.
public enum ModUserStatusKind
{
	Stored,
	Enabled,
	Overridden,
	MissingMaterial,
}

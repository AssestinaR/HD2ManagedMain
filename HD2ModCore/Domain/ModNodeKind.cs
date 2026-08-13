namespace HD2ModCore.Domain;

// Purpose: Distinguishes deployable Mods from decoration packages that must be attached to a host Mod.
public enum ModNodeKind
{
	Standard = 0,
	Decoration = 1,
}

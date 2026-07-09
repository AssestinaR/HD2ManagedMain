namespace HD2ModCore.Domain;

// Purpose: Coarse state describing whether the cached game data reverse index matches the current game files.
public enum GameDataIndexState
{
	Missing = 0,
	Current = 1,
	Stale = 2,
	Invalid = 3,
}
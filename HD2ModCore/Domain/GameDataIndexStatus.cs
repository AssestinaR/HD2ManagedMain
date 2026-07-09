namespace HD2ModCore.Domain;

// Purpose: Compares the stored reverse-index fingerprint with the current game data files.
public sealed record GameDataIndexStatus(
	GameDataIndexState State,
	GameDataIndexFingerprint? StoredFingerprint,
	string GameDataDirectory,
	string CurrentSourceFingerprint)
{
	public bool IsCurrent => State == GameDataIndexState.Current;
}
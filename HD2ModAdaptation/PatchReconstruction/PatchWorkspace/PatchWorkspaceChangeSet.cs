namespace HD2ModAdaptation.PatchReconstruction.PatchWorkspace;

// Purpose: Owns the validated set of discrete Patch workspace changes before packaging.
public sealed class PatchWorkspaceChangeSet
{
	private readonly Dictionary<AssetKey, PatchWorkspaceEntryChange> changes = new();

	public IReadOnlyCollection<PatchWorkspaceEntryChange> Changes => changes.Values;

	public void Add(PatchWorkspaceEntryChange change)
	{
		ArgumentNullException.ThrowIfNull(change);
		if (change.AssetKey == default) throw new ArgumentException("Patch changes require an explicit AssetKey.", nameof(change));
		if (change.Kind is PatchWorkspaceEntryChangeKind.Keep or PatchWorkspaceEntryChangeKind.Replace or PatchWorkspaceEntryChangeKind.Add
			&& change.Entry is null)
			throw new InvalidDataException($"Patch change {change.Kind} requires a payload for AssetKey {change.AssetKey}.");
		if (!changes.TryAdd(change.AssetKey, change))
			throw new InvalidDataException($"Patch workspace contains multiple changes for AssetKey {change.AssetKey}.");
	}

	public void AddRange(IEnumerable<PatchWorkspaceEntryChange> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		foreach (var entry in entries) Add(entry);
	}
}

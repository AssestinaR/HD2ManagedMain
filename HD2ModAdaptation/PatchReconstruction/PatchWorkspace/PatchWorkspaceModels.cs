using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

namespace HD2ModAdaptation.PatchReconstruction.PatchWorkspace;

// Purpose: Defines a format-level Patch workspace containing payload-owned entries and their source metadata.
public sealed record PatchWorkspaceEntry(
	PatchTocEntry Metadata,
	byte[] TocData,
	byte[] StreamData,
	byte[] GpuData);

public sealed record PatchWorkspace(
	string SourcePatchTocPath,
	IReadOnlyList<PatchWorkspaceEntry> Entries,
	byte[] HeaderTemplateTocData)
{
	public IReadOnlyDictionary<AssetKey, PatchWorkspaceEntry> ByAssetKey
		=> Entries.ToDictionary(entry => entry.Metadata.AssetKey);

	public CanonicalPatchSession ToCanonicalSession(
		IEnumerable<PatchWorkspaceEntryChange> changes,
		IReadOnlySet<AssetKey>? excluded = null)
	{
		ArgumentNullException.ThrowIfNull(changes);
		var changeSet = new PatchWorkspaceChangeSet();
		changeSet.AddRange(changes);
		return ToCanonicalSession(changeSet, excluded);
	}

	public CanonicalPatchSession ToCanonicalSession(
		PatchWorkspaceChangeSet changes,
		IReadOnlySet<AssetKey>? excluded = null)
	{
		ArgumentNullException.ThrowIfNull(changes);
		var sourceByKey = ByAssetKey;
		var effective = new Dictionary<AssetKey, PatchWorkspaceEntryChange>();
		foreach (var change in changes.Changes)
		{
			if (excluded?.Contains(change.AssetKey) == true) continue;
			if (!effective.TryAdd(change.AssetKey, change))
				throw new InvalidDataException($"Patch workspace contains multiple changes for AssetKey {change.AssetKey}.");
		}

		foreach (var source in Entries)
		{
			if (excluded?.Contains(source.Metadata.AssetKey) == true) continue;
			if (!effective.ContainsKey(source.Metadata.AssetKey))
				effective.Add(source.Metadata.AssetKey, PatchWorkspaceEntryChange.Keep(source));
		}

		var session = new CanonicalPatchSession();
		foreach (var change in effective.Values)
		{
			if (change.Kind == PatchWorkspaceEntryChangeKind.Remove) continue;
			var entry = change.Entry ?? (sourceByKey.TryGetValue(change.AssetKey, out var source)
				? source
				: throw new InvalidDataException($"Patch workspace change {change.Kind} has no payload for AssetKey {change.AssetKey}."));
			session.AddEntry(new CanonicalPatchSessionEntry(
				entry.Metadata.AssetKey,
				entry.Metadata.AssetKey.TypeId == UnitAssetTypeId
					? CanonicalPatchEntryOwnership.TargetOutput
					: CanonicalPatchEntryOwnership.RequiredDependency,
				entry.TocData,
				entry.GpuData,
				entry.StreamData,
				entry.Metadata.Unknown1,
				entry.Metadata.Unknown2,
				entry.Metadata.Unknown3,
				entry.Metadata.Unknown4));
		}
		return session;
	}

	public CanonicalPatchSession ToCanonicalSession(
		IReadOnlySet<AssetKey>? excluded = null)
		=> ToCanonicalSession(Entries
			.Where(entry => excluded is null || !excluded.Contains(entry.Metadata.AssetKey))
			.Select(PatchWorkspaceEntryChange.Keep), excluded);

	private const ulong UnitAssetTypeId = 0xe0a48d0be9a7453f;
}
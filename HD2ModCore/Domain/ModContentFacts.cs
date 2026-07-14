namespace HD2ModCore.Domain;

// Purpose: Authoritative immutable content snapshot for one flat internal mod.
public sealed record ModContentFacts(
	ModNodeId NodeId,
	string RelativePath,
	string ContentGeneration,
	DateTimeOffset BuiltUtc,
	IReadOnlyList<ModPatchGroupFact> PatchGroups,
	IReadOnlyList<CoreIssue> Issues)
{
	public IReadOnlyList<IndexedPatchFile> ToPatchFileIndex()
		=> PatchGroups
			.SelectMany(group => group.Files.Select(file => new IndexedPatchFile(
				NodeId,
				file.FilePath,
				file.FileName,
				group.Id.SourceArchiveHex,
				group.Id.SourcePatchIndex,
				group.NormalizedOrder,
				file.SidecarKind,
				file.Length,
				file.LastWriteTimeUtc)))
			.OrderBy(file => file.ArchiveHex16, StringComparer.OrdinalIgnoreCase)
			.ThenBy(file => file.NormalizedOrder)
			.ThenBy(file => file.SidecarKind)
			.ToList();
}

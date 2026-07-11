namespace HD2ModAdaptation.PatchReconstruction;

// Purpose: Reconstructs one patch and its material payload closure from prepared Unit edits.
public sealed class PatchAssetReconstructor
{
	private readonly IPatchTocScanner tocScanner;
	private readonly MaterialDependencyResolver materialResolver;
	private readonly PatchArchiveWriter archiveWriter;

	public PatchAssetReconstructor(
		IPatchTocScanner? tocScanner = null,
		MaterialDependencyResolver? materialResolver = null,
		PatchArchiveWriter? archiveWriter = null)
	{
		this.tocScanner = tocScanner ?? new PatchTocScanner();
		this.materialResolver = materialResolver ?? new MaterialDependencyResolver(this.tocScanner);
		this.archiveWriter = archiveWriter ?? new PatchArchiveWriter(this.tocScanner);
	}

	public async ValueTask<PatchAssetReconstructionResult> ReconstructAsync(
		string sourcePatchTocPath,
		string outputDirectoryPath,
		IReadOnlyCollection<PatchUnitMeshEditResult> unitEdits,
		IReadOnlyDictionary<AssetKey, IReadOnlyList<string>> preferredArchivesByAsset,
		string gameDataDirectory,
		IReadOnlyCollection<PatchTocEntry>? removedEntries = null,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePatchTocPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectoryPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(gameDataDirectory);
		ArgumentNullException.ThrowIfNull(unitEdits);
		ArgumentNullException.ThrowIfNull(preferredArchivesByAsset);

		var sourceEntries = await tocScanner.ScanEntriesAsync(sourcePatchTocPath, cancellationToken).ConfigureAwait(false);
		ValidateEdits(sourcePatchTocPath, unitEdits, sourceEntries);
		var materialIds = CollectReplacementMaterialIds(unitEdits);
		var dependencies = await materialResolver.ResolveAsync(
			materialIds,
			sourceEntries,
			gameDataDirectory,
			preferredArchivesByAsset,
			cancellationToken).ConfigureAwait(false);
		if (dependencies.RejectedMaterialReasons.Count > 0)
		{
			throw new InvalidDataException(BuildDependencyFailureMessage(dependencies.RejectedMaterialReasons));
		}
		var sourceKeys = sourceEntries.Select(entry => entry.AssetKey).ToHashSet();
		var additionalEntries = dependencies.Entries.Where(entry => !sourceKeys.Contains(entry.AssetKey)).ToArray();

		var writeResult = await archiveWriter.WriteAsync(
			sourcePatchTocPath,
			outputDirectoryPath,
			unitEdits,
			additionalEntries,
			removedEntries,
			overwriteExisting,
			preserveOriginalStream: true,
			cancellationToken: cancellationToken).ConfigureAwait(false);
		return new PatchAssetReconstructionResult(writeResult, materialIds, dependencies);
	}

	private static void ValidateEdits(string sourcePatchTocPath, IReadOnlyCollection<PatchUnitMeshEditResult> edits, IReadOnlyList<PatchTocEntry> sourceEntries)
	{
		var sourcePath = Path.GetFullPath(sourcePatchTocPath);
		var knownEntries = sourceEntries.Select(CreateEntryKey).ToHashSet();
		foreach (var edit in edits)
		{
			if (!string.Equals(sourcePath, Path.GetFullPath(edit.Entry.SourceFilePath), StringComparison.OrdinalIgnoreCase) || !knownEntries.Contains(CreateEntryKey(edit.Entry)))
			{
				throw new InvalidDataException("Each Unit edit must identify an entry in the source patch.");
			}
		}
	}

	private static IReadOnlyCollection<ulong> CollectReplacementMaterialIds(IReadOnlyCollection<PatchUnitMeshEditResult> edits)
		=> edits.SelectMany(edit => edit.ReplacementMaterialIds ?? Array.Empty<ulong>()).Distinct().OrderBy(id => id).ToArray();

	private static string BuildDependencyFailureMessage(IReadOnlyDictionary<ulong, string> failures)
		=> string.Join(Environment.NewLine, failures.OrderBy(pair => pair.Key).Select(pair => $"Material 0x{pair.Key:x16}: {pair.Value}"));

	private static EntryKey CreateEntryKey(PatchTocEntry entry) => new(entry.EntryIndex, entry.AssetKey);

	private readonly record struct EntryKey(uint EntryIndex, AssetKey AssetKey);
}
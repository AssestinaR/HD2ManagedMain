using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.Analysis;

// Purpose: Builds a read-only Game Data archive index from canonical package TOCs without reading payloads.
public sealed class GameDataArchiveIndexer : IGameDataArchiveIndexer
{
	private const string SchemaVersion = "game-data-index-v1";
	private const string ParserVersion = "package-toc-v1";
	private readonly Func<string, IGameDataPackageResolver> resolverFactory;

	public GameDataArchiveIndexer(Func<string, IGameDataPackageResolver>? resolverFactory = null)
	{
		this.resolverFactory = resolverFactory ?? (directory => new GameDataPackageResolver(directory));
	}

	public async ValueTask<GameDataArchiveIndex> BuildAsync(GameDataArchiveInput input, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		var directory = Path.GetFullPath(input.GameDataDirectory);
		if (!Directory.Exists(directory))
		{
			throw new DirectoryNotFoundException($"Game Data directory was not found: {directory}");
		}

		var resolver = resolverFactory(directory);
		var packageNames = input.PackageNames?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
			?? (await resolver.GetPackageNamesAsync(cancellationToken).ConfigureAwait(false)).ToArray();
		var archives = new List<GameDataArchiveFact>();
		var issues = new List<PatchAnalysisIssue>();
		foreach (var packageName in packageNames)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var metadata = input.MetadataByPackageName is not null && input.MetadataByPackageName.TryGetValue(packageName, out var value) ? value : null;
			try
			{
				var packageToc = await resolver.GetPackageTocAsync(packageName, cancellationToken).ConfigureAwait(false);
				if (packageToc is null)
				{
					var issue = new PatchAnalysisIssue("MissingArchiveToc", $"Game Data archive TOC was not found: {packageName}", packageName);
					archives.Add(new GameDataArchiveFact(packageName, metadata?.ArchiveHex, metadata?.DisplayName, metadata?.Category, false, Array.Empty<GameDataArchiveEntryFact>(), new[] { issue }));
					issues.Add(issue);
					continue;
				}

				var entries = new PatchTocScanner().ScanEntries(packageToc.Data, packageName, packageToc.UsesSlimEntryOffset)
					.Select(entry => new GameDataArchiveEntryFact(entry.AssetKey, packageName, entry.EntryIndex, entry.TocDataOffset, entry.StreamOffset, entry.GpuResourceOffset,
						entry.TocDataSize, entry.StreamSize, entry.GpuResourceSize, entry.Unknown1, entry.Unknown2, entry.Unknown3, entry.Unknown4)).ToArray();
				archives.Add(new GameDataArchiveFact(packageName, metadata?.ArchiveHex, metadata?.DisplayName, metadata?.Category, packageToc.UsesSlimEntryOffset, entries, Array.Empty<PatchAnalysisIssue>()));
			}
			catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or IOException or OverflowException or UnauthorizedAccessException)
			{
				var issue = new PatchAnalysisIssue("InvalidArchiveToc", exception.Message, packageName);
				archives.Add(new GameDataArchiveFact(packageName, metadata?.ArchiveHex, metadata?.DisplayName, metadata?.Category, false, Array.Empty<GameDataArchiveEntryFact>(), new[] { issue }));
				issues.Add(issue);
			}
		}

		return new GameDataArchiveIndex(input with { GameDataDirectory = directory }, archives, issues, DateTimeOffset.UtcNow, SchemaVersion, ParserVersion);
	}
}

using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Builds ordered asset override chains and per-mod coverage status for an enabled mod list.
public sealed class ModAssetOverrideAnalyzer : IModAssetOverrideAnalyzer
{
	private readonly IModAssetAnalyzer _assetAnalyzer;

	public ModAssetOverrideAnalyzer(IModAssetAnalyzer assetAnalyzer)
	{
		_assetAnalyzer = assetAnalyzer ?? throw new ArgumentNullException(nameof(assetAnalyzer));
	}

	public async ValueTask<ModAssetOverrideAnalysis> AnalyzeAsync(
		IReadOnlyList<ProfileEntry> orderedEntries,
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default)
	{
		if (orderedEntries is null)
		{
			throw new ArgumentNullException(nameof(orderedEntries));
		}
		if (snapshot is null)
		{
			throw new ArgumentNullException(nameof(snapshot));
		}

		var summaries = new List<(ProfileEntry Entry, ModAssetSummary Summary)>();
		foreach (var entry in orderedEntries.Where(e => e.Enabled).OrderBy(e => e.LoadOrder))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!snapshot.Nodes.TryGetValue(entry.NodeId, out var node))
			{
				continue;
			}

			var summary = await _assetAnalyzer.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
			summaries.Add((entry, summary));
		}

		var chains = summaries
			.SelectMany(x => x.Summary.Assets.Select(asset => new { x.Entry, x.Summary, Asset = asset }))
			.GroupBy(x => x.Asset.Key)
			.Where(g => g.Count() > 1)
			.Select(g => new ModAssetOverrideChain(
				g.Key,
				g.OrderBy(x => x.Entry.LoadOrder)
					.Select((x, index) => new ModAssetOverrideEntry(
						x.Summary.NodeId,
						x.Summary.Name,
						x.Entry.LoadOrder,
						x.Asset,
						index == g.Count() - 1))
					.ToList()))
			.OrderBy(c => c.Key.ArchiveId, StringComparer.OrdinalIgnoreCase)
			.ThenBy(c => c.Key.TypeId)
			.ThenBy(c => c.Key.FileId)
			.ToList();

		var overriddenByNode = chains
			.SelectMany(chain => chain.Entries.Where(e => !e.IsWinner))
			.GroupBy(e => e.NodeId)
			.ToDictionary(g => g.Key, g => g.Count());

		var coverages = summaries
			.Select(x =>
			{
				overriddenByNode.TryGetValue(x.Summary.NodeId, out var overridden);
				var total = x.Summary.Assets.Count;
				return new ModOverrideCoverage(x.Summary.NodeId, x.Summary.Name, total, overridden, total > 0 && overridden >= total);
			})
			.ToList();

		return new ModAssetOverrideAnalysis(summaries.Select(x => x.Summary).ToList(), chains, coverages);
	}
}
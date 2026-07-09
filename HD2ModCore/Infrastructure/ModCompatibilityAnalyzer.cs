using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Compares a mod's patched asset keys against the current game data reverse index.
public sealed class ModCompatibilityAnalyzer : IModCompatibilityAnalyzer
{
	private readonly IAssetArchiveIndexService _indexService;

	public ModCompatibilityAnalyzer(IAssetArchiveIndexService indexService)
	{
		_indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
	}

	public async ValueTask<ModCompatibilityReport> AnalyzeAsync(ModAssetSummary summary, CancellationToken cancellationToken = default)
	{
		if (summary is null)
		{
			throw new ArgumentNullException(nameof(summary));
		}

		var fingerprint = await _indexService.GetFingerprintAsync(cancellationToken).ConfigureAwait(false);
		var keys = summary.Assets
			.Select(x => x.Key.AssetKey)
			.ToHashSet();

		if (keys.Count == 0)
		{
			return new ModCompatibilityReport(
				summary.NodeId,
				summary.Name,
				ModCompatibilityStatus.Unknown,
				0,
				0,
				0,
				0,
				Array.Empty<AssetArchiveMatch>(),
				fingerprint);
		}

		if (fingerprint is null)
		{
			return new ModCompatibilityReport(
				summary.NodeId,
				summary.Name,
				ModCompatibilityStatus.Unknown,
				keys.Count,
				0,
				keys.Count,
				0,
				Array.Empty<AssetArchiveMatch>(),
				null);
		}

		var matches = await _indexService.FindAssetArchivesAsync(keys, cancellationToken).ConfigureAwait(false);
		var matched = matches.Count(x => x.Found);
		var missing = keys.Count - matched;
		var ratio = matched / (double)keys.Count;

		return new ModCompatibilityReport(
			summary.NodeId,
			summary.Name,
			Classify(keys.Count, ratio),
			keys.Count,
			matched,
			missing,
			ratio,
			matches,
			fingerprint);
	}

	private static ModCompatibilityStatus Classify(int total, double ratio)
	{
		if (total <= 0)
		{
			return ModCompatibilityStatus.Unknown;
		}

		if (ratio >= 0.9)
		{
			return ModCompatibilityStatus.Compatible;
		}

		if (ratio >= 0.5)
		{
			return ModCompatibilityStatus.Partial;
		}

		return ModCompatibilityStatus.LikelyOutdated;
	}
}
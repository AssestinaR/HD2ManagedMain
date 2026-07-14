namespace HD2ModAdaptation.Analysis;

// Purpose: Orchestrates read-only Adaptation analyzers into one versioned, Core-neutral snapshot.
public sealed class AdaptationAnalysisBuilder : IAdaptationAnalysisBuilder
{
	private const string SchemaVersion = "adaptation-analysis-v1";
	private const string AnalyzerVersion = "adaptation-analysis-v1";
	private readonly IPatchGroupAnalyzer patchGroupAnalyzer;
	private readonly IGameItemResourceRelationBuilder itemRelationBuilder;
	private readonly IResourceReuseDetector reuseDetector;
	private readonly IReplacementIntegrityAnalyzer replacementIntegrityAnalyzer;

	public AdaptationAnalysisBuilder(
		IPatchGroupAnalyzer? patchGroupAnalyzer = null,
		IGameItemResourceRelationBuilder? itemRelationBuilder = null,
		IResourceReuseDetector? reuseDetector = null,
		IReplacementIntegrityAnalyzer? replacementIntegrityAnalyzer = null)
	{
		this.patchGroupAnalyzer = patchGroupAnalyzer ?? new PatchGroupAnalyzer();
		this.itemRelationBuilder = itemRelationBuilder ?? new GameItemResourceRelationBuilder();
		this.reuseDetector = reuseDetector ?? new ResourceReuseDetector();
		this.replacementIntegrityAnalyzer = replacementIntegrityAnalyzer ?? new ReplacementIntegrityAnalyzer();
	}

	public async ValueTask<AdaptationAnalysisSnapshot> BuildAsync(
		AdaptationAnalysisInput input,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentException.ThrowIfNullOrWhiteSpace(input.SourceFingerprint);
		ArgumentNullException.ThrowIfNull(input.PatchGroups);
		ArgumentNullException.ThrowIfNull(input.Items);

		var issues = new List<PatchAnalysisIssue>();
		var patchGroups = new List<PatchGroupAnalysis>(input.PatchGroups.Count);
		foreach (var patchGroup in input.PatchGroups)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var analysis = await patchGroupAnalyzer.AnalyzeAsync(patchGroup, cancellationToken).ConfigureAwait(false);
			patchGroups.Add(analysis);
			issues.AddRange(analysis.Issues);
		}

		IReadOnlyList<GameItemResourceInfo> items;
		if (input.GameDataIndex is null)
		{
			items = Array.Empty<GameItemResourceInfo>();
			if (input.Items.Count > 0)
				issues.Add(new PatchAnalysisIssue("MissingGameDataIndex", "Item resource relations were not built because no Game Data index was supplied."));
		}
		else
		{
			items = await itemRelationBuilder.BuildAsync(input.GameDataIndex, input.Items, cancellationToken).ConfigureAwait(false);
			issues.AddRange(items.SelectMany(item => item.Issues));
		}

		var reuseGroups = reuseDetector.Detect(items, input.SourceFingerprint);
		var modifiedAssets = patchGroups
			.SelectMany(group => group.Assets)
			.Select(asset => asset.AssetKey)
			.Distinct()
			.ToArray();
		var replacementFindings = replacementIntegrityAnalyzer.Analyze(items, reuseGroups, modifiedAssets, input.SourceFingerprint);
		return new AdaptationAnalysisSnapshot(input, patchGroups, items, reuseGroups, replacementFindings, issues, DateTimeOffset.UtcNow, SchemaVersion, AnalyzerVersion);
	}
}
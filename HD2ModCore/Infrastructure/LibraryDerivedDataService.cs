using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Centralizes file-system-derived library facts such as directories, icons, patch indexes and asset summaries.
public sealed class LibraryDerivedDataService : ILibraryDerivedDataService
{
	private readonly IModContentFactsService _contentFactsService;
	private readonly ModAssetSummaryProjector _assetSummaryProjector;
	private readonly IModUnitCompatibilityAnalyzer? _unitCompatibilityAnalyzer;

	public LibraryDerivedDataService(IModContentFactsService contentFactsService, ModAssetSummaryProjector assetSummaryProjector, IModUnitCompatibilityAnalyzer? unitCompatibilityAnalyzer = null)
	{
		_contentFactsService = contentFactsService ?? throw new ArgumentNullException(nameof(contentFactsService));
		_assetSummaryProjector = assetSummaryProjector ?? throw new ArgumentNullException(nameof(assetSummaryProjector));
		_unitCompatibilityAnalyzer = unitCompatibilityAnalyzer;
	}

	public async ValueTask<DerivedLibraryData> BuildAsync(LibrarySnapshot snapshot, string modsRootDirectory, string? gameDataDirectory = null, IReadOnlySet<ModNodeId>? nodeIds = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		if (string.IsNullOrWhiteSpace(modsRootDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(modsRootDirectory));
		}

		var contentFacts = await _contentFactsService.GetLibraryFactsAsync(snapshot, modsRootDirectory, nodeIds, cancellationToken).ConfigureAwait(false);
		var issues = new List<CoreIssue>(contentFacts.Values.SelectMany(facts => facts.Issues));
		var nodes = new Dictionary<ModNodeId, DerivedModNodeData>();

		foreach (var node in snapshot.Nodes.Values)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (nodeIds is not null && !nodeIds.Contains(node.Id))
			{
				continue;
			}
			var directory = ResolveNodeDirectory(modsRootDirectory, node.RelativePath);
			var directoryExists = Directory.Exists(directory);
			if (!contentFacts.TryGetValue(node.Id, out var nodeContentFacts))
			{
				continue;
			}
			var patchFiles = nodeContentFacts.ToPatchFileIndex();

			ModAssetSummary? assetSummary = null;
			try
			{
				assetSummary = await _assetSummaryProjector.ProjectAsync(node, nodeContentFacts, cancellationToken);
			}
			catch (Exception ex)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "AssetSummaryProjectionFailed", $"Failed to project stable mod facts: {ex.Message}", directory, node.Id));
			}

			ModUnitCompatibilityReport? unitCompatibility = null;
			if (_unitCompatibilityAnalyzer is not null)
			{
				try
				{
					unitCompatibility = await _unitCompatibilityAnalyzer.AnalyzeNodeAsync(node, modsRootDirectory, gameDataDirectory, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "UnitCompatibilityFailed", $"Failed to analyze unit compatibility: {ex.Message}", directory, node.Id));
				}
			}

			var nodeIssues = issues.Where(i => i.NodeId == node.Id).ToList();
			nodes[node.Id] = new DerivedModNodeData(
				NodeId: node.Id,
				RelativePath: node.RelativePath,
				AbsoluteDirectory: directory,
				DirectoryExists: directoryExists,
				IconPath: ModIconLocator.TryResolve(directory),
				PatchFiles: patchFiles,
				ContentFacts: nodeContentFacts,
				AssetSummary: assetSummary,
				UnitCompatibility: unitCompatibility,
				Issues: nodeIssues);
		}

		return new DerivedLibraryData(DateTimeOffset.UtcNow, nodes, issues);
	}

	private static string ResolveNodeDirectory(string modsRootDirectory, string relativePath)
	{
		return Path.GetFullPath(Path.Combine(modsRootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
	}
}
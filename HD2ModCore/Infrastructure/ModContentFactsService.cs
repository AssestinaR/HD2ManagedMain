using System.Security.Cryptography;
using System.Text;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Unifies top-level patch discovery, sidecar grouping and Adaptation-owned AssetKey analysis into one content snapshot.
public sealed class ModContentFactsService : IModContentFactsService
{
	private readonly IPatchFileNameParser _fileNameParser;
	private readonly IPatchGroupAnalysisProvider _analysisProvider;
	private readonly IUnitVersionProbe _unitVersionProbe;

	public ModContentFactsService(IPatchFileNameParser fileNameParser, IPatchGroupAnalysisProvider analysisProvider, IUnitVersionProbe? unitVersionProbe = null)
	{
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		_analysisProvider = analysisProvider ?? throw new ArgumentNullException(nameof(analysisProvider));
		_unitVersionProbe = unitVersionProbe ?? new UnitVersionProbe();
	}

	public async ValueTask<ModContentFacts> GetNodeFactsAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentException.ThrowIfNullOrWhiteSpace(modsRootDirectory);

		var directory = Path.GetFullPath(Path.Combine(modsRootDirectory, node.RelativePath));
		var issues = new List<CoreIssue>();
		if (!Directory.Exists(directory))
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "ModDirectoryMissing", $"Mod directory does not exist: {directory}", directory, node.Id));
			return new ModContentFacts(node.Id, node.RelativePath, ComputeGeneration(Array.Empty<ModPatchGroupFileFact>()), DateTimeOffset.UtcNow, Array.Empty<ModPatchGroupFact>(), issues);
		}

		var discovered = DiscoverGroups(node, directory, issues, cancellationToken);
		IReadOnlyList<HD2ModAdaptation.Analysis.PatchGroupAnalysis> analyses;
		try
		{
			analyses = await _analysisProvider.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			analyses = Array.Empty<HD2ModAdaptation.Analysis.PatchGroupAnalysis>();
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, "PatchAnalysisFailed", exception.Message, directory, node.Id, exception.ToString()));
		}

		var analysisByBasePath = analyses
			.GroupBy(analysis => Path.GetFullPath(analysis.Input.PatchTocFilePath), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
		var groups = new List<ModPatchGroupFact>();
		foreach (var discoveredGroup in discovered)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var groupIssues = new List<CoreIssue>(discoveredGroup.Issues);
			var assetKeys = new HashSet<AssetKey>();
			HD2ModAdaptation.Analysis.PatchGroupAnalysis? analysis = null;
			var baseFile = discoveredGroup.Files.FirstOrDefault(file => file.SidecarKind == PatchSidecarKind.Base);
			if (baseFile is not null && analysisByBasePath.TryGetValue(Path.GetFullPath(baseFile.FilePath), out var resolvedAnalysis))
			{
				analysis = resolvedAnalysis;
				foreach (var asset in analysis.Assets)
				{
					assetKeys.Add(new AssetKey(asset.AssetKey.TypeId, asset.AssetKey.FileId));
				}
				foreach (var issue in analysis.Issues)
				{
					groupIssues.Add(new CoreIssue(
						IsFatalAnalysisIssue(issue.Code) ? CoreIssueSeverity.Error : CoreIssueSeverity.Warning,
						issue.Code,
						issue.Message,
						issue.SourceFilePath ?? baseFile.FilePath,
						node.Id));
				}
			}
			else if (baseFile is not null)
			{
				groupIssues.Add(new CoreIssue(CoreIssueSeverity.Error, "PatchAnalysisMissing", "No Adaptation analysis was produced for the base patch.", baseFile.FilePath, node.Id));
			}

			issues.AddRange(groupIssues);
			var unitVersions = analysis is null
				? Array.Empty<UnitVersionEvidence>()
				: await _unitVersionProbe.ProbeAsync(analysis, cancellationToken).ConfigureAwait(false);
			groups.Add(new ModPatchGroupFact(discoveredGroup.Id, discoveredGroup.NormalizedOrder, discoveredGroup.Files, assetKeys, groupIssues, unitVersions));
		}

		var allFiles = groups.SelectMany(group => group.Files).ToList();
		return new ModContentFacts(node.Id, node.RelativePath, ComputeGeneration(allFiles), DateTimeOffset.UtcNow, groups, issues);
	}

	public async ValueTask<IReadOnlyDictionary<ModNodeId, ModContentFacts>> GetLibraryFactsAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		IReadOnlySet<ModNodeId>? nodeIds = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		var result = new Dictionary<ModNodeId, ModContentFacts>();
		foreach (var node in snapshot.Nodes.Values)
		{
			if (nodeIds is not null && !nodeIds.Contains(node.Id)) continue;
			cancellationToken.ThrowIfCancellationRequested();
			result[node.Id] = await GetNodeFactsAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		}
		return result;
	}

	private IReadOnlyList<DiscoveredPatchGroup> DiscoverGroups(ModNode node, string directory, ICollection<CoreIssue> issues, CancellationToken cancellationToken)
	{
		var parsed = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
			.Select(path => (Path: path, Info: TryParse(path)))
			.Where(item => item.Info is not null)
			.Select(item => (item.Path, Info: item.Info!))
			.ToList();
		var result = new List<DiscoveredPatchGroup>();
		foreach (var archiveGroup in parsed.GroupBy(item => item.Info.ArchiveHex16, StringComparer.OrdinalIgnoreCase))
		{
			var sourceIndexes = archiveGroup.Select(item => item.Info.PatchIndex).Distinct().OrderBy(index => index).ToList();
			var normalizedBySourceIndex = sourceIndexes.Select((source, normalized) => (source, normalized)).ToDictionary(item => item.source, item => item.normalized);
			foreach (var patchGroup in archiveGroup.GroupBy(item => item.Info.PatchIndex).OrderBy(group => group.Key))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var groupIssues = new List<CoreIssue>();
				var files = patchGroup
					.OrderBy(item => item.Info.SidecarKind)
					.Select(item => CreateFileFact(item.Path, item.Info.SidecarKind))
					.ToList();
				if (files.All(file => file.SidecarKind != PatchSidecarKind.Base))
				{
					var path = files.FirstOrDefault()?.FilePath;
					var issue = new CoreIssue(CoreIssueSeverity.Error, "SidecarWithoutBase", "Patch sidecar has no base patch.", path, node.Id);
					groupIssues.Add(issue);
				}
				var id = new ModPatchGroupId(node.Id, archiveGroup.Key.ToLowerInvariant(), patchGroup.Key);
				result.Add(new DiscoveredPatchGroup(id, normalizedBySourceIndex[patchGroup.Key], files, groupIssues));
			}
		}
		return result;
	}

	private PatchFileNameInfo? TryParse(string path)
		=> _fileNameParser.TryParse(Path.GetFileName(path), out var info) ? info : null;

	private static ModPatchGroupFileFact CreateFileFact(string path, PatchSidecarKind kind)
	{
		var file = new FileInfo(path);
		return new ModPatchGroupFileFact(kind, path, file.Name, file.Length, file.LastWriteTimeUtc);
	}

	private static bool IsFatalAnalysisIssue(string code)
		=> code is "InvalidToc" or "MissingToc";

	private static string ComputeGeneration(IEnumerable<ModPatchGroupFileFact> files)
	{
		var builder = new StringBuilder();
		foreach (var file in files.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase))
		{
			builder.Append(file.FileName.ToLowerInvariant()).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.UtcTicks).AppendLine();
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}

	private sealed record DiscoveredPatchGroup(
		ModPatchGroupId Id,
		int NormalizedOrder,
		IReadOnlyList<ModPatchGroupFileFact> Files,
		IReadOnlyList<CoreIssue> Issues);
}

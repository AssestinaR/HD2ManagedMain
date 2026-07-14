using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Discovers patch groups for a mod and delegates patch fact reading to Adaptation.
public sealed class AdaptationPatchGroupAnalysisProvider : IPatchGroupAnalysisProvider
{
	private readonly IPatchFileNameParser _fileNameParser;
	private readonly IPatchGroupAnalyzer _analyzer;

	public AdaptationPatchGroupAnalysisProvider(
		IPatchFileNameParser fileNameParser,
		IPatchGroupAnalyzer analyzer)
	{
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		_analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
	}

	public async ValueTask<IReadOnlyList<PatchGroupAnalysis>> AnalyzeNodeAsync(
		ModNode node,
		string modsRootDirectory,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentException.ThrowIfNullOrWhiteSpace(modsRootDirectory);

		var nodeDirectory = Path.Combine(modsRootDirectory, node.RelativePath);
		if (!Directory.Exists(nodeDirectory))
		{
			return Array.Empty<PatchGroupAnalysis>();
		}

		var results = new List<PatchGroupAnalysis>();
		foreach (var path in Directory.EnumerateFiles(nodeDirectory, "*", SearchOption.TopDirectoryOnly))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var fileName = Path.GetFileName(path);
			if (!_fileNameParser.TryParse(fileName, out var parsed) || parsed is null || parsed.SidecarKind != PatchSidecarKind.Base)
			{
				continue;
			}

			results.Add(await _analyzer.AnalyzeAsync(
				new PatchGroupInput(
					path,
					File.Exists(path + ".stream") ? path + ".stream" : null,
					File.Exists(path + ".gpu_resources") ? path + ".gpu_resources" : null),
				cancellationToken).ConfigureAwait(false));
		}

		return results;
	}
}

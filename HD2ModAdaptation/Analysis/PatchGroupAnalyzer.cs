using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Analysis;

// Purpose: Performs the first low-cost patch-group analysis using the canonical TOC scanner.
public sealed class PatchGroupAnalyzer : IPatchGroupAnalyzer
{
	private const string AnalyzerVersion = "patch-group-v1";
	private readonly IPatchTocScanner tocScanner;

	public PatchGroupAnalyzer(IPatchTocScanner? tocScanner = null)
	{
		this.tocScanner = tocScanner ?? new PatchTocScanner();
	}

	public async ValueTask<PatchGroupAnalysis> AnalyzeAsync(PatchGroupInput input, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		var issues = new List<PatchAnalysisIssue>();
		var assets = new List<PatchAssetFact>();
		var tocPath = Path.GetFullPath(input.PatchTocFilePath);
		if (!File.Exists(tocPath))
		{
			issues.Add(new PatchAnalysisIssue("MissingToc", $"Patch TOC was not found: {tocPath}", tocPath));
			return CreateResult(input, assets, issues);
		}

		try
		{
			var entries = await tocScanner.ScanEntriesAsync(tocPath, cancellationToken).ConfigureAwait(false);
			foreach (var entry in entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var typeId = entry.AssetKey.TypeId;
				assets.Add(new PatchAssetFact(entry.AssetKey, entry.SourceFilePath, entry.TocDataSize, entry.StreamSize, entry.GpuResourceSize,
					typeId == PatchUnitMeshReader.UnitTypeId,
					typeId == PatchUnitMeshReader.CompositeUnitTypeId,
					typeId == MaterialDependencyResolver.MaterialTypeId,
					typeId == MaterialDependencyResolver.TextureTypeId));
			}
		}
		catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or IOException)
		{
			issues.Add(new PatchAnalysisIssue("InvalidToc", exception.Message, tocPath));
		}

		return CreateResult(input, assets, issues);
	}

	private static PatchGroupAnalysis CreateResult(PatchGroupInput input, IReadOnlyList<PatchAssetFact> assets, IReadOnlyList<PatchAnalysisIssue> issues)
		=> new(input, assets, issues, DateTimeOffset.UtcNow, AnalyzerVersion);
}

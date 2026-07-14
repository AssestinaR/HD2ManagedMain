using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Analysis;

// Purpose: Performs the first low-cost patch-group analysis using the canonical TOC scanner.
public sealed class PatchGroupAnalyzer : IPatchGroupAnalyzer
{
	private const string AnalyzerVersion = "patch-group-v2";
	private readonly IPatchTocScanner tocScanner;
	private readonly IPatchEntryPayloadReader payloadReader;
	private readonly IUnitMaterialReferenceReader unitMaterialReader;
	private readonly StingrayMaterialReferenceReader materialReader;

	public PatchGroupAnalyzer(
		IPatchTocScanner? tocScanner = null,
		IPatchEntryPayloadReader? payloadReader = null,
		IUnitMaterialReferenceReader? unitMaterialReader = null,
		StingrayMaterialReferenceReader? materialReader = null)
	{
		this.tocScanner = tocScanner ?? new PatchTocScanner();
		this.payloadReader = payloadReader ?? new PatchEntryPayloadReader();
		this.unitMaterialReader = unitMaterialReader ?? new UnitMaterialReferenceReader();
		this.materialReader = materialReader ?? new StingrayMaterialReferenceReader();
	}

	public async ValueTask<PatchGroupAnalysis> AnalyzeAsync(PatchGroupInput input, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		var issues = new List<PatchAnalysisIssue>();
		var assets = new List<PatchAssetFact>();
		var references = new List<PatchAssetReference>();
		var tocPath = Path.GetFullPath(input.PatchTocFilePath);
		if (!File.Exists(tocPath))
		{
			issues.Add(new PatchAnalysisIssue("MissingToc", $"Patch TOC was not found: {tocPath}", tocPath));
			return CreateResult(input, assets, references, issues);
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
				await ReadReferencesAsync(entry, references, issues, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or IOException)
		{
			issues.Add(new PatchAnalysisIssue("InvalidToc", exception.Message, tocPath));
		}

		return CreateResult(input, assets, references, issues);
	}

	private async ValueTask ReadReferencesAsync(PatchTocEntry entry, ICollection<PatchAssetReference> references, ICollection<PatchAnalysisIssue> issues, CancellationToken cancellationToken)
	{
		var isUnit = entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId;
		var isMaterial = entry.AssetKey.TypeId == MaterialDependencyResolver.MaterialTypeId;
		var minimumPayloadLength = isUnit ? 0x74 : isMaterial ? 136 : 0;
		if (minimumPayloadLength == 0 || entry.TocDataSize < minimumPayloadLength)
		{
			return;
		}

		try
		{
			var payload = await payloadReader.ReadPayloadAsync(entry, cancellationToken).ConfigureAwait(false);
			if (isUnit)
			{
				foreach (var binding in unitMaterialReader.ReadReferenceBindings(payload.TocData).Where(binding => binding.MaterialId != 0))
				{
					references.Add(new PatchAssetReference(entry.AssetKey, new AssetKey(MaterialDependencyResolver.MaterialTypeId, binding.MaterialId), PatchReferenceKind.UnitMaterial, binding.MaterialIdPayloadRelativeOffset, binding.SectionId));
				}
			}
			else
			{
				var textureIds = materialReader.ReadTextureIds(payload.TocData);
				for (var index = 0; index < textureIds.Count; index++)
				{
					if (textureIds[index] != 0)
					{
						references.Add(new PatchAssetReference(entry.AssetKey, new AssetKey(MaterialDependencyResolver.TextureTypeId, textureIds[index]), PatchReferenceKind.MaterialTexture, checked((uint)(136 + textureIds.Count * 4 + index * 8)), ReferenceIndex: index));
					}
				}
			}
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or OverflowException)
		{
			issues.Add(new PatchAnalysisIssue(isUnit ? "InvalidUnitMaterialReferences" : "InvalidMaterialTextureReferences", exception.Message, entry.SourceFilePath, entry.AssetKey));
		}
	}

	private static PatchGroupAnalysis CreateResult(PatchGroupInput input, IReadOnlyList<PatchAssetFact> assets, IReadOnlyList<PatchAssetReference> references, IReadOnlyList<PatchAnalysisIssue> issues)
		=> new(input, assets, references, issues, DateTimeOffset.UtcNow, AnalyzerVersion);
}

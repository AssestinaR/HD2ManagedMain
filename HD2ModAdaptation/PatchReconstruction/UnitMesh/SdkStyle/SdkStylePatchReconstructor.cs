using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Writes SDK-style Unit reconstruction output while removing obsolete source Units and adding material dependencies.
public sealed class SdkStylePatchReconstructor
{
	private readonly IPatchTocScanner scanner;
	private readonly IPatchEntryPayloadReader payloadReader;
	private readonly MaterialDependencyResolver materialResolver;
	private readonly PatchArchiveWriter writer;

	public SdkStylePatchReconstructor(
		IPatchTocScanner? scanner = null,
		IPatchEntryPayloadReader? payloadReader = null,
		MaterialDependencyResolver? materialResolver = null,
		PatchArchiveWriter? writer = null)
	{
		this.scanner = scanner ?? new PatchTocScanner();
		this.payloadReader = payloadReader ?? new PatchEntryPayloadReader();
		this.materialResolver = materialResolver ?? new MaterialDependencyResolver(this.scanner, this.payloadReader);
		this.writer = writer ?? new PatchArchiveWriter(this.scanner, this.payloadReader);
	}

	public async ValueTask<SdkStylePatchReconstructionResult> WriteAsync(
		string sourcePatchTocPath,
		string outputDirectoryPath,
		SdkStyleUnitOutput output,
		string gameDataDirectory,
		IReadOnlyDictionary<AssetKey, IReadOnlyList<string>> preferredArchivesByAsset,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePatchTocPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(gameDataDirectory);
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(preferredArchivesByAsset);
		var sourcePath = Path.GetFullPath(sourcePatchTocPath);
		var entries = await scanner.ScanEntriesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
		var removals = await GetSafeRemovalsAsync(entries, output.ReplacedSourceUnitAssetKeys, cancellationToken).ConfigureAwait(false);
		var additions = output.AdditionalEntries.ToList();
		var seenAdditions = additions.Select(entry => entry.AssetKey).ToHashSet();
		var materialDependencies = await materialResolver.ResolveAsync(
			output.ReplacementMaterialIds,
			entries,
			gameDataDirectory,
			preferredArchivesByAsset,
			cancellationToken).ConfigureAwait(false);
		foreach (var dependencyEntry in materialDependencies.Entries)
		{
			if (seenAdditions.Add(dependencyEntry.AssetKey))
			{
				additions.Add(dependencyEntry);
			}
		}

		var writeResult = await writer.WriteAsync(
			sourcePath,
			outputDirectoryPath,
			Array.Empty<PatchUnitMeshEditResult>(),
			additions,
			removals,
			overwriteExisting,
			preserveOriginalStream: true,
			headerTemplateTocData: output.HeaderTemplateTocData,
			cancellationToken: cancellationToken).ConfigureAwait(false);
		return new SdkStylePatchReconstructionResult(
			writeResult,
			output.TargetUnitAssetKeys,
			output.AvatarUnitAssetKey,
			removals.Select(entry => entry.AssetKey).ToArray(),
			materialDependencies);
	}

	private async ValueTask<IReadOnlyCollection<PatchTocEntry>> GetSafeRemovalsAsync(
		IReadOnlyList<PatchTocEntry> entries,
		IReadOnlyCollection<AssetKey> replacedSourceUnitKeys,
		CancellationToken cancellationToken)
	{
		if (replacedSourceUnitKeys.Count == 0)
		{
			throw new InvalidDataException("A SDK-style reconstruction must replace at least one old source Unit.");
		}

		var sourceUnits = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).ToArray();
		var explicitReplacedUnits = sourceUnits.Where(entry => replacedSourceUnitKeys.Contains(entry.AssetKey)).ToArray();
		if (explicitReplacedUnits.Length != replacedSourceUnitKeys.Count)
		{
			throw new InvalidDataException("One or more explicit source Unit removals do not belong to the source patch.");
		}

		var compositeByUnit = new Dictionary<AssetKey, ulong>();
		foreach (var unit in sourceUnits)
		{
			var payload = await payloadReader.ReadPayloadAsync(unit, cancellationToken).ConfigureAwait(false);
			compositeByUnit.Add(unit.AssetKey, ReadReference(payload.TocData, 16, "Composite"));
		}

		var removedUnits = sourceUnits;
		var removedUnitKeys = removedUnits.Select(entry => entry.AssetKey).ToHashSet();
		var candidateCompositeIds = removedUnits
			.Select(entry => compositeByUnit[entry.AssetKey])
			.Where(id => id != 0)
			.ToHashSet();
		var referencedByRetainedUnit = sourceUnits
			.Where(entry => !removedUnitKeys.Contains(entry.AssetKey))
			.Select(entry => compositeByUnit[entry.AssetKey])
			.ToHashSet();
		var removableCompositeIds = candidateCompositeIds.Where(id => !referencedByRetainedUnit.Contains(id)).ToHashSet();
		var composites = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.CompositeUnitTypeId && removableCompositeIds.Contains(entry.AssetKey.FileId));
		return removedUnits.Concat(composites).ToArray();
	}

	private static ulong ReadReference(ReadOnlySpan<byte> tocData, int offset, string name)
	{
		if (tocData.Length < offset + sizeof(ulong))
		{
			throw new InvalidDataException($"Unit TocData is too short to read its {name} reference.");
		}

		return BitConverter.ToUInt64(tocData.Slice(offset, sizeof(ulong)));
	}
}

public sealed record SdkStylePatchReconstructionResult(
	PatchArchiveFileWriteResult WriteResult,
	IReadOnlyCollection<AssetKey> TargetUnitAssetKeys,
	AssetKey AvatarUnitAssetKey,
	IReadOnlyCollection<AssetKey> RemovedAssetKeys,
	MaterialDependencyResolutionResult MaterialDependencies);
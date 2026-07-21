using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Writes approved cross-armor target-shell work items while preserving Patch/sidecar and material-closure binary rules inside Adaptation.
namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

public sealed class CrossArmorTargetShellPatchOperation
{
	private const ulong CompositeUnitTypeId = 0xc4f0f4be7fb0c8d6;
	private readonly PatchTocScanner scanner;
	private readonly PatchEntryPayloadReader payloadReader;
	private readonly PatchArchiveWriter archiveWriter;

	public CrossArmorTargetShellPatchOperation(
		PatchTocScanner? scanner = null,
		PatchEntryPayloadReader? payloadReader = null,
		PatchArchiveWriter? archiveWriter = null)
	{
		this.scanner = scanner ?? new PatchTocScanner();
		this.payloadReader = payloadReader ?? new PatchEntryPayloadReader();
		this.archiveWriter = archiveWriter ?? new PatchArchiveWriter(this.scanner, this.payloadReader);
	}

	public async ValueTask<CrossArmorTargetShellPatchOperationResult> ExecuteAsync(
		CrossArmorTargetShellPatchOperationRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		request.Validate();
		var entries = request.PreparedSourceEntries is { Count: > 0 }
			? request.PreparedSourceEntries
			: await scanner.ScanEntriesAsync(request.SourcePatchTocPath, cancellationToken).ConfigureAwait(false);
		var outputBuilder = new SdkStyleTargetShellPatchOutputBuilder(
			new SdkStyleTargetShellUnitReconstructor(
				reencoder: new SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true, canonicalBoneHashOrder: request.CanonicalBoneHashOrder),
				writer: new UnitMeshWriter(allowBoneInfoRelocation: true, allowTransformInfoRelocation: true),
				propagateSourceMaterials: true,
				allowedSourceMaterialIds: request.AllowedSourceMaterialIds,
				planCanonicalSkinningLayout: true,
				streamLayoutRegistry: request.StreamLayoutRegistry));
		var output = outputBuilder.Build(request.WorkItems);
		var removals = await GetSourceUnitAndCompositeRemovalsAsync(entries, cancellationToken).ConfigureAwait(false);
		var preservedSourceKeys = entries.Where(entry => !removals.Contains(entry)).Select(entry => entry.AssetKey).ToHashSet();
		var additionalEntries = output.AdditionalEntries
			.Concat(request.IncludeResolvedMaterialDependencies ? request.MaterialDependencies : Array.Empty<PatchArchiveAdditionalEntry>())
			.Where(entry => !preservedSourceKeys.Contains(entry.AssetKey))
			.GroupBy(entry => entry.AssetKey)
			.Select(group => group.First())
			.ToArray();
		var write = await archiveWriter.WriteAsync(
			request.SourcePatchTocPath,
			request.OutputDirectory,
			Array.Empty<PatchUnitMeshEditResult>(),
			additionalEntries,
			removals,
			preserveOriginalStream: true,
			headerTemplateTocData: request.HeaderTemplateTocData,
			cancellationToken: cancellationToken).ConfigureAwait(false);
		await VerifyAsync(write.TocFilePath, output.UnitResults.Select(result => result.TargetUnitAssetKey).ToHashSet(), cancellationToken).ConfigureAwait(false);
		return new CrossArmorTargetShellPatchOperationResult(write, output, entries, removals);
	}

	private async ValueTask<IReadOnlyList<PatchTocEntry>> GetSourceUnitAndCompositeRemovalsAsync(IReadOnlyList<PatchTocEntry> entries, CancellationToken cancellationToken)
	{
		var units = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).ToArray();
		var compositeIds = new HashSet<ulong>();
		foreach (var unit in units)
		{
			var payload = await payloadReader.ReadPayloadAsync(unit, cancellationToken).ConfigureAwait(false);
			if (payload.TocData.Length >= 24)
			{
				var compositeId = BitConverter.ToUInt64(payload.TocData, 16);
				if (compositeId != 0) compositeIds.Add(compositeId);
			}
		}
		return units.Concat(entries.Where(entry => entry.AssetKey.TypeId == CompositeUnitTypeId && compositeIds.Contains(entry.AssetKey.FileId))).ToArray();
	}

	private async ValueTask VerifyAsync(string tocPath, IReadOnlySet<AssetKey> expectedUnits, CancellationToken cancellationToken)
	{
		var entries = await scanner.ScanEntriesAsync(tocPath, cancellationToken).ConfigureAwait(false);
		var actualUnits = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).Select(entry => entry.AssetKey).ToHashSet();
		if (!actualUnits.SetEquals(expectedUnits)) throw new InvalidDataException("输出 Unit 集合与批准的物理目标集合不一致。");
		if (entries.GroupBy(entry => entry.AssetKey).Any(group => group.Count() != 1)) throw new InvalidDataException("输出包含重复 AssetKey。");
	}
}

public sealed record CrossArmorTargetShellPatchOperationRequest(
	string SourcePatchTocPath,
	string OutputDirectory,
	byte[] HeaderTemplateTocData,
	IReadOnlyList<SdkStyleTargetShellPatchWorkItem> WorkItems,
	IReadOnlyCollection<PatchArchiveAdditionalEntry> MaterialDependencies,
	bool IncludeResolvedMaterialDependencies,
	IReadOnlySet<ulong>? AllowedSourceMaterialIds,
	IReadOnlyList<PatchTocEntry>? PreparedSourceEntries = null,
	IReadOnlyList<uint>? CanonicalBoneHashOrder = null,
	ICurrentGameStreamLayoutRegistry? StreamLayoutRegistry = null)
{
	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(SourcePatchTocPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(OutputDirectory);
		ArgumentNullException.ThrowIfNull(HeaderTemplateTocData);
		ArgumentNullException.ThrowIfNull(WorkItems);
		ArgumentNullException.ThrowIfNull(MaterialDependencies);
		if (WorkItems.Count == 0) throw new InvalidDataException("At least one approved target Unit work item is required.");
	}
}

public sealed record CrossArmorTargetShellPatchOperationResult(
	PatchArchiveFileWriteResult WriteResult,
	SdkStyleTargetShellPatchOutput Output,
	IReadOnlyList<PatchTocEntry> SourceEntries,
	IReadOnlyList<PatchTocEntry> RemovedEntries);

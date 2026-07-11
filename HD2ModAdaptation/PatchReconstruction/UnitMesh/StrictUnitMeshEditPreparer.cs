namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Serializes one explicit strict Unit mesh transfer into a patch reconstruction edit.
public sealed class StrictUnitMeshEditPreparer
{
	private readonly StrictUnitMeshTransfer transfer;
	private readonly UnitMeshWriter writer;
	private readonly PatchUnitMeshReader unitReader;

	public StrictUnitMeshEditPreparer(StrictUnitMeshTransfer? transfer = null, UnitMeshWriter? writer = null, PatchUnitMeshReader? unitReader = null)
	{
		this.transfer = transfer ?? new StrictUnitMeshTransfer();
		this.writer = writer ?? new UnitMeshWriter();
		this.unitReader = unitReader ?? new PatchUnitMeshReader();
	}

	public async ValueTask<PatchUnitMeshEditResult> PrepareAsync(
		PatchTocEntry targetEntry,
		int targetMeshInfoIndex,
		PatchTocEntry sourceEntry,
		int sourceMeshInfoIndex,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(targetEntry);
		ArgumentNullException.ThrowIfNull(sourceEntry);
		var target = await unitReader.ReadAsync(targetEntry, cancellationToken).ConfigureAwait(false);
		var source = await unitReader.ReadAsync(sourceEntry, cancellationToken).ConfigureAwait(false);
		return Prepare(target, targetMeshInfoIndex, source, sourceMeshInfoIndex);
	}

	public PatchUnitMeshEditResult Prepare(PatchUnitMesh target, int targetMeshInfoIndex, PatchUnitMesh source, int sourceMeshInfoIndex)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(source);
		return Prepare(target.Entry, target.Payload, target.Model, target.CompositePayload, targetMeshInfoIndex, source.Model, sourceMeshInfoIndex);
	}

	public PatchUnitMeshEditResult Prepare(
		PatchUnitMesh outputPatchUnit,
		GameDataUnitMesh targetShell,
		int targetMeshInfoIndex,
		PatchUnitMesh source,
		int sourceMeshInfoIndex)
	{
		ArgumentNullException.ThrowIfNull(outputPatchUnit);
		ArgumentNullException.ThrowIfNull(targetShell);
		ArgumentNullException.ThrowIfNull(source);
		if (outputPatchUnit.Entry.AssetKey != targetShell.AssetKey)
		{
			throw new InvalidDataException($"Output patch Unit 0x{outputPatchUnit.Entry.AssetKey.FileId:x16} does not match target shell 0x{targetShell.AssetKey.FileId:x16}.");
		}
		EnsureReferencedTopologyMatches(outputPatchUnit.Payload.TocData, targetShell.Payload.TocData);

		return Prepare(outputPatchUnit.Entry, outputPatchUnit.Payload, targetShell.Model, targetShell.CompositePayload, targetMeshInfoIndex, source.Model, sourceMeshInfoIndex);
	}

	private static void EnsureReferencedTopologyMatches(ReadOnlySpan<byte> outputPatchTocData, ReadOnlySpan<byte> targetShellTocData)
	{
		const int boneReferenceOffset = 8;
		const int compositeReferenceOffset = 16;
		const int referenceSize = sizeof(ulong);
		if (outputPatchTocData.Length < compositeReferenceOffset + referenceSize || targetShellTocData.Length < compositeReferenceOffset + referenceSize)
		{
			throw new InvalidDataException("Output patch Unit and target shell must both contain Unit reference fields.");
		}

		var outputBoneReference = BitConverter.ToUInt64(outputPatchTocData.Slice(boneReferenceOffset, referenceSize));
		var targetBoneReference = BitConverter.ToUInt64(targetShellTocData.Slice(boneReferenceOffset, referenceSize));
		var outputCompositeReference = BitConverter.ToUInt64(outputPatchTocData.Slice(compositeReferenceOffset, referenceSize));
		var targetCompositeReference = BitConverter.ToUInt64(targetShellTocData.Slice(compositeReferenceOffset, referenceSize));
		if (outputBoneReference != targetBoneReference || outputCompositeReference != targetCompositeReference)
		{
			throw new InvalidDataException("Output patch Unit reference topology differs from the explicit target shell; rebuild requires matching Bone and Composite references.");
		}
	}

	private PatchUnitMeshEditResult Prepare(
		PatchTocEntry outputEntry,
		PatchEntryPayload outputOriginalPayload,
		UnitMeshModel targetModel,
		PatchEntryPayload? targetCompositePayload,
		int targetMeshInfoIndex,
		UnitMeshModel sourceModel,
		int sourceMeshInfoIndex)
	{
		var transferResult = transfer.Transfer(targetModel, targetMeshInfoIndex, sourceModel, sourceMeshInfoIndex);
		var writeResult = targetCompositePayload is null
			? writer.Write(transferResult.Model, outputOriginalPayload.TocData)
			: writer.Write(transferResult.Model, outputOriginalPayload.TocData, targetCompositePayload.TocData);
		return new PatchUnitMeshEditResult(
			outputEntry,
			outputOriginalPayload,
			writeResult.TocData,
			targetCompositePayload is null ? writeResult.GpuData : Array.Empty<byte>(),
			targetCompositePayload?.Entry.AssetKey,
			writeResult.CompositeTocData,
			targetCompositePayload is null ? null : writeResult.CompositeGpuData,
			ReplacementMaterialIds: transferResult.ReplacementMaterialIds);
	}
}
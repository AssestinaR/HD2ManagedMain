namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Converts a validated SDK-style reconstruction plan into patch entries for the first executable smoke loop.
public sealed class SdkStyleUnitOutputBuilder
{
	private readonly StrictUnitMeshTransfer transfer;
	private readonly UnitMeshWriter writer;

	public SdkStyleUnitOutputBuilder(StrictUnitMeshTransfer? transfer = null, UnitMeshWriter? writer = null)
	{
		this.transfer = transfer ?? new StrictUnitMeshTransfer(allowTargetLayoutConversion: true);
		this.writer = writer ?? new UnitMeshWriter();
	}

	public SdkStyleUnitOutput Build(SdkStyleUnitReconstructionPlan plan, byte[]? targetArchiveTocTemplate = null)
	{
		ArgumentNullException.ThrowIfNull(plan);
		if (plan.MeshBindings.Count == 0)
		{
			throw new InvalidDataException("At least one SDK-style mesh binding is required.");
		}

		var model = plan.TargetShell.Model;
		var replacementMaterialIds = new List<ulong>();
		foreach (var binding in plan.MeshBindings)
		{
			var transferResult = transfer.Transfer(model, binding.TargetMeshInfoIndex, binding.SourceUnit.Model, binding.SourceMeshInfoIndex);
			model = transferResult.Model;
			replacementMaterialIds.AddRange(transferResult.ReplacementMaterialIds);
		}

		var writeResult = plan.TargetShell.CompositePayload is null
			? writer.Write(model, plan.TargetShell.Payload.TocData)
			: writer.Write(model, plan.TargetShell.Payload.TocData, plan.TargetShell.CompositePayload.TocData);
		var additions = new List<PatchArchiveAdditionalEntry>
		{
			CreateAdditionalEntry(plan.TargetShell.Payload.Entry, writeResult.TocData, Array.Empty<byte>(), writeResult.GpuData)
		};

		if (plan.DependencyPolicy == TargetShellDependencyPolicy.EmbedReferencedComposite && writeResult.CompositeTocData is not null && plan.TargetShell.CompositePayload is not null)
		{
			additions.Add(CreateAdditionalEntry(plan.TargetShell.CompositePayload.Entry, writeResult.CompositeTocData, Array.Empty<byte>(), writeResult.CompositeGpuData ?? Array.Empty<byte>()));
		}
		else if (plan.DependencyPolicy == TargetShellDependencyPolicy.EmbedReferencedComposite && plan.TargetShell.CompositePayload is null)
		{
			throw new InvalidDataException("The requested embedded Composite output is unavailable from the selected target shell.");
		}

		var replacedSourceUnitKeys = plan.MeshBindings
			.Select(binding => binding.SourceUnit.Entry.AssetKey)
			.Distinct()
			.ToArray();
		return new SdkStyleUnitOutput(
			plan.Resources.TargetUnitAssetKey,
			plan.Resources.AvatarUnitAssetKey,
			additions,
			replacedSourceUnitKeys,
			replacementMaterialIds.Distinct().OrderBy(id => id).ToArray(),
			targetArchiveTocTemplate);
	}

	private static PatchArchiveAdditionalEntry CreateAdditionalEntry(PatchTocEntry entry, byte[] tocData, byte[] streamData, byte[] gpuData)
		=> new(entry.AssetKey, tocData, streamData, gpuData, entry.Unknown1, entry.Unknown2, entry.Unknown3, entry.Unknown4);
}

public sealed record SdkStyleUnitOutput(
	AssetKey TargetUnitAssetKey,
	AssetKey AvatarUnitAssetKey,
	IReadOnlyCollection<PatchArchiveAdditionalEntry> AdditionalEntries,
	IReadOnlyCollection<AssetKey> ReplacedSourceUnitAssetKeys,
	IReadOnlyCollection<ulong> ReplacementMaterialIds,
	byte[]? HeaderTemplateTocData = null)
{
	public IReadOnlyCollection<AssetKey> TargetUnitAssetKeys { get; init; } = [TargetUnitAssetKey];
}
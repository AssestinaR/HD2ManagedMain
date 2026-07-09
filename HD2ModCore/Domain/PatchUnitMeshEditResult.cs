namespace HD2ModCore.Domain;

// 作用：描述 patch-level Unit mesh dry-run 编辑产生的待写回 payload 与大小变化。
// Purpose: Describes rewritten payloads and size changes produced by a patch-level Unit mesh dry-run edit.
public sealed record PatchUnitMeshEditResult(
	PatchTocEntry Entry,
	PatchEntryPayload OriginalPayload,
	UnitMeshModel OriginalModel,
	UnitMeshModel EditedModel,
	byte[] TocData,
	byte[] GpuResourceData)
{
	public int TocDataSizeDelta => TocData.Length - OriginalPayload.TocData.Length;

	public int GpuResourceSizeDelta => GpuResourceData.Length - OriginalPayload.GpuResourceData.Length;
}

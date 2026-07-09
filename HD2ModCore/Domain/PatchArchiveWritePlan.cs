namespace HD2ModCore.Domain;

// 作用：描述 patch archive dry-run 重建结果，包括新 TOC/sidecar bytes 与更新后的 entry 元数据。
// Purpose: Describes a patch archive dry-run rebuild result, including new TOC/sidecar bytes and updated entry metadata.
public sealed record PatchArchiveWritePlan(
	string SourcePatchFilePath,
	byte[] TocFileData,
	byte[] StreamFileData,
	byte[] GpuResourceFileData,
	IReadOnlyList<PatchTocEntry> Entries,
	IReadOnlyList<PatchArchiveEditPlacement> EditedPlacements);

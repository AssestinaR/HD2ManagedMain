namespace HD2ModCore.Domain;

// 作用：部署计划中的单个文件操作，记录源文件、目标文件、hex 分组与部署编号。
// Purpose: A single file operation in an apply plan, recording source, target, archive group and target patch index.
public sealed record ApplyOperation(
	ApplyOperationKind Kind,
	string TargetPath,
	string? SourcePath,
	string? ArchiveHex16,
	int? SourcePatchIndex,
	int? TargetPatchIndex,
	PatchSidecarKind? SidecarKind,
	ModNodeId? NodeId);
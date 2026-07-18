namespace HD2ModCore.Domain;

// 作用：描述一次 source Unit mesh 到原版 target Unit 模板的适配请求。
// Purpose: Describes one adaptation request from a source Unit mesh to a vanilla target Unit template.
public sealed record UnitMeshAdaptationIntent(
	PatchTocEntry SourceEntry,
	string TargetArchiveId,
	int? SourceMeshInfoIndex);

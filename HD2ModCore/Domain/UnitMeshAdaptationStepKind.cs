namespace HD2ModCore.Domain;

// 作用：标识自动适配 dry-run 中 mesh slot 的处理方式。
// Purpose: Identifies how a mesh slot is handled during adaptation dry-runs.
public enum UnitMeshAdaptationStepKind
{
	MinifyTarget,
	ReplaceWithSource,
}

namespace HD2ModCore.Domain;

// 作用：区分源 Patch、Overwrite 和最终有效视图，避免不同内容视图互相污染缓存。
// Purpose: Separates source Patch, Overwrite, and effective views so their caches cannot collide.
public enum ModInformationContentView
{
	Source,
	Overwrite,
	Effective,
}

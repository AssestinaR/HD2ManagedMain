namespace HD2ModCore.Domain;

// 作用：约束一次信息读取允许保留结果的最长生命周期。
// Purpose: Limits the longest lifetime for which one information read may retain its result.
public enum ModInformationCacheScope
{
	// 只在本次请求中使用，读取完成后不进入任何共享缓存。
	None = 0,
	// 只允许当前操作复用；适合完整 Unit、GPU/Stream 等大对象。
	Operation = 1,
	// 允许当前应用会话内复用，但不写入磁盘。
	Session = 2,
	// 允许写入持久化信息缓存。
	Persistent = 3,
}

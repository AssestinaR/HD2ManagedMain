namespace HD2ModCore.Domain;

// 作用：为一次读取请求携带操作/会话身份和缓存生命周期策略。
// Purpose: Carries operation/session identity and cache lifetime policy for one read request.
public sealed record ModInformationRequestContext(
	Guid OperationId,
	Guid SessionId,
	ModInformationCacheScope CacheScope = ModInformationCacheScope.Persistent,
	string? OperationName = null,
	long? MemoryBudgetBytes = null)
{
	public static ModInformationRequestContext Create(
		ModInformationCacheScope cacheScope = ModInformationCacheScope.Persistent,
		Guid? operationId = null,
		Guid? sessionId = null,
		string? operationName = null,
		long? memoryBudgetBytes = null)
		=> new(operationId ?? Guid.NewGuid(), sessionId ?? Guid.NewGuid(), cacheScope, operationName, memoryBudgetBytes);

	public bool AllowsMemoryCache => CacheScope >= ModInformationCacheScope.Operation;
	public bool AllowsSessionCache => CacheScope >= ModInformationCacheScope.Session;
	public bool AllowsPersistentCache => CacheScope >= ModInformationCacheScope.Persistent;

	public void Validate()
	{
		if (OperationId == Guid.Empty) throw new ArgumentException("操作 ID 不能为 Guid.Empty。", nameof(OperationId));
		if (SessionId == Guid.Empty) throw new ArgumentException("会话 ID 不能为 Guid.Empty。", nameof(SessionId));
		if (MemoryBudgetBytes is < 0) throw new ArgumentOutOfRangeException(nameof(MemoryBudgetBytes));
	}
}

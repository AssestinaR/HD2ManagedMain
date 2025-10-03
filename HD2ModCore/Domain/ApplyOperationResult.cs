namespace HD2ModCore.Domain;

// 作用：单个部署操作的执行结果。
// Purpose: Execution result for a single apply operation.
public sealed record ApplyOperationResult(
	ApplyOperation Operation,
	bool Success,
	DeploymentMethod? Method,
	string? ErrorCode,
	string? Message);
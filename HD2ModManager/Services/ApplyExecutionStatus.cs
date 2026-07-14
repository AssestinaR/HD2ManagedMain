using HD2ModCore.Domain;

namespace HD2ModManager.Services;

// Purpose: Manager-facing projection of a completed coordinated deployment or deactivation result.
public sealed record ApplyExecutionStatus(
	bool Success,
	string Message,
	ApplyResult? CoreResult);

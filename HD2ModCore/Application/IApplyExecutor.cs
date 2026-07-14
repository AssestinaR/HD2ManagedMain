using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：执行 ApplyPlan（强制清空旧 patch，按硬链接/软链接/复制顺序部署并验证）。
// Purpose: Executes an ApplyPlan (clears old patches, deploys via hardlink/symlink/copy fallback and verifies).
public interface IApplyExecutor
{
	ValueTask<ApplyResult> ExecuteAsync(ApplyPlan plan, CancellationToken cancellationToken = default);
	ValueTask<ApplyResult> DeactivateAsync(string gameDataDirectory, CancellationToken cancellationToken = default);
}

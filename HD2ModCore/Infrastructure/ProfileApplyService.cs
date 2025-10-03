using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：串联真实索引、计划生成和部署执行，提供 Profile 应用的一站式入口。
// Purpose: Connects real indexing, planning and execution as a one-stop profile apply entry point.
public sealed class ProfileApplyService : IProfileApplyService
{
	private readonly IPatchFileIndexBuilder _indexBuilder;
	private readonly IApplyPlanner _planner;
	private readonly IApplyExecutor _executor;

	public ProfileApplyService(IPatchFileIndexBuilder indexBuilder, IApplyPlanner planner, IApplyExecutor executor)
	{
		_indexBuilder = indexBuilder ?? throw new ArgumentNullException(nameof(indexBuilder));
		_planner = planner ?? throw new ArgumentNullException(nameof(planner));
		_executor = executor ?? throw new ArgumentNullException(nameof(executor));
	}

	public async ValueTask<ApplyResult> ApplyAsync(
		Profile profile,
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		string gameDataDirectory,
		CancellationToken cancellationToken = default)
	{
		var index = await _indexBuilder.BuildAsync(snapshot, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		var plan = await _planner.BuildPlanAsync(profile, snapshot, index, gameDataDirectory, cancellationToken).ConfigureAwait(false);
		return await _executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
	}
}
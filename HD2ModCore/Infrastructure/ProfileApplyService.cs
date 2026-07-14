using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：串联真实索引、计划生成和部署执行，提供 Profile 应用的一站式入口。
// Purpose: Connects real indexing, planning and execution as a one-stop profile apply entry point.
public sealed class ProfileApplyService : IProfileApplyService
{
	private readonly IModContentFactsService _contentFactsService;
	private readonly IApplyPlanner _planner;
	private readonly IApplyExecutor _executor;
	private readonly DeploymentCapabilityService _capabilityService;

	public ProfileApplyService(IModContentFactsService contentFactsService, IApplyPlanner planner, IApplyExecutor executor, DeploymentCapabilityService? capabilityService = null)
	{
		_contentFactsService = contentFactsService ?? throw new ArgumentNullException(nameof(contentFactsService));
		_planner = planner ?? throw new ArgumentNullException(nameof(planner));
		_executor = executor ?? throw new ArgumentNullException(nameof(executor));
		_capabilityService = capabilityService ?? new DeploymentCapabilityService();
	}

	public async ValueTask<ApplyResult> ApplyAsync(
		Profile profile,
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		string gameDataDirectory,
		CancellationToken cancellationToken = default)
	{
		var capability = _capabilityService.Probe(modsRootDirectory, gameDataDirectory);
		if (!capability.IsAvailable || capability.Method is null)
		{
			return new ApplyResult(false, [], null, [new CoreIssue(CoreIssueSeverity.Error, "DeploymentUnavailable", capability.Error ?? capability.Summary, gameDataDirectory)]);
		}
		var facts = await _contentFactsService.GetLibraryFactsAsync(snapshot, modsRootDirectory, null, cancellationToken).ConfigureAwait(false);
		var index = new PatchFileIndex(
			DateTimeOffset.UtcNow,
			facts.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<IndexedPatchFile>)pair.Value.ToPatchFileIndex()),
			facts.Values.SelectMany(value => value.Issues).ToList());
		var plan = await _planner.BuildPlanAsync(profile, snapshot, index, gameDataDirectory, cancellationToken).ConfigureAwait(false);
		plan = plan with { DeploymentMethod = capability.Method.Value };
		return await _executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
	}
}
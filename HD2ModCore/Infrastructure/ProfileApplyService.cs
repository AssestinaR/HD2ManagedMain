using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：仅基于 JSON 与 Mod 文件事实串联计划生成和部署执行，不触发内容分析。
// Purpose: Applies a profile from JSON and filesystem facts without content analysis.
public sealed class ProfileApplyService : IProfileApplyService
{
	private readonly IModInformationCenter _informationCenter;
	private readonly IApplyPlanner _planner;
	private readonly IApplyExecutor _executor;
	private readonly DeploymentCapabilityService _capabilityService;

	public ProfileApplyService(IModInformationCenter informationCenter, IApplyPlanner planner, IApplyExecutor executor, DeploymentCapabilityService? capabilityService = null)
	{
		_informationCenter = informationCenter ?? throw new ArgumentNullException(nameof(informationCenter));
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
		var fileFactsResult = await _informationCenter.RequestFileFactsAsync(
			snapshot,
			modsRootDirectory,
			new ModInformationRequest(ModInformationKind.FileFacts, "Deployment", RequireFresh: true),
			cancellationToken).ConfigureAwait(false);
		if (fileFactsResult.Data is null)
			return new ApplyResult(false, [], null, fileFactsResult.Issues);
		var allFacts = fileFactsResult.Data;
		var activeNodeIds = profile.Entries.Select(entry => entry.NodeId).ToHashSet();
		var index = new PatchFileIndex(
			allFacts.BuiltUtc,
			allFacts.FilesByNode.Where(pair => activeNodeIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value),
			allFacts.Issues.Where(issue => issue.NodeId is null || activeNodeIds.Contains(issue.NodeId.Value)).ToList());
		var plan = await _planner.BuildPlanAsync(profile, snapshot, index, gameDataDirectory, cancellationToken).ConfigureAwait(false);
		plan = plan with { DeploymentMethod = capability.Method.Value };
		return await _executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
	}
}
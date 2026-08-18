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
	private readonly OptionActivationStore _optionActivations;

	public ProfileApplyService(IModInformationCenter informationCenter, IApplyPlanner planner, IApplyExecutor executor, DeploymentCapabilityService? capabilityService = null, StoragePaths? paths = null)
	{
		_informationCenter = informationCenter ?? throw new ArgumentNullException(nameof(informationCenter));
		_planner = planner ?? throw new ArgumentNullException(nameof(planner));
		_executor = executor ?? throw new ArgumentNullException(nameof(executor));
		_capabilityService = capabilityService ?? new DeploymentCapabilityService();
		_optionActivations = new OptionActivationStore(Path.Combine((paths ?? new StoragePaths(AppContext.BaseDirectory)).DataDirectory, "option-activations.json"));
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
			new ModInformationRequest(ModInformationKind.FileFacts, "Deployment"),
			cancellationToken).ConfigureAwait(false);
		if (fileFactsResult.Data is null)
			return new ApplyResult(false, [], null, fileFactsResult.Issues);
		var allFacts = fileFactsResult.Data;
		var activeNodeIds = profile.Entries.Select(entry => entry.NodeId).ToHashSet();
		var optionEntries = snapshot.Nodes.Values
			.Where(node => node.Metadata.Kind == ModNodeKind.Option)
			.Where(node => node.Metadata.HostModGuids?.Any(host => snapshot.Nodes.Values.Any(candidate => activeNodeIds.Contains(candidate.Id) && SameGuid(host, candidate.Id.Value.ToString("N")))) == true)
			.Where(node => _optionActivations.GetEnabledHosts(node.Id.Value.ToString("N")).Any(host => activeNodeIds.Any(id => SameGuid(host, id.Value.ToString("N")))))
			.OrderBy(node => node.Metadata.Name, StringComparer.OrdinalIgnoreCase)
			.Select((node, index) => new ProfileEntry(node.Id, profile.Entries.Count + index))
			.ToArray();
		var effectiveProfile = optionEntries.Length == 0 ? profile : profile with { Entries = profile.Entries.Concat(optionEntries).ToArray() };
		activeNodeIds.UnionWith(optionEntries.Select(entry => entry.NodeId));
		var index = new PatchFileIndex(
			allFacts.BuiltUtc,
			allFacts.FilesByNode.Where(pair => activeNodeIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value),
			allFacts.Issues.Where(issue => issue.NodeId is null || activeNodeIds.Contains(issue.NodeId.Value)).ToList());
		var plan = await _planner.BuildPlanAsync(effectiveProfile, snapshot, index, gameDataDirectory, cancellationToken).ConfigureAwait(false);
		plan = plan with { DeploymentMethod = capability.Method.Value };
		return await _executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
	}

	private static bool SameGuid(string? left, string? right)
		=> Guid.TryParse(left, out var leftGuid) && Guid.TryParse(right, out var rightGuid)
			? leftGuid == rightGuid
			: string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

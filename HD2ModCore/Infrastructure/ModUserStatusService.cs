using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Projects expected and actual override facts into simple Mod statuses without exposing technical identities.
public sealed class ModUserStatusService : IModUserStatusService
{
	private readonly IModContentFactsService _contentFactsService;
	private readonly IModInformationCenter? _informationCenter;
	private readonly IProfileOverrideGraphService _profileGraphService;
	private readonly IDeployedOverrideGraphService _deployedGraphService;

	public ModUserStatusService(IModContentFactsService contentFactsService, IProfileOverrideGraphService profileGraphService, IDeployedOverrideGraphService deployedGraphService)
		: this(contentFactsService, profileGraphService, deployedGraphService, null)
	{
	}

	public ModUserStatusService(IModContentFactsService contentFactsService, IProfileOverrideGraphService profileGraphService, IDeployedOverrideGraphService deployedGraphService, IModInformationCenter? informationCenter)
	{
		_contentFactsService = contentFactsService ?? throw new ArgumentNullException(nameof(contentFactsService));
		_profileGraphService = profileGraphService ?? throw new ArgumentNullException(nameof(profileGraphService));
		_deployedGraphService = deployedGraphService ?? throw new ArgumentNullException(nameof(deployedGraphService));
		_informationCenter = informationCenter;
	}

	public async ValueTask<IReadOnlyDictionary<ModNodeId, ModUserStatus>> GetStatusesAsync(LibrarySnapshot snapshot, ProfileId? selectedProfileId, string modsRootDirectory, string? gameDataDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		var active = snapshot.ActiveProfileId is { } activeId ? snapshot.Profiles.FirstOrDefault(profile => profile.Id == activeId) : null;
		var content = _informationCenter is null
			? await _contentFactsService.GetLibraryFactsAsync(snapshot, modsRootDirectory, null, cancellationToken).ConfigureAwait(false)
			: await GetAssetInventoryAsync(snapshot, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		ProfileOverrideGraph? expected = null;
		if (active is not null)
		{
			try { expected = await _profileGraphService.BuildAsync(active, snapshot, modsRootDirectory, cancellationToken).ConfigureAwait(false); }
			catch { }
		}
		DeployedOverrideGraph? actual = null;
		if (!string.IsNullOrWhiteSpace(gameDataDirectory))
		{
			try { actual = await _deployedGraphService.BuildAsync(gameDataDirectory, cancellationToken).ConfigureAwait(false); }
			catch { }
		}

		return ModUserStatusProjector.Project(snapshot, selectedProfileId, content, expected, null, actual);
	}

	private async ValueTask<IReadOnlyDictionary<ModNodeId, ModContentFacts>> GetAssetInventoryAsync(LibrarySnapshot snapshot, string modsRootDirectory, CancellationToken cancellationToken)
	{
		var result = new Dictionary<ModNodeId, ModContentFacts>();
		foreach (var node in snapshot.Nodes.Values)
		{
			var inventory = await _informationCenter!.RequestAssetInventoryAsync(node, modsRootDirectory, new ModInformationRequest(ModInformationKind.AssetInventory, "UserStatus"), cancellationToken).ConfigureAwait(false);
			if (inventory.Data is not null) result[node.Id] = inventory.Data;
		}
		return result;
	}
}

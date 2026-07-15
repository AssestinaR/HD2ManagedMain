using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Projects expected and actual override facts into simple Mod statuses without exposing technical identities.
public sealed class ModUserStatusService : IModUserStatusService
{
	private readonly IModContentFactsService _contentFactsService;
	private readonly IProfileOverrideGraphService _profileGraphService;
	private readonly IDeployedOverrideGraphService _deployedGraphService;

	public ModUserStatusService(IModContentFactsService contentFactsService, IProfileOverrideGraphService profileGraphService, IDeployedOverrideGraphService deployedGraphService)
	{
		_contentFactsService = contentFactsService ?? throw new ArgumentNullException(nameof(contentFactsService));
		_profileGraphService = profileGraphService ?? throw new ArgumentNullException(nameof(profileGraphService));
		_deployedGraphService = deployedGraphService ?? throw new ArgumentNullException(nameof(deployedGraphService));
	}

	public async ValueTask<IReadOnlyDictionary<ModNodeId, ModUserStatus>> GetStatusesAsync(LibrarySnapshot snapshot, ProfileId? selectedProfileId, string modsRootDirectory, string? gameDataDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		var active = snapshot.ActiveProfileId is { } activeId ? snapshot.Profiles.FirstOrDefault(profile => profile.Id == activeId) : null;
		var content = await _contentFactsService.GetLibraryFactsAsync(snapshot, modsRootDirectory, null, cancellationToken).ConfigureAwait(false);
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
}

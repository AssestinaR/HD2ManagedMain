using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Projects Core technical facts into concise player-facing Mod states.
public interface IModUserStatusService
{
	ValueTask<IReadOnlyDictionary<ModNodeId, ModUserStatus>> GetStatusesAsync(
		LibrarySnapshot snapshot,
		ProfileId? selectedProfileId,
		string modsRootDirectory,
		string? gameDataDirectory,
		CancellationToken cancellationToken = default);
}

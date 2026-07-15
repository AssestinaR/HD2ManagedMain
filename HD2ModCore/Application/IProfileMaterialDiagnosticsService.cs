using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Builds a winner-first Unit → Material → Texture diagnostic projection for a profile.
public interface IProfileMaterialDiagnosticsService
{
	ValueTask<ProfileMaterialDiagnostics> BuildAsync(
		Profile profile,
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}
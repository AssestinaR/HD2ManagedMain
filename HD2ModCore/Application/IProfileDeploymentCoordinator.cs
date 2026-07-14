using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Serializes immediate profile deployment, revision coalescing and deactivation.
public interface IProfileDeploymentCoordinator : IAsyncDisposable
{
	ProfileDeploymentStatus Status { get; }
	event EventHandler<ProfileDeploymentStatus>? StatusChanged;
	void NotifyActiveProfileChanged();
	Task DeactivateAsync(CancellationToken cancellationToken = default);
}

using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Atomically reads, publishes and removes the public activation-state contract.
public interface IActivationStateStore
{
	ValueTask<ActivationState?> TryLoadAsync(string gameDataDirectory, CancellationToken cancellationToken = default);
	ValueTask SaveAsync(string gameDataDirectory, ActivationState state, CancellationToken cancellationToken = default);
	ValueTask DeleteAsync(string gameDataDirectory, CancellationToken cancellationToken = default);
}

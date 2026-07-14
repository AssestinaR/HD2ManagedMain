namespace HD2ModCore.Application;

// Purpose: Supplies deterministic time and cancellable delay behavior for profile deployment buffering.
public interface IDeploymentDelay
{
	DateTimeOffset UtcNow { get; }
	Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

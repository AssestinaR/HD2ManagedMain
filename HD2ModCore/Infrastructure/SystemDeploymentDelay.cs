using HD2ModCore.Application;

namespace HD2ModCore.Infrastructure;

// Purpose: Uses system UTC time and cancellable Task.Delay for production deployment buffering.
public sealed class SystemDeploymentDelay : IDeploymentDelay
{
	public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
	public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

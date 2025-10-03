namespace HD2ModCore.Application;

public interface IGameDataLocator
{
	ValueTask<string?> TryGetGameDataDirectoryAsync(CancellationToken cancellationToken = default);
}

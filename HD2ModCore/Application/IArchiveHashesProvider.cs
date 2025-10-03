namespace HD2ModCore.Application;

public interface IArchiveHashesProvider
{
	ValueTask<string> GetArchiveHashesJsonAsync(CancellationToken cancellationToken = default);
}

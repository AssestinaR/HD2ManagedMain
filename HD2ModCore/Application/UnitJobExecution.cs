namespace HD2ModCore.Application;

public static class UnitJobExecutor
{
    public static async ValueTask<T[]> ExecuteAsync<T>(
        IReadOnlyList<(int Sequence, string UnitKey)> jobs,
        Func<int, CancellationToken, ValueTask<T>> worker,
        Func<int, T, ValueTask>? completed = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(worker);
        var results = new T[jobs.Count];
        for (var index = 0; index < jobs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await worker(index, cancellationToken).ConfigureAwait(false);
            results[index] = result;
            if (completed is not null)
            {
                await completed(index, result).ConfigureAwait(false);
                results[index] = default!;
            }
        }
        return results;
    }
}

namespace HD2ModManager.Services;

// Purpose: Delivers Core progress on its producer thread; the bridge decides whether UI work is needed.
public sealed class InlineProgress<T> : IProgress<T>
{
	private readonly Action<T> _report;

	public InlineProgress(Action<T> report) => _report = report ?? throw new ArgumentNullException(nameof(report));

	public void Report(T value) => _report(value);
}

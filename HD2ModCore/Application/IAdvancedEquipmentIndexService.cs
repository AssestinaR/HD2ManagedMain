using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Builds the optional, expensive equipment mesh facts used only by cross-armor transfer.
public interface IAdvancedEquipmentIndexService
{
	ValueTask<bool> IsCurrentAsync(CancellationToken cancellationToken = default);

	ValueTask BuildOrRefreshAsync(
		string gameDataDirectory,
		IProgress<IndexBuildProgress>? progress = null,
		CancellationToken cancellationToken = default);
}

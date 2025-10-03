using HD2ModCore.Domain;

namespace HD2ModCore.Application;

public interface IPatchTocScanner
{
	ValueTask<IReadOnlySet<AssetKey>> ScanAssetKeysAsync(string patchTocFilePath, CancellationToken cancellationToken = default);
}

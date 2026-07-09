using HD2ModCore.Domain;

namespace HD2ModCore.Application;

public interface IPatchTocScanner
{
	ValueTask<IReadOnlySet<AssetKey>> ScanAssetKeysAsync(string patchTocFilePath, CancellationToken cancellationToken = default);
	IReadOnlySet<AssetKey> ScanAssetKeys(ReadOnlySpan<byte> tocData, bool usesSlimEntryOffset = false);
	IReadOnlyList<PatchTocEntry> ScanEntries(ReadOnlySpan<byte> tocData, string sourceFilePath, bool usesSlimEntryOffset = false);
	ValueTask<IReadOnlyList<PatchTocEntry>> ScanEntriesAsync(string patchTocFilePath, CancellationToken cancellationToken = default);
}

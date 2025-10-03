using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：扫描目录中的 patch 文件状态，检查连续性、sidecar 与外部残留。
// Purpose: Scans patch file state in a directory, checking continuity, sidecars and foreign leftovers.
public interface IPatchStateScanner
{
	ValueTask<PatchStateReport> ScanAsync(
		string directoryPath,
		bool recursive = false,
		CancellationToken cancellationToken = default);
}
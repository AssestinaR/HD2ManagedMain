using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义 patch archive write plan 的安全落盘 API，只写入指定输出目录而不覆盖源文件。
// Purpose: Defines safe file output APIs for patch archive write plans, writing only to a chosen output directory without overwriting sources.
public interface IPatchArchiveFileWriter
{
	ValueTask<PatchArchiveFileWriteResult> WriteAsync(
		PatchArchiveWritePlan plan,
		string outputDirectoryPath,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default);
}

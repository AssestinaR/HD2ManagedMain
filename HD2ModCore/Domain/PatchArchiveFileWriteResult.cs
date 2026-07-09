namespace HD2ModCore.Domain;

// 作用：描述 patch archive write plan 安全落盘后的输出文件路径与写入尺寸。
// Purpose: Describes output file paths and written sizes after safely writing a patch archive plan to disk.
public sealed record PatchArchiveFileWriteResult(
	string OutputDirectoryPath,
	string TocFilePath,
	string StreamFilePath,
	string GpuResourceFilePath,
	long TocFileSize,
	long StreamFileSize,
	long GpuResourceFileSize);

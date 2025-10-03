using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：将对象树中的某个根节点导出为 zip（包含 manifest.json），保留原始文件名且按根节点名命名 zip。
// Purpose: Exports a root node from the object tree as a zip (with manifest.json), keeping original filenames and naming the zip after the root node.
public interface IModExporter
{
	ValueTask<string> ExportToZipAsync(
		ModNodeId rootNodeId,
		LibrarySnapshot snapshot,
		string destinationDirectory,
		CancellationToken cancellationToken = default);
}

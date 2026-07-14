namespace HD2ModCore.Domain.Manifest;

// 作用：manifest 中的节点条目，仅包含名称和备注等可移植元数据。
// Purpose: Node entry in the manifest containing portable name and notes metadata.
public sealed record ExportManifestNode(
	string RelativePath,
	string Name,
	string? Notes);

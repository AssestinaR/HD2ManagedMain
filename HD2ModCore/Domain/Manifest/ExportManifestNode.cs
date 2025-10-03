namespace HD2ModCore.Domain.Manifest;

// 作用：manifest 中的节点条目（自定义标签/备注等），不包含资产标签。
// Purpose: Node entry in manifest (user tags/notes, etc.); does not include derived asset tags.
public sealed record ExportManifestNode(
	string RelativePath,
	string Name,
	string? Notes,
    List<string> Tags);

namespace HD2ModCore.Domain.Manifest;

// 作用：导出用 manifest（用于跨管理器/跨机器携带），包含自定义标签但不包含资产标签。
// Purpose: Export manifest for portability/interoperability; includes user tags but not derived asset tags.
public sealed record ExportManifest(
	int Version,
	string RootName,
	DateTimeOffset ExportedUtc,
	IReadOnlyList<ExportManifestNode> Nodes);

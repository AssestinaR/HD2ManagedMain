using System.Text.Json.Serialization;

namespace HD2ModCore.Infrastructure;

// 作用：记录本地资产元数据缓存来源、更新时间与文件摘要。
// Purpose: Records local asset metadata cache source, update time, and file digests.
public sealed class AssetMetadataManifest
{
	public string Source { get; set; } = string.Empty;
	public DateTimeOffset UpdatedAtUtc { get; set; }
	public Dictionary<string, AssetMetadataFileManifest> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AssetMetadataFileManifest
{
	public long Bytes { get; set; }
	public string Sha256 { get; set; } = string.Empty;
}
namespace HD2ModCore.Domain;

// 作用：描述资产元数据同步结果，供 UI 展示状态与错误信息。
// Purpose: Describes asset metadata sync results for UI status and error reporting.
public sealed record AssetMetadataSyncResult(
	bool Success,
	DateTimeOffset? UpdatedAtUtc,
	string RepositoryRawBaseUrl,
	IReadOnlyList<string> UpdatedFiles,
	string? ErrorMessage)
{
	public static AssetMetadataSyncResult Failed(string repositoryRawBaseUrl, string errorMessage)
		=> new(false, null, repositoryRawBaseUrl, Array.Empty<string>(), errorMessage);
}
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：按需生成 Mod 图像缩略图事实，不读取 Patch 或触发资产分析。
// Purpose: Produces on-demand Mod thumbnail facts without reading Patch data or asset analysis.
public interface IModThumbnailProducer
{
	ValueTask<ModThumbnailFacts> ProduceAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
}

public sealed record ModThumbnailFacts(
	ModNodeId NodeId,
	string RelativePath,
	string Generation,
	DateTimeOffset BuiltUtc,
	string? SourcePath,
	long? SourceLength,
	DateTimeOffset? SourceLastWriteUtc,
	IReadOnlyList<CoreIssue> Issues);

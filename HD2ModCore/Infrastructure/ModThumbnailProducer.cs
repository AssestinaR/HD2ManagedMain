using System.Security.Cryptography;
using System.Text;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：从 Mod 目录约定图像生成轻量 Thumbnail 信息，不做图像解码或 Patch 分析。
// Purpose: Produces lightweight thumbnail facts from convention-based Mod images without decoding or Patch analysis.
public sealed class ModThumbnailProducer : IModThumbnailProducer
{
	public ValueTask<ModThumbnailFacts> ProduceAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(node);
		cancellationToken.ThrowIfCancellationRequested();
		var directory = Path.Combine(modsRootDirectory, node.RelativePath);
		var source = ModIconLocator.TryResolve(directory);
		FileInfo? info = source is null ? null : new FileInfo(source);
		var fingerprint = source is null ? "missing" : $"{Path.GetFileName(source)}:{info!.Length}:{info.LastWriteTimeUtc.Ticks}";
		var generation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.Id.Value.ToString("N") + fingerprint))).ToLowerInvariant();
		return ValueTask.FromResult(new ModThumbnailFacts(node.Id, node.RelativePath, generation, DateTimeOffset.UtcNow, source, info?.Length, info?.LastWriteTimeUtc, Array.Empty<CoreIssue>()));
	}
}

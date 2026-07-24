using System.Security.Cryptography;
using System.Text;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：将已有深度分析 Provider 封装为独立 ReferenceGraph 信息产品。
// Purpose: Wraps the existing deep analysis provider as an independent ReferenceGraph product.
public sealed class ReferenceGraphProducer : IReferenceGraphProducer
{
	private readonly IPatchGroupAnalysisProvider _provider;

	public ReferenceGraphProducer(IPatchGroupAnalysisProvider provider)
		=> _provider = provider ?? throw new ArgumentNullException(nameof(provider));

	public async ValueTask<ReferenceGraphFacts> ProduceAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var analyses = await _provider.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		var issues = analyses.SelectMany(analysis => analysis.Issues.Select(issue => new CoreIssue(CoreIssueSeverity.Warning, issue.Code, issue.Message, issue.SourceFilePath, node.Id))).ToArray();
		return new ReferenceGraphFacts(node.Id, node.RelativePath, ComputeGeneration(node, modsRootDirectory), DateTimeOffset.UtcNow, analyses, issues);
	}

	private static string ComputeGeneration(ModNode node, string root)
	{
		var builder = new StringBuilder(node.Id.Value.ToString("N"));
		var directory = Path.Combine(root, node.RelativePath);
		if (Directory.Exists(directory))
			foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
			{
				var file = new FileInfo(path);
				builder.Append('|').Append(file.Name).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks);
			}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}
}

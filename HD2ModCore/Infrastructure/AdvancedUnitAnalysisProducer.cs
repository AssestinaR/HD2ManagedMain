using System.Security.Cryptography;
using System.Text;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：隔离完整 Patch 结构分析，避免普通信息产品隐式触发高级解析。
// Purpose: Isolates full Patch analysis so ordinary information products never trigger it implicitly.
public sealed class AdvancedUnitAnalysisProducer : IAdvancedUnitAnalysisProducer
{
	private readonly IPatchGroupAnalysisProvider _analysisProvider;

	public AdvancedUnitAnalysisProducer(IPatchGroupAnalysisProvider analysisProvider)
	{
		_analysisProvider = analysisProvider ?? throw new ArgumentNullException(nameof(analysisProvider));
	}

	// 作用：高级分析通过统一读取器取得 Patch、Payload 与 Unit，避免自行创建底层 reader。
	// Purpose: Routes full Patch/Unit analysis through the unified reader instead of creating low-level readers.
	public AdvancedUnitAnalysisProducer(IModInformationReader informationReader)
		: this(new ModInformationPatchGroupAnalysisProvider(
			informationReader,
			depth: PatchAnalysisDepth.Full))
	{
	}

	public async ValueTask<AdvancedUnitAnalysisFacts> ProduceAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(node);
		var analyses = await _analysisProvider.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		var issues = analyses.SelectMany(analysis => analysis.Issues)
			.Select(issue => new CoreIssue(CoreIssueSeverity.Warning, issue.Code, issue.Message, issue.SourceFilePath, node.Id))
			.ToArray();
		return new AdvancedUnitAnalysisFacts(node.Id, node.RelativePath, ComputeGeneration(node, modsRootDirectory), DateTimeOffset.UtcNow, analyses, issues);
	}

	public static string ComputeGeneration(ModNode node, string root)
	{
		var directory = Path.Combine(root, node.RelativePath);
		var details = Directory.Exists(directory)
			? string.Join('|', Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(path =>
			{
				var file = new FileInfo(path);
				return $"{file.Name}:{file.Length}:{file.LastWriteTimeUtc.Ticks}";
			}))
			: string.Empty;
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.Id.Value.ToString("N") + details))).ToLowerInvariant();
	}
}

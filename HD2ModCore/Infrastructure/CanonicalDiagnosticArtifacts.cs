using System.Text;

namespace HD2ModCore.Infrastructure;

// Purpose: Owns workflow-local diagnostics so manager logging can remain a concise lifecycle summary.
public sealed record CanonicalMappingDiagnosticRow(
	string Part,
	string Result,
	string SourceId,
	string TargetId,
	string SourceSize,
	string TargetSize,
	string TargetBody,
	string SourceBody,
	string TargetArchive,
	string MatchMode,
	string Note);

public sealed class CanonicalDiagnosticArtifacts : IDisposable
{
	private readonly StreamWriter detailsWriter;
	private readonly object gate = new();
	private bool detailsClosed;

	public CanonicalDiagnosticArtifacts(string outputDirectory, string flow)
	{
		Directory.CreateDirectory(outputDirectory);
		Flow = flow;
		DetailsPath = Path.Combine(outputDirectory, "canonical-details.log");
		MappingsPath = Path.Combine(outputDirectory, "canonical-mappings.csv");
		TelemetryPath = Path.Combine(outputDirectory, "canonical-unit-job-telemetry.csv");
		ReportPath = Path.Combine(outputDirectory, "canonical-report.md");
		detailsWriter = new StreamWriter(new FileStream(DetailsPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
		Log($"[START] Flow={flow}");
	}

	public string Flow { get; }
	public string DetailsPath { get; }
	public string MappingsPath { get; }
	public string TelemetryPath { get; }
	public string ReportPath { get; }

	public void Log(string message)
	{
		lock (gate)
			if (!detailsClosed) detailsWriter.WriteLine($"[{DateTimeOffset.Now:O}] {message}");
	}

	public async ValueTask WriteMappingsAsync(IReadOnlyList<CanonicalMappingDiagnosticRow> rows, CancellationToken cancellationToken)
	{
		var builder = new StringBuilder();
		builder.AppendLine("流程,部位,结果,来源ID,目标ID,来源尺寸,目标尺寸,目标身形,来源身形,目标Archive,命中方式,备注");
		foreach (var row in rows)
			builder.AppendLine(string.Join(',', new[] { Flow, row.Part, row.Result, row.SourceId, row.TargetId, row.SourceSize, row.TargetSize, row.TargetBody, row.SourceBody, row.TargetArchive, row.MatchMode, row.Note }.Select(Escape)));
		await File.WriteAllTextAsync(MappingsPath, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask WriteReportAsync(string status, string summary, IReadOnlyList<string> errors, CancellationToken cancellationToken)
	{
		CloseDetails();
		var builder = new StringBuilder();
		builder.AppendLine("# Canonical 流程报告").AppendLine();
		builder.AppendLine($"- 流程：{Flow}");
		builder.AppendLine($"- 状态：{status}");
		builder.AppendLine($"- 摘要：{summary}").AppendLine();
		if (errors.Count != 0)
		{
			builder.AppendLine("## 错误摘要");
			foreach (var error in errors) builder.AppendLine($"- {error}");
			builder.AppendLine();
		}
		await AppendArtifactAsync(builder, "规划与结果", MappingsPath, "csv", cancellationToken).ConfigureAwait(false);
		await AppendArtifactAsync(builder, "Unit 作业遥测", TelemetryPath, "csv", cancellationToken).ConfigureAwait(false);
		await AppendArtifactAsync(builder, "详细日志", DetailsPath, "log", cancellationToken).ConfigureAwait(false);
		await File.WriteAllTextAsync(ReportPath, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
	}

	private static async ValueTask AppendArtifactAsync(StringBuilder builder, string title, string path, string language, CancellationToken cancellationToken)
	{
		builder.AppendLine($"## {title}").AppendLine().AppendLine($"```{language}");
		if (File.Exists(path)) builder.Append(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
		else builder.AppendLine("(未生成)");
		builder.AppendLine().AppendLine("```").AppendLine();
	}

	private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

	public void Dispose() => CloseDetails();

	private void CloseDetails()
	{
		lock (gate)
		{
			if (detailsClosed) return;
			detailsWriter.Dispose();
			detailsClosed = true;
		}
	}
}

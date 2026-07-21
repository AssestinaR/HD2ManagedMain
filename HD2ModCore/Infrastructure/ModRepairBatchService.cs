using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Commits only fully staged same-key repair candidates, with independent portable backups outside the Mod library.
public sealed class ModRepairBatchService : IModRepairBatchService
{
    private readonly IModSameKeyReconstructionService reconstruction;
    private readonly IAdvancedModAnalysisService advancedAnalysis;
    private readonly IPatchFileNameParser fileNameParser;
    private readonly StoragePaths paths;

    public ModRepairBatchService(StoragePaths paths, IModSameKeyReconstructionService reconstruction, IAdvancedModAnalysisService advancedAnalysis, IPatchFileNameParser fileNameParser)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.reconstruction = reconstruction ?? throw new ArgumentNullException(nameof(reconstruction));
        this.advancedAnalysis = advancedAnalysis ?? throw new ArgumentNullException(nameof(advancedAnalysis));
        this.fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
    }

    public async ValueTask<ModRepairBatchResult> RepairAsync(IReadOnlyList<ModNode> sourceNodes, string modsRootDirectory, string gameDataDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceNodes);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var batchDirectory = Path.Combine(paths.AppRootDirectory, "backups", stamp);
        var stagingRoot = Path.Combine(Directory.GetParent(Path.GetFullPath(modsRootDirectory))?.FullName ?? Path.GetFullPath(modsRootDirectory), ".hd2mod-repair-staging", stamp);
        Directory.CreateDirectory(batchDirectory);
        Directory.CreateDirectory(stagingRoot);
        var results = new List<ModRepairBatchModResult>();

        foreach (var source in sourceNodes.DistinctBy(node => node.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceDirectory = Path.Combine(modsRootDirectory, source.RelativePath);
            var stagingDirectory = Path.Combine(stagingRoot, source.Id.Value.ToString("N"));
            try
            {
                var analysisState = await advancedAnalysis.GetStateAsync(source, modsRootDirectory, cancellationToken).ConfigureAwait(false);
                if (!analysisState.IsReady)
                {
                    await advancedAnalysis.AnalyzeAsync(source, modsRootDirectory, cancellationToken).ConfigureAwait(false);
                }
                var state = await reconstruction.InspectAsync(source, modsRootDirectory, gameDataDirectory, cancellationToken).ConfigureAwait(false);
                if (!state.CanWrite)
                {
                    results.Add(new(source.Id, source.Metadata.Name, ModRepairBatchModStatus.SkippedNotRepairable, DescribeIssues(state.Issues), null, null, null));
                    continue;
                }

                var candidate = await reconstruction.GenerateCandidateAsync(source, modsRootDirectory, gameDataDirectory, stagingDirectory, cancellationToken).ConfigureAwait(false);
                if (!candidate.IsSuccessful || string.IsNullOrWhiteSpace(candidate.OutputDirectory) || !Directory.Exists(candidate.OutputDirectory))
                {
                    results.Add(new(source.Id, source.Metadata.Name, ModRepairBatchModStatus.CandidateFailed, DescribeIssues(candidate.Issues), candidate.OutputDirectory, null, candidate.ReportJsonPath));
                    continue;
                }

                var backupDirectory = Path.Combine(batchDirectory, source.RelativePath);
                CopyPatchFiles(sourceDirectory, backupDirectory, overwrite: false);
                try
                {
                    ReplacePatchFiles(sourceDirectory, candidate.OutputDirectory);
                    results.Add(new(source.Id, source.Metadata.Name, ModRepairBatchModStatus.Repaired, "候选已通过内部检查并完成提交。", candidate.OutputDirectory, backupDirectory, candidate.ReportJsonPath));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    try
                    {
                        RestorePatchFiles(sourceDirectory, backupDirectory);
                        results.Add(new(source.Id, source.Metadata.Name, ModRepairBatchModStatus.CommitFailed, $"提交失败，已恢复备份：{exception.Message}", candidate.OutputDirectory, backupDirectory, candidate.ReportJsonPath));
                    }
                    catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
                    {
                        results.Add(new(source.Id, source.Metadata.Name, ModRepairBatchModStatus.CommitFailed, $"提交失败且自动恢复失败：{exception.Message} / {restoreException.Message}", candidate.OutputDirectory, backupDirectory, candidate.ReportJsonPath));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
            {
                results.Add(new(source.Id, source.Metadata.Name, ModRepairBatchModStatus.CandidateFailed, exception.Message, null, null, null));
            }
        }

        var manifestPath = Path.Combine(batchDirectory, "backup.json");
        var result = new ModRepairBatchResult(batchDirectory, manifestPath, sourceNodes.DistinctBy(node => node.Id).Count(), results.Count(item => item.Status == ModRepairBatchModStatus.Repaired), results.Count(item => item.Status == ModRepairBatchModStatus.SkippedNotRepairable), results.Count(item => item.Status is ModRepairBatchModStatus.CandidateFailed or ModRepairBatchModStatus.CommitFailed), results);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
        return result;
    }

    private void CopyPatchFiles(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException($"Mod directory does not exist: {sourceDirectory}");
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourcePath in EnumeratePatchFiles(sourceDirectory))
        {
            File.Copy(sourcePath, Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)), overwrite);
        }
    }

    private void ReplacePatchFiles(string sourceDirectory, string candidateDirectory)
    {
        var candidates = EnumeratePatchFiles(candidateDirectory).ToArray();
        if (candidates.Length == 0) throw new InvalidDataException("重构候选不包含任何 Patch 或 sidecar 文件。");
        foreach (var sourcePath in EnumeratePatchFiles(sourceDirectory).ToArray()) File.Delete(sourcePath);
        foreach (var candidatePath in candidates) File.Copy(candidatePath, Path.Combine(sourceDirectory, Path.GetFileName(candidatePath)), overwrite: false);
    }

    private void RestorePatchFiles(string sourceDirectory, string backupDirectory)
    {
        foreach (var sourcePath in EnumeratePatchFiles(sourceDirectory).ToArray()) File.Delete(sourcePath);
        CopyPatchFiles(backupDirectory, sourceDirectory, overwrite: false);
    }

    private IEnumerable<string> EnumeratePatchFiles(string directory)
        => Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => fileNameParser.TryParse(Path.GetFileName(path), out _));

    private static string DescribeIssues(IReadOnlyList<CoreIssue> issues)
        => issues.Count == 0 ? "不满足安全重构条件。" : string.Join("；", issues.Take(3).Select(issue => issue.Message));
}

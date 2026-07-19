using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Preflights deployment sources, commits one complete patch set, verifies it and publishes activation state atomically.
public sealed class ApplyExecutor : IApplyExecutor
{
	private readonly IPatchStateScanner _stateScanner;
	private readonly IPatchFileNameParser _fileNameParser;
	private readonly IActivationStateStore _activationStateStore;

	public ApplyExecutor()
		: this(new PatchStateScanner(new PatchFileNameParser()), new PatchFileNameParser(), new JsonActivationStateStore())
	{
	}

	public ApplyExecutor(IPatchStateScanner stateScanner)
		: this(stateScanner, new PatchFileNameParser(), new JsonActivationStateStore())
	{
	}

	public ApplyExecutor(IPatchStateScanner stateScanner, IPatchFileNameParser fileNameParser, IActivationStateStore activationStateStore)
	{
		_stateScanner = stateScanner ?? throw new ArgumentNullException(nameof(stateScanner));
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		_activationStateStore = activationStateStore ?? throw new ArgumentNullException(nameof(activationStateStore));
	}

	public async ValueTask<ApplyResult> ExecuteAsync(ApplyPlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		Directory.CreateDirectory(plan.GameDataDirectory);
		var operationResults = new List<ApplyOperationResult>();
		var issues = new List<CoreIssue>(plan.Issues);
		var deployOperations = plan.Operations.Where(operation => operation.Kind == ApplyOperationKind.DeployPatch).ToList();
		var preflight = await PreflightAsync(deployOperations, cancellationToken).ConfigureAwait(false);
		issues.AddRange(preflight.Issues);
		if (preflight.Issues.Any(issue => issue.Severity == CoreIssueSeverity.Error))
		{
			return new ApplyResult(false, operationResults, await _stateScanner.ScanAsync(plan.GameDataDirectory, false, CancellationToken.None).ConfigureAwait(false), issues);
		}

		// Last cancellable checkpoint. Commit must finish or deterministically clean every controlled patch.
		cancellationToken.ThrowIfCancellationRequested();
		var deployed = new List<ActivationStateFileEntry>();
		var commitFailed = false;
		foreach (var operation in plan.Operations.Where(operation => operation.Kind == ApplyOperationKind.DeletePatch))
		{
			var result = DeletePatch(operation);
			operationResults.Add(result);
			if (!result.Success)
			{
				commitFailed = true;
				issues.Add(ToIssue(result, operation));
			}
		}

		if (!commitFailed)
		{
			foreach (var operation in deployOperations)
			{
				var result = DeployPatch(operation, plan.DeploymentMethod);
				operationResults.Add(result);
				if (!result.Success || result.Method is null)
				{
					commitFailed = true;
					issues.Add(ToIssue(result, operation));
					break;
				}

				var source = preflight.Sources[operation.SourcePath!];
				deployed.Add(new ActivationStateFileEntry(operation.TargetPath, operation.SourcePath!, result.Method.Value, operation.ArchiveHex16!, operation.SourcePatchIndex!.Value, operation.TargetPatchIndex!.Value, operation.SidecarKind!.Value, operation.NodeId, source.Length, source.ContentSha256));
			}
		}

		if (commitFailed)
		{
			CleanupControlledFiles(plan.GameDataDirectory);
			await DeleteActivationStateBestEffortAsync(plan.GameDataDirectory).ConfigureAwait(false);
			var failedReport = await _stateScanner.ScanAsync(plan.GameDataDirectory, false, CancellationToken.None).ConfigureAwait(false);
			return new ApplyResult(false, operationResults, failedReport, issues);
		}

		var report = await _stateScanner.ScanAsync(plan.GameDataDirectory, recursive: false, CancellationToken.None).ConfigureAwait(false);
		issues.AddRange(report.Issues);
		issues.AddRange(await VerifyDeployedFilesAsync(deployed).ConfigureAwait(false));
		if (issues.Any(issue => issue.Severity == CoreIssueSeverity.Error))
		{
			CleanupControlledFiles(plan.GameDataDirectory);
			await DeleteActivationStateBestEffortAsync(plan.GameDataDirectory).ConfigureAwait(false);
			return new ApplyResult(false, operationResults, report, issues);
		}

		var statePath = Path.Combine(plan.GameDataDirectory, JsonActivationStateStore.StateFileName);
		var stateOperation = new ApplyOperation(ApplyOperationKind.WriteState, statePath, null, null, null, null, null, null);
		try
		{
			var state = new ActivationState(JsonActivationStateStore.CurrentVersion, plan.ProfileId, plan.ProfileRevision, DateTimeOffset.UtcNow, true, deployed, issues);
			await _activationStateStore.SaveAsync(plan.GameDataDirectory, state, CancellationToken.None).ConfigureAwait(false);
			operationResults.Add(new ApplyOperationResult(stateOperation, true, DeploymentMethod.StateFile, null, null));
		}
		catch (Exception exception)
		{
			operationResults.Add(new ApplyOperationResult(stateOperation, false, DeploymentMethod.StateFile, "WriteStateFailed", exception.Message));
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, "WriteStateFailed", exception.Message, statePath, ExceptionMessage: exception.ToString()));
			CleanupControlledFiles(plan.GameDataDirectory);
			await DeleteActivationStateBestEffortAsync(plan.GameDataDirectory).ConfigureAwait(false);
			return new ApplyResult(false, operationResults, report, issues);
		}

		return new ApplyResult(true, operationResults, report, issues);
	}

	public async ValueTask<ApplyResult> DeactivateAsync(string gameDataDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(gameDataDirectory);
		Directory.CreateDirectory(gameDataDirectory);
		cancellationToken.ThrowIfCancellationRequested();
		var operations = new List<ApplyOperationResult>();
		var issues = new List<CoreIssue>();
		foreach (var path in EnumerateControlledFiles(gameDataDirectory).ToList())
		{
			var operation = new ApplyOperation(ApplyOperationKind.DeletePatch, path, null, null, null, null, null, null);
			var result = DeletePatch(operation);
			operations.Add(result);
			if (!result.Success) issues.Add(ToIssue(result, operation));
		}
		try { await _activationStateStore.DeleteAsync(gameDataDirectory, CancellationToken.None).ConfigureAwait(false); }
		catch (Exception exception) { issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeleteActivationStateFailed", exception.Message, Path.Combine(gameDataDirectory, JsonActivationStateStore.StateFileName), ExceptionMessage: exception.ToString())); }
		var report = await _stateScanner.ScanAsync(gameDataDirectory, false, CancellationToken.None).ConfigureAwait(false);
		issues.AddRange(report.Issues);
		return new ApplyResult(!issues.Any(issue => issue.Severity == CoreIssueSeverity.Error), operations, report, issues);
	}

	private async ValueTask<PreflightResult> PreflightAsync(IReadOnlyList<ApplyOperation> operations, CancellationToken cancellationToken)
	{
		var issues = new List<CoreIssue>();
		var sources = new Dictionary<string, SourceFileFact>(StringComparer.OrdinalIgnoreCase);
		foreach (var operation in operations)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(operation.SourcePath) || !File.Exists(operation.SourcePath))
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Error, "SourceFileMissing", $"Source file does not exist: {operation.SourcePath}", operation.SourcePath, operation.NodeId));
				continue;
			}
			if (operation.ArchiveHex16 is null || operation.SourcePatchIndex is null || operation.TargetPatchIndex is null || operation.SidecarKind is null)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployOperationIncomplete", "Deploy operation lacks patch identity fields.", operation.SourcePath, operation.NodeId));
				continue;
			}
			try
			{
				await using var stream = new FileStream(operation.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
				var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
				sources[operation.SourcePath] = new SourceFileFact(stream.Length, Convert.ToHexString(hash).ToLowerInvariant());
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Error, "SourceFileUnreadable", exception.Message, operation.SourcePath, operation.NodeId, exception.ToString()));
			}
		}
		return new PreflightResult(sources, issues);
	}

	private static async ValueTask<IReadOnlyList<CoreIssue>> VerifyDeployedFilesAsync(IReadOnlyList<ActivationStateFileEntry> files)
	{
		var issues = new List<CoreIssue>();
		foreach (var file in files)
		{
			if (!File.Exists(file.TargetPath)) { issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedTargetMissing", "Deployed target is missing.", file.TargetPath, file.NodeId)); continue; }
			if (file.Method == DeploymentMethod.SymbolicLink)
			{
				var resolved = File.ResolveLinkTarget(file.TargetPath, returnFinalTarget: true);
				if (resolved is null || !string.Equals(Path.GetFullPath(resolved.FullName), Path.GetFullPath(file.SourcePath), StringComparison.OrdinalIgnoreCase))
				{
					issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedLinkMismatch", "Symbolic link does not point to its source.", file.TargetPath, file.NodeId));
					continue;
				}
			}
			else if (new FileInfo(file.TargetPath).Length != file.Length)
			{
				issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedLengthMismatch", "Deployed target length differs from its source.", file.TargetPath, file.NodeId));
				continue;
			}
			await using var stream = new FileStream(file.TargetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
			var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, CancellationToken.None).ConfigureAwait(false)).ToLowerInvariant();
			if (!string.Equals(hash, file.ContentSha256, StringComparison.OrdinalIgnoreCase)) issues.Add(new CoreIssue(CoreIssueSeverity.Error, "DeployedContentMismatch", "Deployed target content differs from its source.", file.TargetPath, file.NodeId));
		}
		return issues;
	}

	private IEnumerable<string> EnumerateControlledFiles(string gameDataDirectory)
	{
		if (!Directory.Exists(gameDataDirectory)) yield break;
		foreach (var path in Directory.EnumerateFiles(gameDataDirectory, "*", SearchOption.TopDirectoryOnly))
		{
			if (_fileNameParser.TryParse(Path.GetFileName(path), out _)) yield return path;
		}
	}

	private void CleanupControlledFiles(string gameDataDirectory)
	{
		foreach (var path in EnumerateControlledFiles(gameDataDirectory).ToList())
		{
			try
			{
				ClearReadOnlyAttribute(path);
				File.Delete(path);
			}
			catch { }
		}
	}

	private async ValueTask DeleteActivationStateBestEffortAsync(string gameDataDirectory)
	{
		try { await _activationStateStore.DeleteAsync(gameDataDirectory, CancellationToken.None).ConfigureAwait(false); } catch { }
	}

	private static ApplyOperationResult DeletePatch(ApplyOperation operation)
	{
		try
		{
			if (File.Exists(operation.TargetPath))
			{
				ClearReadOnlyAttribute(operation.TargetPath);
				File.Delete(operation.TargetPath);
			}
			else if (Directory.Exists(operation.TargetPath))
			{
				Directory.Delete(operation.TargetPath);
			}
			return new ApplyOperationResult(operation, true, DeploymentMethod.Delete, null, null);
		}
		catch (Exception exception) { return new ApplyOperationResult(operation, false, DeploymentMethod.Delete, "CannotDeleteExistingPatch", exception.Message); }
	}

	private static ApplyOperationResult DeployPatch(ApplyOperation operation, DeploymentMethod method)
	{
		try
		{
			var targetDirectory = Path.GetDirectoryName(operation.TargetPath);
			if (!string.IsNullOrWhiteSpace(targetDirectory)) Directory.CreateDirectory(targetDirectory);
			if (File.Exists(operation.TargetPath))
			{
				ClearReadOnlyAttribute(operation.TargetPath);
				File.Delete(operation.TargetPath);
			}
			else if (Directory.Exists(operation.TargetPath))
			{
				Directory.Delete(operation.TargetPath);
			}
		}
		catch (Exception exception) { return new ApplyOperationResult(operation, false, null, "CannotPrepareTarget", exception.Message); }
		if (method == DeploymentMethod.HardLink)
		{
			return TryCreateHardLink(operation.TargetPath, operation.SourcePath!, out var error)
				? new ApplyOperationResult(operation, true, DeploymentMethod.HardLink, null, null)
				: new ApplyOperationResult(operation, false, null, "HardLinkFailed", error);
		}
		if (method == DeploymentMethod.SymbolicLink)
		{
			return TryCreateSymbolicLink(operation.TargetPath, operation.SourcePath!, out var error)
				? new ApplyOperationResult(operation, true, DeploymentMethod.SymbolicLink, null, null)
				: new ApplyOperationResult(operation, false, null, "SymbolicLinkFailed", error);
		}
		return new ApplyOperationResult(operation, false, null, "UnsupportedDeploymentMethod", "Only hard-link and symbolic-link deployment are supported.");
	}

	private static CoreIssue ToIssue(ApplyOperationResult result, ApplyOperation operation)
		=> new(CoreIssueSeverity.Error, result.ErrorCode ?? "DeployOperationFailed", result.Message ?? "Deployment operation failed.", operation.TargetPath, operation.NodeId);

	private static bool TryCreateSymbolicLink(string linkPath, string targetPath, out string? error)
	{
		try { File.CreateSymbolicLink(linkPath, targetPath); error = null; return true; }
		catch (Exception exception) { error = exception.Message; return false; }
	}

	private static void ClearReadOnlyAttribute(string path)
	{
		var attributes = File.GetAttributes(path);
		if ((attributes & FileAttributes.ReadOnly) != 0)
		{
			File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
		}
	}

	private static bool TryCreateHardLink(string linkPath, string targetPath, out string? error)
	{
		if (!OperatingSystem.IsWindows()) { error = "Hardlink P/Invoke is only implemented for Windows."; return false; }
		if (CreateHardLinkW(linkPath, targetPath, IntPtr.Zero)) { error = null; return true; }
		error = Marshal.GetLastWin32Error().ToString();
		return false;
	}

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

	private sealed record SourceFileFact(long Length, string ContentSha256);
	private sealed record PreflightResult(IReadOnlyDictionary<string, SourceFileFact> Sources, IReadOnlyList<CoreIssue> Issues);
}

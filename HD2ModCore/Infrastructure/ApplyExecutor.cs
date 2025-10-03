using HD2ModCore.Application;
using HD2ModCore.Domain;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HD2ModCore.Infrastructure;

// 作用：执行 ApplyPlan：清空所有 patch，按硬链接/软链接/复制 fallback 部署，并写入结构化状态文件。
// Purpose: Executes an ApplyPlan: clears all patches, deploys via hardlink/symlink/copy fallback and writes a structured state file.
public sealed class ApplyExecutor : IApplyExecutor
{
	private const string StateFileName = "activation-state.json";
	private readonly IPatchStateScanner _stateScanner;

	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	public ApplyExecutor()
		: this(new PatchStateScanner(new PatchFileNameParser()))
	{
	}

	public ApplyExecutor(IPatchStateScanner stateScanner)
	{
		_stateScanner = stateScanner ?? throw new ArgumentNullException(nameof(stateScanner));
	}

	public async ValueTask<ApplyResult> ExecuteAsync(ApplyPlan plan, CancellationToken cancellationToken = default)
	{
		if (plan is null)
		{
			throw new ArgumentNullException(nameof(plan));
		}

		Directory.CreateDirectory(plan.GameDataDirectory);
		var operationResults = new List<ApplyOperationResult>();
		var issues = new List<CoreIssue>(plan.Issues);
		var deployed = new List<ActivationStateFileEntry>();

		foreach (var op in plan.Operations)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (op.Kind == ApplyOperationKind.DeletePatch)
			{
				operationResults.Add(DeletePatch(op));
				continue;
			}

			if (op.Kind == ApplyOperationKind.DeployPatch)
			{
				var result = DeployPatch(op);
				operationResults.Add(result);
				if (result.Success && result.Method is not null)
				{
					deployed.Add(new ActivationStateFileEntry(
						TargetPath: op.TargetPath,
						SourcePath: op.SourcePath!,
						Method: result.Method.Value,
						ArchiveHex16: op.ArchiveHex16!,
						TargetPatchIndex: op.TargetPatchIndex!.Value,
						SidecarKind: op.SidecarKind!.Value,
						NodeId: op.NodeId));
				}
				else
				{
					issues.Add(new CoreIssue(CoreIssueSeverity.Error, result.ErrorCode ?? "DeployPatchFailed", result.Message ?? "Failed to deploy patch.", op.TargetPath, op.NodeId));
				}
				continue;
			}
		}

		var stateResult = await WriteStateAsync(plan, deployed, cancellationToken).ConfigureAwait(false);
		operationResults.Add(stateResult);
		if (!stateResult.Success)
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, stateResult.ErrorCode ?? "WriteStateFailed", stateResult.Message ?? "Failed to write activation state.", Path.Combine(plan.GameDataDirectory, StateFileName)));
		}

		var report = await _stateScanner.ScanAsync(plan.GameDataDirectory, recursive: false, cancellationToken).ConfigureAwait(false);
		issues.AddRange(report.Issues);
		var success = !issues.Any(i => i.Severity == CoreIssueSeverity.Error) && operationResults.All(r => r.Success);
		return new ApplyResult(success, operationResults, report, issues);
	}

	private static ApplyOperationResult DeletePatch(ApplyOperation op)
	{
		try
		{
			if (File.Exists(op.TargetPath) || Directory.Exists(op.TargetPath))
			{
				File.Delete(op.TargetPath);
			}
			return new ApplyOperationResult(op, true, DeploymentMethod.Delete, null, null);
		}
		catch (Exception ex)
		{
			return new ApplyOperationResult(op, false, DeploymentMethod.Delete, "CannotDeleteExistingPatch", ex.Message);
		}
	}

	private static ApplyOperationResult DeployPatch(ApplyOperation op)
	{
		if (string.IsNullOrWhiteSpace(op.SourcePath) || !File.Exists(op.SourcePath))
		{
			return new ApplyOperationResult(op, false, null, "SourceFileMissing", $"Source file does not exist: {op.SourcePath}");
		}

		try
		{
			var targetDir = Path.GetDirectoryName(op.TargetPath);
			if (!string.IsNullOrWhiteSpace(targetDir))
			{
				Directory.CreateDirectory(targetDir);
			}

			if (File.Exists(op.TargetPath) || Directory.Exists(op.TargetPath))
			{
				File.Delete(op.TargetPath);
			}
		}
		catch (Exception ex)
		{
			return new ApplyOperationResult(op, false, null, "CannotPrepareTarget", ex.Message);
		}

		if (TryCreateHardLink(op.TargetPath, op.SourcePath!, out var hardLinkError))
		{
			return new ApplyOperationResult(op, true, DeploymentMethod.HardLink, null, null);
		}

		if (TryCreateSymbolicLink(op.TargetPath, op.SourcePath!, out var symlinkError))
		{
			return new ApplyOperationResult(op, true, DeploymentMethod.SymbolicLink, null, null);
		}

		try
		{
			File.Copy(op.SourcePath!, op.TargetPath, overwrite: true);
			return new ApplyOperationResult(op, true, DeploymentMethod.Copy, null, null);
		}
		catch (Exception ex)
		{
			var message = $"HardLink failed: {hardLinkError}; Symlink failed: {symlinkError}; Copy failed: {ex.Message}";
			return new ApplyOperationResult(op, false, null, "AllDeploymentMethodsFailed", message);
		}
	}

	private static bool TryCreateSymbolicLink(string linkPath, string targetPath, out string? error)
	{
		try
		{
			File.CreateSymbolicLink(linkPath, targetPath);
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static bool TryCreateHardLink(string linkPath, string targetPath, out string? error)
	{
		if (!OperatingSystem.IsWindows())
		{
			error = "Hardlink P/Invoke is only implemented for Windows.";
			return false;
		}

		if (CreateHardLinkW(linkPath, targetPath, IntPtr.Zero))
		{
			error = null;
			return true;
		}

		error = Marshal.GetLastWin32Error().ToString();
		return false;
	}

	private async Task<ApplyOperationResult> WriteStateAsync(ApplyPlan plan, IReadOnlyList<ActivationStateFileEntry> files, CancellationToken cancellationToken)
	{
		var statePath = Path.Combine(plan.GameDataDirectory, StateFileName);
		var operation = new ApplyOperation(ApplyOperationKind.WriteState, statePath, null, null, null, null, null, null);
		try
		{
			var state = new ActivationStateFile(1, plan.ProfileId, DateTimeOffset.UtcNow, files);
			var json = JsonSerializer.Serialize(state, SerializerOptions);
			await File.WriteAllTextAsync(statePath, json, cancellationToken).ConfigureAwait(false);
			return new ApplyOperationResult(operation, true, DeploymentMethod.StateFile, null, null);
		}
		catch (Exception ex)
		{
			return new ApplyOperationResult(operation, false, DeploymentMethod.StateFile, "WriteStateFailed", ex.Message);
		}
	}

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

	private sealed record ActivationStateFile(
		int Version,
		ProfileId? ProfileId,
		DateTimeOffset AppliedUtc,
		IReadOnlyList<ActivationStateFileEntry> Files);

	private sealed record ActivationStateFileEntry(
		string TargetPath,
		string SourcePath,
		DeploymentMethod Method,
		string ArchiveHex16,
		int TargetPatchIndex,
		PatchSidecarKind SidecarKind,
		ModNodeId? NodeId);
}

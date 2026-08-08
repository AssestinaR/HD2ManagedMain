using HD2ModCore.Application;
using System.Threading;

namespace HD2ModManager.Services;

// Purpose: Adapts Core operation telemetry to Manager background-task state without coupling Core to WPF.
public interface IOperationProgressTarget
{
	void MarkStarted(string stage);
	void Update(string stage, double? progress);
	void MarkCompleted();
	void MarkFailed(string error);
	void MarkCanceled();
}

public sealed class BackgroundTaskOperationTarget : IOperationProgressTarget
{
	private readonly BackgroundTaskItem _task;

	public BackgroundTaskOperationTarget(BackgroundTaskItem task) => _task = task;
	public void MarkStarted(string stage) => _task.MarkRunning(stage);
	public void Update(string stage, double? progress)
	{
		_task.UpdateStage(stage);
		_task.UpdateProgress(progress);
	}
	public void MarkCompleted() => _task.MarkCompleted();
	public void MarkFailed(string error) => _task.MarkFailed(error);
	public void MarkCanceled() => _task.MarkCanceled();
}

// Purpose: Keeps high-frequency Core telemetry off the WPF dispatcher while retaining lifecycle state.
public sealed class OperationProgressBridge
{
	private readonly IOperationProgressTarget _target;
	private readonly SynchronizationContext _context;
	private readonly object _gate = new();
	private readonly Guid _operationId;
	private long _lastSequence = -1;
	private long _lastPostedSequence = -1;
	private string? _lastStageId;
	private DateTimeOffset _lastProgressPublishedAt;
	private string? _displayedStageId;
	private DateTimeOffset _stageStartedAt;
	private bool _terminal;
	private long _receivedProgressCount;
	private long _publishedStageUpdateCount;
	private long _suppressedProgressCount;
	private long _uiUpdateCount;

	public OperationProgressBridge(IOperationProgressTarget target, Guid expectedOperationId, SynchronizationContext context)
	{
		_target = target ?? throw new ArgumentNullException(nameof(target));
		if (expectedOperationId == Guid.Empty) throw new ArgumentException("Operation ID cannot be empty.", nameof(expectedOperationId));
		_context = context ?? throw new ArgumentNullException(nameof(context));
		_operationId = expectedOperationId;
	}

	public void Apply(OperationProgressEvent progress)
	{
		lock (_gate)
		{
			if (progress.OperationId != _operationId || progress.Sequence <= _lastSequence || _terminal) return;
			if (progress.State == OperationState.Progress)
			{
				_receivedProgressCount++;
				if (!ShouldPublishProgress(progress))
				{
					_suppressedProgressCount++;
					_lastSequence = progress.Sequence;
					return;
				}
				_publishedStageUpdateCount++;
			}

			_lastSequence = progress.Sequence;
			_terminal = progress.IsTerminal;
			var sequence = progress.Sequence;
			Post(() =>
			{
				lock (_gate)
				{
					if (sequence < _lastPostedSequence) return;
					_lastPostedSequence = sequence;
					ApplyOnContext(progress);
				}
			});
		}
	}

	private bool ShouldPublishProgress(OperationProgressEvent progress)
	{
		var stageId = progress.StageId ?? progress.Stage.ToString();
		var stageChanged = !string.Equals(stageId, _lastStageId, StringComparison.Ordinal);
		if (!stageChanged && progress.TimestampUtc - _lastProgressPublishedAt < TimeSpan.FromMilliseconds(500)) return false;

		_lastStageId = stageId;
		_lastProgressPublishedAt = progress.TimestampUtc;
		return true;
	}

	private void Post(Action action)
	{
		if (SynchronizationContext.Current == _context) action();
		else _context.Post(_ => action(), null);
	}

	private void ApplyOnContext(OperationProgressEvent progress)
	{
		var stageText = progress.StageText ?? progress.Message ?? "Processing";
		var stageId = progress.StageId ?? progress.Stage.ToString();
		var isDetailStage = IsCanonicalDetailStage(progress.StageId);
		if (!string.Equals(_displayedStageId, stageId, StringComparison.Ordinal))
		{
			if (_stageStartedAt != default && !isDetailStage)
				LogService.Info($"Operation stage completed: stage={_displayedStageId}, elapsed={(progress.TimestampUtc - _stageStartedAt).TotalMilliseconds:0}ms, operation={progress.OperationId:N}.");
			_displayedStageId = stageId;
			_stageStartedAt = progress.TimestampUtc;
		}

		if (progress.State == OperationState.Progress)
		{
			_target.Update(stageText, progress.Progress);
			_uiUpdateCount++;
			return;
		}

		if (progress.IsTerminal && _stageStartedAt != default && !isDetailStage)
			LogService.Info($"Operation stage completed: stage={_displayedStageId}, elapsed={(progress.TimestampUtc - _stageStartedAt).TotalMilliseconds:0}ms, operation={progress.OperationId:N}.");

		switch (progress.State)
		{
			case OperationState.Started:
				_target.MarkStarted(stageText);
				LogService.Info($"Operation started: kind={progress.Kind}, stage={progress.Stage}, operation={progress.OperationId:N}.");
				break;
			case OperationState.Completed:
				_target.MarkCompleted();
				LogTelemetry();
				LogService.Info($"Operation completed: kind={progress.Kind}, operation={progress.OperationId:N}.");
				break;
			case OperationState.Failed:
				_target.MarkFailed("Operation failed" + (string.IsNullOrWhiteSpace(progress.IssueCode) ? string.Empty : $" ({progress.IssueCode})"));
				LogTelemetry();
				LogService.Error($"Operation failed: kind={progress.Kind}, stage={progress.StageId ?? progress.Stage.ToString()}, operation={progress.OperationId:N}, issue={progress.IssueCode ?? "Unclassified"}, message={progress.Message ?? "None"}.");
				break;
			case OperationState.Canceled:
				_target.MarkCanceled();
				LogTelemetry();
				LogService.Info($"Operation canceled: kind={progress.Kind}, operation={progress.OperationId:N}.");
				break;
		}
	}

	private void LogTelemetry()
		=> LogService.Info($"Progress telemetry: received={_receivedProgressCount}, stage-updates={_publishedStageUpdateCount}, suppressed={_suppressedProgressCount}, ui-updates={_uiUpdateCount}, operation={_operationId:N}.");

	private static bool IsCanonicalDetailStage(string? stageId)
		=> stageId is "InspectEligibility" or "Plan" or "PrepareAvatarRig" or "CanonicalPreparing"
			or "TargetUnitPlan" or "RebuildTargetUnit" or "BuildCandidate" or "BuildCandidateMetrics"
			or "CanonicalUnitJobMetrics" or "MaterialBindingDiagnostics" or "CarryThroughMaterials"
			or "ValidateUnitReferences" or "WritePatch" or "WriteCandidate" or "CanonicalCompleted";
}

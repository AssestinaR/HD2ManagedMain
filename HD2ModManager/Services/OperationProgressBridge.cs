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

// Purpose: Provides an injectable BackgroundTaskItem target for production and lightweight tests.
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

public sealed class OperationProgressBridge
{
	private readonly IOperationProgressTarget _target;
	private readonly SynchronizationContext _context;
	private readonly Func<DateTimeOffset> _clock;
	private readonly NotificationService? _notifications;
	private readonly string? _notificationKey;
	private Guid _operationId;
	private long _lastSequence = -1;
	private double? _lastProgress;
	private long _lastCompleted = -1;
	private long _lastTotal = -1;
	private string? _lastStageId;
	private string? _displayedStageId;
	private DateTimeOffset _lastProgressAt;
	private DateTimeOffset _stageStartedAt;
	private bool _terminal;
	private long _lastPostedSequence = -1;
	private readonly object _gate = new();

	public OperationProgressBridge(IOperationProgressTarget target, Guid expectedOperationId, SynchronizationContext context, Func<DateTimeOffset>? clock = null, NotificationService? notifications = null, string? notificationKey = null)
	{
		_target = target ?? throw new ArgumentNullException(nameof(target));
		if (expectedOperationId == Guid.Empty) throw new ArgumentException("操作 ID 不能为 Guid.Empty。", nameof(expectedOperationId));
		_context = context ?? throw new ArgumentNullException(nameof(context));
		_operationId = expectedOperationId;
		_clock = clock ?? (() => DateTimeOffset.UtcNow);
		_notifications = notifications;
		_notificationKey = notificationKey;
	}

	public void Apply(OperationProgressEvent progress)
	{
		lock (_gate)
		{
			if (progress.OperationId != _operationId || progress.Sequence <= _lastSequence || _terminal) return;
			if (progress.State == OperationState.Progress && !ShouldSendProgress(progress)) { _lastSequence = progress.Sequence; return; }
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

	private bool ShouldSendProgress(OperationProgressEvent progress)
	{
		var now = _clock();
		var stageId = progress.StageId ?? progress.Stage.ToString();
		if (!string.Equals(stageId, _lastStageId, StringComparison.Ordinal))
		{
			_lastStageId = stageId;
			_lastProgress = progress.Progress;
			_lastProgressAt = now;
			return true;
		}
		var changedByPercent = progress.Progress is { } value && (_lastProgress is null || Math.Abs(value - _lastProgress.Value) >= 0.01d);
		var changedByCount = progress.Total > 0 && (progress.Completed != _lastCompleted || progress.Total != _lastTotal);
		var changedByTime = now - _lastProgressAt >= TimeSpan.FromMilliseconds(250);
		if (!changedByCount && !changedByPercent && !changedByTime) return false;
		// Unknown totals have no percentage signal; they are therefore time-throttled only.
		_lastProgress = progress.Progress;
		_lastCompleted = progress.Completed;
		_lastTotal = progress.Total;
		_lastProgressAt = now;
		return true;
	}

	private void Post(Action action)
	{
		if (SynchronizationContext.Current == _context) action();
		else _context.Post(_ => action(), null);
	}

	private void ApplyOnContext(OperationProgressEvent progress)
	{
		var stageText = progress.StageText ?? progress.Message ?? "处理中";
		var stageId = progress.StageId ?? progress.Stage.ToString();
		if (!string.Equals(_displayedStageId, stageId, StringComparison.Ordinal))
		{
			var now = progress.TimestampUtc;
			if (_stageStartedAt != default)
				LogService.Info($"操作阶段完成：阶段={_displayedStageId}，耗时={(now - _stageStartedAt).TotalMilliseconds:0}ms，操作={progress.OperationId:N}。");
			_displayedStageId = stageId;
			_stageStartedAt = now;
		}
		if (_notifications is not null && !string.IsNullOrWhiteSpace(_notificationKey))
		{
			var count = progress.Total > 0 ? $" {progress.Completed}/{progress.Total}" : string.Empty;
			_notifications.ShowOrUpdate(_notificationKey, $"{stageText}{count}", progress.State == OperationState.Failed ? NotificationLevel.Error : NotificationLevel.Info, null);
		}
		if (progress.IsTerminal && _stageStartedAt != default)
		{
			LogService.Info($"操作阶段完成：阶段={_displayedStageId}，耗时={(progress.TimestampUtc - _stageStartedAt).TotalMilliseconds:0}ms，操作={progress.OperationId:N}。");
			_stageStartedAt = default;
		}
		switch (progress.State)
		{
			case OperationState.Started:
				_target.MarkStarted(stageText);
				LogService.Info($"操作开始：类型={progress.Kind}，阶段={progress.Stage}，操作={progress.OperationId:N}。");
				break;
			case OperationState.Progress:
				_target.Update(stageText, progress.Progress);
				LogService.Info($"操作进度：阶段={stageText}，进度={progress.Completed}/{(progress.Total > 0 ? progress.Total : 0)}，操作={progress.OperationId:N}。");
				break;
			case OperationState.Completed:
				_target.MarkCompleted();
				LogService.Info($"操作完成：类型={progress.Kind}，操作={progress.OperationId:N}。");
				break;
			case OperationState.Failed:
				_target.MarkFailed("操作失败" + (string.IsNullOrWhiteSpace(progress.IssueCode) ? string.Empty : $"（{progress.IssueCode}）"));
				LogService.Error($"操作失败：类型={progress.Kind}，操作={progress.OperationId:N}，问题={progress.IssueCode ?? "未分类"}，消息={progress.Message ?? "无"}。");
				break;
			case OperationState.Canceled:
				_target.MarkCanceled();
				LogService.Info($"操作已取消：类型={progress.Kind}，操作={progress.OperationId:N}。");
				break;
		}
	}
}
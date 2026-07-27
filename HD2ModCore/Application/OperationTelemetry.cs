using System;

namespace HD2ModCore.Application;

// Purpose: Defines a dependency-free operation telemetry contract shared by core workflows and UI adapters.
public enum OperationKind
{
	PatchRepair,
	RepairBatch,
	RepairBatchItem,
	CrossArmorTransfer,
	Other,
}

// Purpose: Identifies the coarse-grained stage reported by an operation.
public enum OperationStage
{
	Queued,
	Preparing,
	Processing,
	Finalizing,
	Completed,
	Failed,
	Canceled,
}

// Purpose: Describes the lifecycle state of one operation progress event.
public enum OperationState
{
	Started,
	Progress,
	Completed,
	Failed,
	Canceled,
}

// Purpose: Carries bounded, timestamped progress information without depending on Manager or WPF types.
public sealed record OperationProgressEvent
{
	public Guid OperationId { get; }
	public Guid? ParentOperationId { get; }
	public OperationKind Kind { get; }
	public OperationStage Stage { get; }
	public OperationState State { get; }
	public long Completed { get; }
	public long Total { get; }
	public string? Message { get; }
	public string? IssueCode { get; }
	public DateTimeOffset TimestampUtc { get; }
	public long Sequence { get; }
	public string? StageId { get; }
	public string? StageText { get; }

	public OperationProgressEvent(
		Guid operationId,
		Guid? parentOperationId,
		OperationKind kind,
		OperationStage stage,
		OperationState state,
		long completed,
		long total,
		string? message,
		string? issueCode,
		DateTimeOffset timestampUtc,
		long sequence,
		string? stageId = null,
		string? stageText = null)
	{
		if (operationId == Guid.Empty) throw new ArgumentException("操作 ID 不能为 Guid.Empty。", nameof(operationId));
		if (completed < 0) throw new ArgumentOutOfRangeException(nameof(completed));
		if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
		if (total > 0 && completed > total) throw new ArgumentOutOfRangeException(nameof(completed));
		if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
		OperationId = operationId;
		ParentOperationId = parentOperationId;
		Kind = kind;
		Stage = stage;
		State = state;
		Completed = completed;
		Total = total;
		Message = message;
		IssueCode = issueCode;
		TimestampUtc = timestampUtc;
		Sequence = sequence;
		StageId = stageId;
		StageText = stageText;
	}

	public double? Progress => Total > 0
		? Math.Clamp((double)Completed / Total, 0d, 1d)
		: null;

	public bool IsTerminal => State is OperationState.Completed or OperationState.Failed or OperationState.Canceled;

	public OperationProgressEvent(
		Guid operationId,
		OperationKind kind,
		OperationStage stage,
		OperationState state,
		long completed = 0,
		long total = 0,
		string? message = null,
		string? issueCode = null,
		Guid? parentOperationId = null,
		DateTimeOffset? timestampUtc = null,
		long sequence = 0)
		: this(operationId, parentOperationId, kind, stage, state, completed, total, message, issueCode, timestampUtc ?? DateTimeOffset.UtcNow, sequence, null, null)
	{
	}
}
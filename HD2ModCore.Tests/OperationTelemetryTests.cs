using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Tests;

public sealed class OperationTelemetryTests
{
	[Fact]
	public void Progress_IsClampedAndSafeWhenTotalIsUnknown()
	{
		var id = Guid.NewGuid();
		Assert.Equal(0.5d, new OperationProgressEvent(id, OperationKind.Other, OperationStage.Processing, OperationState.Progress, 5, 10).Progress);
		Assert.Equal(1d, new OperationProgressEvent(id, OperationKind.Other, OperationStage.Processing, OperationState.Progress, 10, 10).Progress);
		Assert.Null(new OperationProgressEvent(id, OperationKind.Other, OperationStage.Processing, OperationState.Progress, 1, 0).Progress);
	}

	[Fact]
	public void Event_PreservesIdentityStateAndFields()
	{
		var id = Guid.NewGuid();
		var parent = Guid.NewGuid();
		var timestamp = DateTimeOffset.UtcNow;
		var telemetry = new OperationProgressEvent(id, OperationKind.CrossArmorTransfer, OperationStage.Finalizing, OperationState.Completed, 3, 3, "done", "OK", parent, timestamp, 7);

		Assert.Equal(id, telemetry.OperationId);
		Assert.Equal(parent, telemetry.ParentOperationId);
		Assert.Equal(OperationState.Completed, telemetry.State);
		Assert.Equal("OK", telemetry.IssueCode);
		Assert.Equal(timestamp, telemetry.TimestampUtc);
		Assert.Equal(7, telemetry.Sequence);
		Assert.True(telemetry.IsTerminal);
	}

	[Fact]
	public void CrossArmorProgress_MapsStageIdAndStageTextWithoutLeakingMachineId()
	{
		var progress = new CrossArmorTransferProgress("RebuildTargetBatch", "正在重建目标 Unit", 2, 4, TimeSpan.FromSeconds(3));
		var telemetry = progress.ToOperationProgressEvent(Guid.NewGuid());

		Assert.Equal("RebuildTargetBatch", telemetry.StageId);
		Assert.Equal("正在重建目标 Unit", telemetry.StageText);
		Assert.Equal("正在重建目标 Unit", telemetry.Message);
	}
}
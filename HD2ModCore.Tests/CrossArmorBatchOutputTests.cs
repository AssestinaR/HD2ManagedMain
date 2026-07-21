using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using HD2ModCore.Infrastructure;

// Purpose: Verifies independently rebuilt cross-armor batches form one unique final target set.
namespace HD2ModCore.Tests;

public sealed class CrossArmorBatchOutputTests
{
	[Fact]
	public void CombineBatchOutputs_RejectsDuplicateTargetUnits()
	{
		var key = new AssetKey(0xe0a48d0be9a7453f, 1);
		var output = Output(key);

		var method = typeof(CrossArmorTransferCandidateService).GetMethod("CombineBatchOutputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
		var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [new[] { output, output }]));

		Assert.IsType<InvalidDataException>(exception.InnerException);
	}

	private static SdkStyleTargetShellPatchOutput Output(AssetKey target)
		=> new([], [], [new SdkStyleTargetShellPatchUnitResult(target, 1, 0, 1, [], [], [], [])]);
}
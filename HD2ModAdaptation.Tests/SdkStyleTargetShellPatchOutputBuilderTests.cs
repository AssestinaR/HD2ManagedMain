using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies multiple fully reconstructed target Units become distinct patch additions without source Unit entries.
public sealed class SdkStyleTargetShellPatchOutputBuilderTests
{
	[Fact]
	public void Build_RejectsDuplicateTargetUnitsBeforePatchWrite()
	{
		var target = new GameDataUnitMesh(new AssetKey(PatchUnitMeshReader.UnitTypeId, 2), "target", new PatchEntryPayload(new PatchTocEntry(new AssetKey(PatchUnitMeshReader.UnitTypeId, 2), "target", "target"), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>()), EmptyModel());
		var item = new SdkStyleTargetShellPatchWorkItem(target, Array.Empty<PatchUnitMesh>(), Array.Empty<TargetShellMeshMapping>());

		Assert.Throws<InvalidDataException>(() => new SdkStyleTargetShellPatchOutputBuilder().Build(new[] { item, item }));
	}

	private static UnitMeshModel EmptyModel()
		=> new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), Array.Empty<UnitStreamInfo>(), Array.Empty<UnitMeshInfo>(), Array.Empty<UnitMaterialBinding>(), Array.Empty<UnitRawMeshSummary>(), Array.Empty<UnitRawMeshData>());
}
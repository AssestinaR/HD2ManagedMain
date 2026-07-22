using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
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

	[Fact]
	public void ExpandCompleteLodFamilyMappings_MultipleApprovedMappings_DoesNotInferAdditionalFamilyMembers()
	{
		var sourceKey = new AssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, 0), (1, 1), (2, 2));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, 0), (1, 1), (2, 2)));
		var approved = new[]
		{
			new TargetShellMeshMapping(sourceKey, 0, 0),
			new TargetShellMeshMapping(sourceKey, 1, 1)
		};

		var result = Expand(target, new Dictionary<AssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Equal(approved, result);
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_RepresentativeMinusOne_UsesRealSourceLodFamily()
	{
		var sourceKey = new AssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, 0), (1, 1), (2, 2));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, -1), (1, 3), (2, 2), (3, 1), (4, 0)));
		var approved = new[] { new TargetShellMeshMapping(sourceKey, 0, 0) };

		var result = Expand(target, new Dictionary<AssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Collection(result,
			mapping => Assert.Equal((4, 0), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((3, 1), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((2, 2), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)));
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_NonUniqueSourceLod0_RemainsConservative()
	{
		var sourceKey = new AssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, -1), (1, 3), (2, 2), (3, 1), (4, 0));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, 0), (1, 0)));
		var approved = new[] { new TargetShellMeshMapping(sourceKey, 0, 4) };

		var result = Expand(target, new Dictionary<AssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Equal(approved, result);
	}

	private static IReadOnlyList<TargetShellMeshMapping> Expand(UnitMeshModel target, IReadOnlyDictionary<AssetKey, PatchUnitMesh> sources, IReadOnlyList<TargetShellMeshMapping> approved)
	{
		var method = typeof(CrossArmorTransferCandidateService).GetMethod("ExpandCompleteLodFamilyMappings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
		return (IReadOnlyList<TargetShellMeshMapping>)method.Invoke(null, [target, sources, approved])!;
	}

	private static PatchUnitMesh CreatePatchUnit(AssetKey key, UnitMeshModel model)
		=> new(new PatchTocEntry(key, "test.patch", "test.patch"), new PatchEntryPayload(new PatchTocEntry(key, "test.patch", "test.patch"), [], [], []), model);

	private static UnitMeshModel CreateModel(params (int MeshInfoIndex, int LodIndex)[] specs)
	{
		var stream = new UnitStreamInfo(0, 0, 0, 0, 0, 3, 12, 0, 3, 0, 0, 0, 0, 0, []);
		var meshes = specs.Select(spec => new UnitMeshInfo(spec.MeshInfoIndex, 0, (uint)spec.MeshInfoIndex, spec.LodIndex, 0, 0, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(spec.LodIndex, spec.MeshInfoIndex), [0], [new UnitMeshSectionInfo(0, 0, 0, 0, 3, 0, 3, 0)])).ToArray();
		var rawMeshes = specs.Select(spec => new UnitRawMeshData(spec.MeshInfoIndex, (uint)spec.MeshInfoIndex, spec.LodIndex, 0, [new UnitRawMeshSectionData(0, 0, [new UnitTriangleIndices(0, 1, 2)])], [new UnitTriangleIndices(0, 1, 2)], [])).ToArray();
		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, [], [stream], meshes, [], [], rawMeshes);
	}

	private static SdkStyleTargetShellPatchOutput Output(AssetKey target)
		=> new([], [], [new SdkStyleTargetShellPatchUnitResult(target, 1, 0, 1, [], [], [], [])]);
}
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;

// Purpose: Verifies independently rebuilt cross-armor batches form one unique final target set.
namespace HD2ModCore.Tests;

public sealed class CrossArmorBatchOutputTests
{
	[Fact]
	public void CombineBatchOutputs_RejectsDuplicateTargetUnits()
	{
		var key = new AdaptationAssetKey(0xe0a48d0be9a7453f, 1);
		var output = Output(key);

		var method = typeof(CrossArmorTransferCandidateService).GetMethod("CombineBatchOutputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
		var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [new[] { output, output }]));

		Assert.IsType<InvalidDataException>(exception.InnerException);
	}

	[Fact]
	public void TargetGroups_IncludeUnitsWithOnlyHiddenMappings()
	{
		var hiddenTarget = new AdaptationAssetKey(0xe0a48d0be9a7453f, 3);
		var mappings = new[]
		{
			new CrossArmorTransferMapping(
				new CrossArmorPhysicalTargetKey(new HD2ModCore.Domain.AssetKey(hiddenTarget.TypeId, hiddenTarget.FileId), 4),
				null!,
				null,
				false,
				"隐藏",
				Array.Empty<string>(),
				Array.Empty<string>(),
				false,
				false)
		};

		var groups = mappings.GroupBy(mapping => mapping.PhysicalTarget.UnitAssetKey).ToArray();

		Assert.Single(groups);
		Assert.Equal(new HD2ModCore.Domain.AssetKey(hiddenTarget.TypeId, hiddenTarget.FileId), groups[0].Key);
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_MultipleApprovedMappings_DoesNotInferAdditionalFamilyMembers()
	{
		var sourceKey = new AdaptationAssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, 0), (1, 1), (2, 2));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, 0), (1, 1), (2, 2)));
		var approved = new[]
		{
			new TargetShellMeshMapping(sourceKey, 0, 0),
			new TargetShellMeshMapping(sourceKey, 1, 1)
		};

		var result = Expand(target, new Dictionary<AdaptationAssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Equal(approved, result);
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_RepresentativeMinusOne_UsesMatchingSourceLods()
	{
		var sourceKey = new AdaptationAssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, -1), (1, 4), (2, 3), (3, 2), (4, 1), (5, 0));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, -1), (1, 3), (2, 2), (3, 1), (4, 0)));
		var approved = new[] { new TargetShellMeshMapping(sourceKey, 0, 5) };

		var result = Expand(target, new Dictionary<AdaptationAssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Collection(result,
			mapping => Assert.Equal((4, 5), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((1, 2), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((2, 3), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((3, 4), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)));
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_MissingSourceLod_FallsBackToSourceLod0()
	{
		var sourceKey = new AdaptationAssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, -1), (1, 3), (2, 2), (3, 1), (4, 0));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, 0), (1, 1), (2, 3)));
		var approved = new[] { new TargetShellMeshMapping(sourceKey, 0, 4) };

		var result = Expand(target, new Dictionary<AdaptationAssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Collection(result,
			mapping => Assert.Equal((0, 4), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((2, 1), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((0, 2), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)),
			mapping => Assert.Equal((1, 3), (mapping.SourceMeshInfoIndex, mapping.TargetMeshInfoIndex)));
	}

	[Fact]
	public void ExpandCompleteLodFamilyMappings_NonUniqueSourceLod0_RemainsConservative()
	{
		var sourceKey = new AdaptationAssetKey(0xe0a48d0be9a7453f, 2);
		var target = CreateModel((0, -1), (1, 3), (2, 2), (3, 1), (4, 0));
		var source = CreatePatchUnit(sourceKey, CreateModel((0, 0), (1, 0)));
		var approved = new[] { new TargetShellMeshMapping(sourceKey, 0, 4) };

		var result = Expand(target, new Dictionary<AdaptationAssetKey, PatchUnitMesh> { [sourceKey] = source }, approved);

		Assert.Equal(approved, result);
	}

	private static IReadOnlyList<TargetShellMeshMapping> Expand(UnitMeshModel target, IReadOnlyDictionary<AdaptationAssetKey, PatchUnitMesh> sources, IReadOnlyList<TargetShellMeshMapping> approved)
	{
		var method = typeof(CrossArmorTransferCandidateService).GetMethod("ExpandCompleteLodFamilyMappings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
		return (IReadOnlyList<TargetShellMeshMapping>)method.Invoke(null, [target, sources, approved])!;
	}

	private static PatchUnitMesh CreatePatchUnit(AdaptationAssetKey key, UnitMeshModel model)
		=> new(new HD2ModAdaptation.PatchReconstruction.PatchTocEntry(key, "test.patch", "test.patch"), new HD2ModAdaptation.PatchReconstruction.PatchEntryPayload(new HD2ModAdaptation.PatchReconstruction.PatchTocEntry(key, "test.patch", "test.patch"), [], [], []), model);

	private static UnitMeshModel CreateModel(params (int MeshInfoIndex, int LodIndex)[] specs)
	{
		var stream = new UnitStreamInfo(0, 0, 0, 0, 0, 3, 12, 0, 3, 0, 0, 0, 0, 0, []);
		var meshes = specs.Select(spec => new UnitMeshInfo(spec.MeshInfoIndex, 0, (uint)spec.MeshInfoIndex, spec.LodIndex, 0, 0, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(spec.LodIndex, spec.MeshInfoIndex), [0], [new UnitMeshSectionInfo(0, 0, 0, 0, 3, 0, 3, 0)])).ToArray();
		var rawMeshes = specs.Select(spec => new UnitRawMeshData(spec.MeshInfoIndex, (uint)spec.MeshInfoIndex, spec.LodIndex, 0, [new UnitRawMeshSectionData(0, 0, [new UnitTriangleIndices(0, 1, 2)])], [new UnitTriangleIndices(0, 1, 2)], [])).ToArray();
		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, [], [stream], meshes, [], [], rawMeshes);
	}

	private static SdkStyleTargetShellPatchOutput Output(AdaptationAssetKey target)
		=> new([], [], [new SdkStyleTargetShellPatchUnitResult(target, 1, 0, 1, [], [], [], [])]);
}
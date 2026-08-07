using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies semantic Unit mesh replacement candidate selection before target-shell transfer.
public sealed class UnitMeshReplacementStrategyTests
{
	[Fact]
	public void FindCandidates_PrefersSemanticMatchOverSameMeshInfoIndex()
	{
		var target = CreateModel(new MeshSpec(0, 100, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20));
		var source = CreateModel(
			new MeshSpec(0, 200, "body_torso_Medium_lod0", "body", "torso", "Medium", "", 60, 20),
			new MeshSpec(1, 201, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20));

		var selected = UnitMeshReplacementStrategy.SelectNonConflictingCandidates(new UnitMeshReplacementStrategy(allowExperimentalFallback: true).FindCandidates(target, source));

		var candidate = Assert.Single(selected);
		Assert.Equal(0, candidate.TargetMeshInfoIndex);
		Assert.Equal(1, candidate.SourceMeshInfoIndex);
		Assert.Contains("Semantic part match", candidate.Reason);
	}

	[Fact]
	public void FindCandidates_RejectsTinySemanticSourceMeshes()
	{
		var target = CreateModel(new MeshSpec(0, 100, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20));
		var source = CreateModel(new MeshSpec(0, 200, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 10, 3));

		var candidates = new UnitMeshReplacementStrategy(allowExperimentalFallback: true).FindCandidates(target, source);

		Assert.Empty(candidates);
	}

	[Fact]
	public void FindCandidates_RejectsDifferentSectionCounts()
	{
		var target = CreateModel(new MeshSpec(0, 100, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20, SectionCount: 1));
		var source = CreateModel(new MeshSpec(0, 200, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20, SectionCount: 2));

		var candidates = new UnitMeshReplacementStrategy(allowExperimentalFallback: true).FindCandidates(target, source);

		Assert.Empty(candidates);
	}

	[Fact]
	public void FindCandidates_RejectsDifferentStreamsWithoutSemanticEvidence()
	{
		var target = CreateModel(new MeshSpec(0, 100, "", "", "", "", "", 60, 20));
		var source = CreateModel(new MeshSpec(0, 200, "", "", "", "", "", 60, 20)) with
		{
			Streams = [CreateStream(vertexStride: 16)]
		};

		var candidates = new UnitMeshReplacementStrategy(allowExperimentalFallback: true).FindCandidates(target, source);

		Assert.Empty(candidates);
	}

	[Fact]
	public void FindCandidates_DifferentStreamsWithSameSemanticEvidence_UsesSdkStreamTranscode()
	{
		var target = CreateModel(new MeshSpec(0, 100, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20));
		var source = CreateModel(new MeshSpec(0, 200, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20)) with
		{
			Streams = [CreateStream(vertexStride: 16)]
		};

		var candidate = Assert.Single(new UnitMeshReplacementStrategy(allowExperimentalFallback: true).FindCandidates(target, source));

		Assert.Equal(UnitMeshReplacementCandidateKind.SdkStreamTranscode, candidate.Kind);
		Assert.Contains("SDK-style stream transcode", candidate.Reason);
	}

	[Fact]
	public void AutoLodExpansion_UsesRealSourceLod0ForEveryVisibleTargetLod()
	{
		var key = new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1111111111111111);
		var source = CreateModel(
			new MeshSpec(0, 200, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20),
			new MeshSpec(1, 201, "body_legs_Medium_culling", "body", "legs", "Medium", "", 8, 3, LodIndex: -1));
		var target = CreateModel(
			new MeshSpec(4, 100, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20),
			new MeshSpec(5, 101, "body_legs_Medium_lod1", "body", "legs", "Medium", "", 30, 10, LodIndex: 1),
			new MeshSpec(6, 102, "body_legs_Medium_culling", "body", "legs", "Medium", "", 8, 3, LodIndex: -1));

		var mappings = CanonicalAutoLodMappingExpander.Expand(
			target,
			new Dictionary<AssetKey, UnitMeshModel> { [key] = source },
			[new CanonicalReplacementMapping(new(key, 0), new(key, 4))]);

		Assert.Equal(0, mappings.Single(mapping => mapping.Target.MeshInfoIndex == 4).Source.MeshInfoIndex);
		Assert.Equal(0, mappings.Single(mapping => mapping.Target.MeshInfoIndex == 5).Source.MeshInfoIndex);
		Assert.Equal(1, mappings.Single(mapping => mapping.Target.MeshInfoIndex == 6).Source.MeshInfoIndex);
	}

	[Fact]
	public void CreatePlan_MapsSelectedCandidatesAndMinifiesRemainingTargetMeshes()
	{
		var unitKey = new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1111111111111111);
		var sourceModel = CreateModel(new MeshSpec(0, 200, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20));
		var targetModel = CreateModel(
			new MeshSpec(0, 100, "body_legs_Medium_lod0", "body", "legs", "Medium", "", 60, 20),
			new MeshSpec(1, 101, "body_torso_Medium_lod0", "body", "torso", "Medium", "", 60, 20));
		var source = new PatchUnitMesh(new PatchTocEntry(unitKey, "source.patch", "source.patch"), new PatchEntryPayload(new PatchTocEntry(unitKey, "source.patch", "source.patch"), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>()), sourceModel);
		var target = new GameDataUnitMesh(unitKey, "units", new PatchEntryPayload(new PatchTocEntry(unitKey, "units", "units"), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>()), targetModel);

		var plan = new SameKeyTargetShellPlanningOperation().CreatePlan(source, target);

		var mapping = Assert.Single(plan.MeshMappings);
		Assert.Equal(0, mapping.SourceMeshInfoIndex);
		Assert.Equal(0, mapping.TargetMeshInfoIndex);
		Assert.Equal(new[] { 1 }, plan.MinifiedTargetMeshInfoIndexes);
		Assert.True(plan.HasFullTargetShellCoverage);
	}

	private static UnitMeshModel CreateModel(params MeshSpec[] meshes)
	{
		var stream = CreateStream(vertexStride: 12);
		var meshInfos = new List<UnitMeshInfo>();
		var rawMeshes = new List<UnitRawMeshData>();
		foreach (var spec in meshes)
		{
			var materialSlot = checked((uint)(1000 + spec.MeshInfoIndex));
			var semantic = new UnitMeshSemanticInfo(spec.Name, spec.Slot, spec.PieceType, spec.BodyType, spec.Weight, spec.LodIndex, spec.MeshInfoIndex, false, false, spec.LodIndex is not 0 and not -1);
			var sectionInfo = new UnitMeshSectionInfo(0, 0, materialSlot, 0, spec.VertexCount, 0, checked((uint)(spec.TriangleCount * 3)), 0);
			meshInfos.Add(new UnitMeshInfo(spec.MeshInfoIndex, 0, spec.MeshId, spec.LodIndex, 0, 0, 1, 0, 1, 0, semantic, new[] { materialSlot }, new[] { sectionInfo }));

			var vertices = Enumerable.Range(0, checked((int)spec.VertexCount))
				.Select(index => new UnitRawVertexRecord((uint)index, new byte[12], Array.Empty<UnitVertexComponentValue>()))
				.ToArray();
			var triangles = Enumerable.Range(0, checked((int)spec.TriangleCount))
				.Select(index => new UnitTriangleIndices((uint)(index % spec.VertexCount), (uint)((index + 1) % spec.VertexCount), (uint)((index + 2) % spec.VertexCount)))
				.ToArray();
			var sections = Enumerable.Range(0, spec.SectionCount)
				.Select(index => new UnitRawMeshSectionData((uint)index, materialSlot, triangles))
				.ToArray();
			rawMeshes.Add(new UnitRawMeshData(spec.MeshInfoIndex, spec.MeshId, spec.LodIndex, 0, sections, triangles, vertices));
		}

		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), new[] { stream }, meshInfos, Array.Empty<UnitMaterialBinding>(), Array.Empty<UnitRawMeshSummary>(), rawMeshes);
	}

	private static UnitStreamInfo CreateStream(uint vertexStride)
		=> new(0, 0, 0, 1, 0, 6, vertexStride, 0, 6, 0, 0, 0, 0, 0, [new UnitStreamComponentInfo(0, "position", 0, "vec3_float", 0, 0, vertexStride)]);

	private sealed record MeshSpec(
		int MeshInfoIndex,
		uint MeshId,
		string Name,
		string Slot,
		string PieceType,
		string BodyType,
		string Weight,
		uint VertexCount,
		uint TriangleCount,
		int SectionCount = 1,
		int LodIndex = 0);
}

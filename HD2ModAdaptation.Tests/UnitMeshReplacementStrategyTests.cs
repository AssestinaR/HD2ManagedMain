using HD2ModAdaptation.PatchReconstruction.UnitMesh;
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

	private static UnitMeshModel CreateModel(params MeshSpec[] meshes)
	{
		var component = new UnitStreamComponentInfo(0, "position", 0, "vec3_float", 0, 0, 12);
		var stream = new UnitStreamInfo(0, 0, 0, 1, 0, 6, 12, 0, 6, 0, 0, 0, 0, 0, new[] { component });
		var meshInfos = new List<UnitMeshInfo>();
		var rawMeshes = new List<UnitRawMeshData>();
		foreach (var spec in meshes)
		{
			var materialSlot = checked((uint)(1000 + spec.MeshInfoIndex));
			var semantic = new UnitMeshSemanticInfo(spec.Name, spec.Slot, spec.PieceType, spec.BodyType, spec.Weight, 0, spec.MeshInfoIndex, false, false, false);
			var sectionInfo = new UnitMeshSectionInfo(0, 0, materialSlot, 0, spec.VertexCount, 0, checked((uint)(spec.TriangleCount * 3)), 0);
			meshInfos.Add(new UnitMeshInfo(spec.MeshInfoIndex, 0, spec.MeshId, 0, 0, 0, 1, 0, 1, 0, semantic, new[] { materialSlot }, new[] { sectionInfo }));

			var vertices = Enumerable.Range(0, checked((int)spec.VertexCount))
				.Select(index => new UnitRawVertexRecord((uint)index, new byte[12], Array.Empty<UnitVertexComponentValue>()))
				.ToArray();
			var triangles = Enumerable.Range(0, checked((int)spec.TriangleCount))
				.Select(index => new UnitTriangleIndices((uint)(index % spec.VertexCount), (uint)((index + 1) % spec.VertexCount), (uint)((index + 2) % spec.VertexCount)))
				.ToArray();
			var section = new UnitRawMeshSectionData(0, materialSlot, triangles);
			rawMeshes.Add(new UnitRawMeshData(spec.MeshInfoIndex, spec.MeshId, 0, 0, new[] { section }, triangles, vertices));
		}

		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), new[] { stream }, meshInfos, Array.Empty<UnitMaterialBinding>(), Array.Empty<UnitRawMeshSummary>(), rawMeshes);
	}

	private sealed record MeshSpec(
		int MeshInfoIndex,
		uint MeshId,
		string Name,
		string Slot,
		string PieceType,
		string BodyType,
		string Weight,
		uint VertexCount,
		uint TriangleCount);
}
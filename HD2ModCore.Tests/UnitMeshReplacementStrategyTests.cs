using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 UnitMeshReplacementStrategy 按结构兼容性选择 RawMesh 替换候选。
// Purpose: Verifies UnitMeshReplacementStrategy selects RawMesh replacement candidates by structural compatibility.
public sealed class UnitMeshReplacementStrategyTests
{
	[Fact]
	public void FindCandidates_SameMeshIdAndLayout_ReturnsHighestRankedCandidate()
	{
		var target = CreateModel(meshId: 100, lodIndex: 1, materialSlots: [10, 20]);
		var source = CreateModel(meshId: 100, lodIndex: 1, materialSlots: [10, 20]);
		var strategy = new UnitMeshReplacementStrategy();

		var candidate = Assert.Single(strategy.FindCandidates(target, source));

		Assert.Equal(UnitMeshReplacementCandidateKind.SameMeshId, candidate.Kind);
		Assert.Equal(0, candidate.TargetMeshInfoIndex);
		Assert.Equal(0, candidate.SourceMeshInfoIndex);
		Assert.Equal(100u, candidate.TargetMeshId);
		Assert.Equal(100u, candidate.SourceMeshId);
		Assert.Equal(1, candidate.LodIndex);
		Assert.Equal(12u, candidate.VertexStride);
		Assert.Single(candidate.ComponentLayout);
		Assert.True(candidate.Score >= 400);
	}

	[Fact]
	public void FindCandidates_SameLodAndMaterialSlots_ReturnsStructuralCandidate()
	{
		var target = CreateModel(meshId: 100, lodIndex: 2, materialSlots: [10, 20]);
		var source = CreateModel(meshId: 200, lodIndex: 2, materialSlots: [10, 20]);
		var strategy = new UnitMeshReplacementStrategy();

		var candidate = Assert.Single(strategy.FindCandidates(target, source));

		Assert.Equal(UnitMeshReplacementCandidateKind.SameLodAndMaterialSlots, candidate.Kind);
		Assert.Contains("material", candidate.Reason, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void FindCandidates_DifferentComponentLayout_ReturnsNoCandidates()
	{
		var target = CreateModel(meshId: 100, lodIndex: 0, materialSlots: [10]);
		var source = CreateModel(meshId: 100, lodIndex: 0, materialSlots: [10], componentFormat: 2);
		var strategy = new UnitMeshReplacementStrategy();

		var candidates = strategy.FindCandidates(target, source);

		Assert.Empty(candidates);
	}

	[Fact]
	public void FindCandidates_ExperimentalFallbackWithExtremeGeometryRatio_ReturnsNoCandidates()
	{
		var target = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10], vertexCount: 1200, triangleCount: 800));
		var source = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 200, lodIndex: 0, materialSlots: [10], vertexCount: 24, triangleCount: 12), componentFormat: 2);
		var strategy = new UnitMeshReplacementStrategy(allowExperimentalFallback: true);

		var candidates = strategy.FindCandidates(target, source);

		Assert.Empty(candidates);
	}

	[Fact]
	public void FindCandidates_ExperimentalFallbackWithSameMeshIdAllowsDifferentDetailLevel()
	{
		var target = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10], vertexCount: 1200, triangleCount: 800));
		var source = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10], vertexCount: 16000, triangleCount: 25000), componentFormat: 2);
		var strategy = new UnitMeshReplacementStrategy(allowExperimentalFallback: true);

		var candidate = Assert.Single(strategy.FindCandidates(target, source));

		Assert.Equal(UnitMeshReplacementCandidateKind.ExperimentalFallback, candidate.Kind);
		Assert.Equal(100u, candidate.SourceMeshId);
	}

	[Fact]
	public void FindCandidates_ExperimentalFallbackWithComparableGeometry_ReturnsCandidate()
	{
		var target = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10], vertexCount: 1200, triangleCount: 800));
		var source = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10], vertexCount: 900, triangleCount: 700), componentFormat: 2);
		var strategy = new UnitMeshReplacementStrategy(allowExperimentalFallback: true);

		var candidate = Assert.Single(strategy.FindCandidates(target, source));

		Assert.Equal(UnitMeshReplacementCandidateKind.ExperimentalFallback, candidate.Kind);
	}

	[Fact]
	public void FindCandidates_MultipleCandidates_OrdersByScoreThenIndex()
	{
		var target = CreateModel(meshId: 100, lodIndex: 0, materialSlots: [10]);
		var source = CreateModel(
			CreateRawMesh(meshInfoIndex: 1, meshId: 200, lodIndex: 5, materialSlots: [30]),
			CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10]));
		var strategy = new UnitMeshReplacementStrategy();

		var candidates = strategy.FindCandidates(target, source);

		Assert.Equal(2, candidates.Count);
		Assert.Equal(UnitMeshReplacementCandidateKind.SameMeshId, candidates[0].Kind);
		Assert.Equal(0, candidates[0].SourceMeshInfoIndex);
		Assert.Equal(UnitMeshReplacementCandidateKind.LayoutOnly, candidates[1].Kind);
		Assert.Equal(1, candidates[1].SourceMeshInfoIndex);
	}

	[Fact]
	public void FindCandidates_WithSemanticInfo_PrefersMatchingPart()
	{
		var target = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10]), CreateSemantic("Torso", "Armor", "Slim"));
		var source = CreateModel(
			(CreateRawMesh(meshInfoIndex: 0, meshId: 200, lodIndex: 0, materialSlots: [10]), CreateSemantic("Helmet", "Armor", "Any")),
			(CreateRawMesh(meshInfoIndex: 1, meshId: 300, lodIndex: 0, materialSlots: [10]), CreateSemantic("Torso", "Armor", "Slim")));
		var strategy = new UnitMeshReplacementStrategy();

		var candidate = Assert.Single(strategy.FindCandidates(target, source));

		Assert.Equal(1, candidate.SourceMeshInfoIndex);
		Assert.Equal("Torso_Armor_Slim_lod0", candidate.SourceSemanticName);
		Assert.Contains("Semantic part match", candidate.Reason, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void FindCandidates_SkipsTinySourceMeshes()
	{
		var target = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10], vertexCount: 12));
		var source = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10], vertexCount: 3), CreateSemantic("Torso", "Armor", "Slim"));
		var strategy = new UnitMeshReplacementStrategy();

		var candidates = strategy.FindCandidates(target, source);

		Assert.Empty(candidates);
	}

	[Fact]
	public void FindCandidates_SkipsCullingSourceMeshes()
	{
		var target = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10]), CreateSemantic("Torso", "Armor", "Slim"));
		var source = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10]), CreateSemantic("Torso", "Armor", "Slim", isCullingBody: true));
		var strategy = new UnitMeshReplacementStrategy();

		var candidates = strategy.FindCandidates(target, source);

		Assert.Empty(candidates);
	}

	[Fact]
	public void FindCandidates_SkipsCullingTargetMeshes()
	{
		var target = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10]), CreateSemantic("Torso", "Armor", "Slim", isCullingBody: true));
		var source = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10]), CreateSemantic("Torso", "Armor", "Slim"));
		var strategy = new UnitMeshReplacementStrategy();

		var candidates = strategy.FindCandidates(target, source);

		Assert.Empty(candidates);
	}

	[Fact]
	public void FindCandidates_SkipsStaticSourceMeshes()
	{
		var target = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10]), CreateSemantic("Torso", "Armor", "Slim"));
		var source = CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId: 100, lodIndex: 0, materialSlots: [10]), CreateSemantic("Torso", "Armor", "Slim", isStaticMesh: true));
		var strategy = new UnitMeshReplacementStrategy();

		var candidates = strategy.FindCandidates(target, source);

		Assert.Empty(candidates);
	}

	private static UnitMeshModel CreateModel(uint meshId, int lodIndex, uint[] materialSlots, uint componentFormat = 1)
	{
		var rawMesh = CreateRawMesh(0, meshId, lodIndex, materialSlots);
		return CreateModel(rawMesh, componentFormat);
	}

	private static UnitMeshModel CreateModel(params UnitRawMeshData[] rawMeshes)
		=> CreateModel(rawMeshes, componentFormat: 1);

	private static UnitMeshModel CreateModel(UnitRawMeshData rawMesh, UnitMeshSemanticInfo semanticInfo)
		=> CreateModel([(rawMesh, semanticInfo)]);

	private static UnitMeshModel CreateModel(params (UnitRawMeshData RawMesh, UnitMeshSemanticInfo SemanticInfo)[] rawMeshes)
	{
		var rawMeshData = rawMeshes.Select(mesh => mesh.RawMesh).ToArray();
		var model = CreateModel(rawMeshData, componentFormat: 1);
		var meshes = rawMeshes.Select(mesh => CreateMeshInfo(mesh.RawMesh, mesh.SemanticInfo)).ToArray();
		return model with { Meshes = meshes };
	}

	private static UnitMeshModel CreateModel(UnitRawMeshData rawMesh, uint componentFormat = 1)
		=> CreateModel([rawMesh], componentFormat);

	private static UnitMeshModel CreateModel(UnitRawMeshData[] rawMeshes, uint componentFormat)
	{
		var streamIndexes = rawMeshes.Select(mesh => mesh.StreamIndex).Distinct().ToArray();
		var streams = streamIndexes.Select(index => CreateStream((int)index, componentFormat)).ToArray();
		return new UnitMeshModel(
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			UnitCustomizationInfo.Empty,
			Array.Empty<UnitBoneInfo>(),
			streams,
			Array.Empty<UnitMeshInfo>(),
			Array.Empty<UnitMaterialBinding>(),
			Array.Empty<UnitRawMeshSummary>(),
			rawMeshes);
	}

	private static UnitStreamInfo CreateStream(int index, uint componentFormat)
		=> new(
			index,
			0,
			0,
			1,
			0,
			3,
			12,
			0,
			3,
			0,
			0,
			36,
			36,
			6,
			[new UnitStreamComponentInfo(1, "POSITION", componentFormat, "Float3", 0, 0, 12)]);

	private static UnitRawMeshData CreateRawMesh(int meshInfoIndex, uint meshId, int lodIndex, uint[] materialSlots)
		=> CreateRawMesh(meshInfoIndex, meshId, lodIndex, materialSlots, vertexCount: 12);

	private static UnitRawMeshData CreateRawMesh(int meshInfoIndex, uint meshId, int lodIndex, uint[] materialSlots, int vertexCount)
		=> CreateRawMesh(meshInfoIndex, meshId, lodIndex, materialSlots, vertexCount, triangleCount: materialSlots.Length);

	private static UnitRawMeshData CreateRawMesh(int meshInfoIndex, uint meshId, int lodIndex, uint[] materialSlots, int vertexCount, int triangleCount)
	{
		var triangles = Enumerable.Range(0, triangleCount)
			.Select(index => new UnitTriangleIndices((uint)(index % vertexCount), (uint)((index + 1) % vertexCount), (uint)((index + 2) % vertexCount)))
			.ToArray();
		var sections = materialSlots.Select(slot => new UnitRawMeshSectionData(0, slot, triangles)).ToArray();
		return new UnitRawMeshData(
			meshInfoIndex,
			meshId,
			lodIndex,
			0,
			sections,
			sections.SelectMany(section => section.Triangles).ToArray(),
			Enumerable.Range(0, vertexCount).Select(index => new UnitRawVertexRecord((uint)index, new byte[12], Array.Empty<UnitVertexComponentValue>())).ToArray());
	}

	private static UnitMeshInfo CreateMeshInfo(UnitRawMeshData rawMesh, UnitMeshSemanticInfo semanticInfo)
		=> new(
			rawMesh.MeshInfoIndex,
			0,
			rawMesh.MeshId,
			rawMesh.LodIndex,
			0,
			rawMesh.StreamIndex,
			(uint)rawMesh.Sections.Count,
			0,
			(uint)rawMesh.Sections.Count,
			0,
			semanticInfo,
			rawMesh.Sections.Select(section => section.MaterialSlotId).ToArray(),
			rawMesh.Sections.Select((section, index) => new UnitMeshSectionInfo((uint)index, section.MaterialIndex, section.MaterialSlotId, 0, (uint)rawMesh.Vertices.Count, 0, (uint)(section.Triangles.Count * 3), 0)).ToArray());

	private static UnitMeshSemanticInfo CreateSemantic(string slot, string pieceType, string bodyType, bool isCullingBody = false, bool isStaticMesh = false)
		=> new($"{slot}_{pieceType}_{bodyType}_lod0", slot, pieceType, bodyType, string.Empty, 0, 0, isCullingBody, isStaticMesh, false);
}

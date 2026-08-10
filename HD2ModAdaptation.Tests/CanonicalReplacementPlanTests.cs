using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using System.Numerics;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies the canonical planning/session boundary remains explicit and fail-closed.
public sealed class CanonicalReplacementPlanTests
{
	[Fact]
	public void TryCreate_DuplicateTargetMapping_IsRejected()
	{
		var target = Mesh(2, 0x20);
		var result = CanonicalReplacementPlan.TryCreate([
			new(Mesh(1, 0x10), target),
			new(Mesh(1, 0x11), target)]);

		Assert.False(result.IsValid);
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "DuplicateTargetMapping");
	}

	[Fact]
	public void TryCreate_DuplicateSourceMapping_IsAllowedForDistinctTargets()
	{
		var source = Mesh(1, 0x10);
		var result = CanonicalReplacementPlan.TryCreate([
			new(source, Mesh(2, 0x20)),
			new(source, Mesh(2, 0x21))]);

		Assert.True(result.IsValid);
		Assert.Equal(2, result.Plan!.Mappings.Count);
	}

	[Fact]
	public void TryCreate_EmptySourceMesh_IsRejectedWithDiagnostic()
	{
		var result = CanonicalReplacementPlan.TryCreate([
			new(Mesh(1, 0x10), Mesh(2, 0x20), CanonicalSourceMeshState.Empty)]);

		Assert.False(result.IsValid);
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UnavailableSourceMesh");
	}

	[Fact]
	public void Requests_KeepSourceAndTargetKeysExplicit()
	{
		var source = new CanonicalSourceResourceRequest(Mesh(1, 0x10));
		var target = new CanonicalTargetResourceRequest(Mesh(2, 0x20));

		Assert.True(source.IsReadOnly);
		Assert.Equal(new AssetKey(1, 0x10), source.SourceKey.UnitKey);
		Assert.Equal(0, source.SourceKey.MeshInfoIndex);
		Assert.True(target.IsGameDataRead);
		Assert.Equal(new AssetKey(2, 0x20), target.TargetKey.UnitKey);
		Assert.Equal(0, target.TargetKey.MeshInfoIndex);
	}

	[Fact]
	public void Session_RejectsSourceRetainedEntries()
	{
		var session = new CanonicalPatchSession();

		Assert.Throws<InvalidOperationException>(() => session.AddEntry(
			new(new AssetKey(3, 0x30), CanonicalPatchEntryOwnership.SourceRetained)));
		Assert.Empty(session.Entries);
	}

	[Fact]
	public void Session_Finalize_RequiresTargetOutputAndRejectsLaterEntries()
	{
		var session = new CanonicalPatchSession();

		var invalid = session.Finalize(CanonicalDependencyClosureValidation.Valid);

		Assert.False(invalid.IsValid);
		Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == "MissingTargetOutput");
		Assert.Throws<InvalidOperationException>(() => session.AddEntry(
			new(new AssetKey(3, 0x30), CanonicalPatchEntryOwnership.RequiredDependency)));
	}

	[Fact]
	public void Session_Finalize_WithTargetOutput_IsValid()
	{
		var session = new CanonicalPatchSession();
		session.AddEntry(new(new AssetKey(3, 0x30), CanonicalPatchEntryOwnership.TargetOutput, [], [], []));

		var result = session.Finalize(CanonicalDependencyClosureValidation.Valid);

		Assert.True(result.IsValid);
		Assert.Empty(result.Diagnostics);
	}

	[Fact]
	public void Merger_Identity_ReplacesGeometryAndRetainsTargetMaterials()
	{
		var target = RawMesh([new UnitRawMeshSectionData(7, 70, [])], [], 9);
		var source = RawMesh([new UnitRawMeshSectionData(0, 10, [new(0, 1, 2)])], [
			Vertex(0, [1, 2, 3]), Vertex(1, [4, 5, 6]), Vertex(2, [7, 8, 9])]);

		var result = new CanonicalMeshSemanticMerger().TryMerge(target, source, Matrix4x4.Identity);

		Assert.True(result.IsValid);
		Assert.Equal(new UnitTriangleIndices(0, 1, 2), Assert.Single(result.Mesh!.Triangles));
		Assert.Equal((uint)0, Assert.Single(result.Mesh.Sections).MaterialIndex);
		Assert.Equal(3, result.Mesh.Vertices.Count);
	}

	[Fact]
	public void Merger_TransformsPositionComponent()
	{
		var source = RawMesh([new UnitRawMeshSectionData(0, 10, [new(0, 1, 2)])], [
			Vertex(0, [1, 2, 3]), Vertex(1, [4, 5, 6]), Vertex(2, [7, 8, 9])]);
		var transform = Matrix4x4.CreateTranslation(10, 20, 30);

		var result = new CanonicalMeshSemanticMerger().TryMerge(
			RawMesh([new UnitRawMeshSectionData(2, 20, [])], [], 9), source, transform);

		Assert.True(result.IsValid);
		Assert.Equal(new[] { 11f, 22f, 33f }, Assert.Single(result.Mesh!.Vertices[0].Components).FloatValues);
	}

	[Fact]
	public void Merger_NonIdentityTransform_DiscardsSourceAbiBytesAndPreservesSemanticComponents()
	{
		var source = RawMesh([new UnitRawMeshSectionData(0, 10, [new(0, 1, 2)])], [
			VertexWithAbi(0, [1, 2, 3]), VertexWithAbi(1, [4, 5, 6]), VertexWithAbi(2, [7, 8, 9])]);

		var result = new CanonicalMeshSemanticMerger().TryMerge(
			RawMesh([new UnitRawMeshSectionData(2, 20, [])], [], 9), source, Matrix4x4.CreateTranslation(10, 20, 30));

		Assert.True(result.IsValid);
		Assert.All(result.Mesh!.Vertices, vertex => Assert.Empty(vertex.Data));
		Assert.Equal(new[] { 11f, 22f, 33f }, Assert.Single(result.Mesh.Vertices[0].Components).FloatValues);
	}

	[Fact]
	public void Merger_FewerVisibleSourceSections_UsesExistingTargetMaterialsAndRetainsEmptyTargetSections()
	{
		var source = RawMesh([new UnitRawMeshSectionData(0, 10, [new(0, 1, 2)])], [Vertex(0), Vertex(1), Vertex(2)]);
		var result = new CanonicalMeshSemanticMerger().TryMerge(
			RawMesh([new(0, 20, []), new(1, 21, [])], [], 9), source, Matrix4x4.Identity);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Equal(new uint[] { 0, 1 }, result.Mesh!.Sections.Select(section => section.MaterialIndex));
		Assert.Equal(new uint[] { 20, 21 }, result.Mesh.Sections.Select(section => section.MaterialSlotId));
		Assert.Single(result.Mesh.Sections[0].Triangles);
		Assert.Empty(result.Mesh.Sections[1].Triangles);
	}

	[Fact]
	public void Merger_IgnoresZeroTriangleSourceSectionsBeforeLowering()
	{
		var source = RawMesh([
			new UnitRawMeshSectionData(0, 10, [new(0, 1, 2)]),
			new UnitRawMeshSectionData(1, 11, [])], [Vertex(0), Vertex(1), Vertex(2)]);

		var result = new CanonicalMeshSemanticMerger().TryMerge(
			RawMesh([new(7, 20, [])], [], 9), source, Matrix4x4.Identity);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Equal(0u, Assert.Single(result.Mesh!.Sections).MaterialIndex);
	}

	[Fact]
    public void Merger_VisibleSourceSectionsExceedTargetCapacity_ExpandsTargetSections()
	{
		var source = RawMesh([
			new UnitRawMeshSectionData(0, 10, [new(0, 1, 2)]),
			new UnitRawMeshSectionData(1, 11, [new(0, 1, 2)])], [Vertex(0), Vertex(1), Vertex(2)]);

		var result = new CanonicalMeshSemanticMerger().TryMerge(
			RawMesh([new(7, 20, [])], [], 9), source, Matrix4x4.Identity);

        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal([10u, 11u], result.Mesh!.Sections.Select(section => section.MaterialSlotId));
        Assert.Equal([0u, 1u], result.Mesh.Sections.Select(section => section.MaterialIndex));
        Assert.All(result.Mesh.Sections, section => Assert.Single(section.Triangles));
	}

	[Fact]
	public void Merger_EmptySource_IsRejected()
	{
		var result = new CanonicalMeshSemanticMerger().TryMerge(
			RawMesh([new(0, 20, [])], [], 9), RawMesh([new(0, 10, [])], [], 8), Matrix4x4.Identity);

		Assert.False(result.IsValid);
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EmptySourceMesh");
	}

	[Fact]
	public void Merger_OutOfRangeIndex_IsRejected()
	{
		var source = RawMesh([new UnitRawMeshSectionData(0, 10, [new(0, 1, 3)])], [Vertex(0), Vertex(1), Vertex(2)]);
		var result = new CanonicalMeshSemanticMerger().TryMerge(
			RawMesh([new(0, 20, [])], [], 9), source, Matrix4x4.Identity);

		Assert.False(result.IsValid);
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "IndexOutOfRange");
	}

	[Fact]
	public void Merger_ReplacesTargetTopologyWithoutRequiringLegacyTargetTrianglesToBeReadable()
	{
		var target = RawMesh([new UnitRawMeshSectionData(7, 70, [new(0, 1, 3)])], [Vertex(0), Vertex(1), Vertex(2)]);
		var source = RawMesh([new UnitRawMeshSectionData(0, 10, [new(0, 1, 2)])], [Vertex(0), Vertex(1), Vertex(2)]);

		var result = new CanonicalMeshSemanticMerger().TryMerge(target, source, Matrix4x4.Identity);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
		Assert.Equal(new UnitTriangleIndices(0, 1, 2), Assert.Single(result.Mesh!.Sections[0].Triangles));
	}

	private static CanonicalMeshKey Mesh(ulong typeId, ulong fileId)
		=> new(new AssetKey(typeId, fileId), 0);

	private static UnitRawMeshData RawMesh(IReadOnlyList<UnitRawMeshSectionData> sections, IReadOnlyList<UnitRawVertexRecord> vertices, uint meshId = 1)
		=> new(0, meshId, 0, 0, sections, sections.SelectMany(section => section.Triangles).ToArray(), vertices);

	private static UnitRawVertexRecord Vertex(uint index, float[]? position = null)
	{
		position ??= [index, index, index];
		return new(index, Array.Empty<byte>(), [new UnitVertexComponentValue(0, "position", 0, "vec3", 0, position, Array.Empty<uint>(), Array.Empty<byte>())]);
	}

	private static UnitRawVertexRecord VertexWithAbi(uint index, float[] position)
		=> new(index, [0xde, 0xad, 0xbe, 0xef], [new UnitVertexComponentValue(0, "position", 2, "vec3_float", 0, position, Array.Empty<uint>(), [0, 0, 0, 0])]);
}

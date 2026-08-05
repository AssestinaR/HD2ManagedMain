using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies Canonical final material ordinals remain distinct from serialized target section indices.
public sealed class CanonicalFinalMaterialLayoutTests
{
	[Fact]
	public void Create_UsesFirstTargetSlotAppearanceForSdkMaterialOrdinals()
	{
		var mesh = Mesh([
			new UnitRawMeshSectionData(90, 500, []),
			new UnitRawMeshSectionData(7, 700, []),
			new UnitRawMeshSectionData(42, 500, [])]);

		var result = CanonicalFinalMaterialLayout.TryCreate(mesh);

		Assert.True(result.IsValid);
		Assert.Equal([500u, 700u], result.Slots.Select(slot => slot.MaterialSlotId));
		Assert.Equal([0u, 1u, 0u], result.Sections.Select(section => section.MaterialOrdinal));
		Assert.Equal([90u, 7u, 42u], result.Sections.Select(section => section.TargetSectionMaterialIndex));
	}

	[Fact]
	public void SectionLowering_WritesFinalMaterialOrdinalInsteadOfOriginalSectionIndex()
	{
		var source = Mesh([new UnitRawMeshSectionData(0, 10, [new(0, 1, 2)])]);
		var target = Mesh([new UnitRawMeshSectionData(90, 500, []), new UnitRawMeshSectionData(7, 700, [])]);

		var result = CanonicalSectionLayout.TryCreate(source, target);

		Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
		Assert.Equal([0u, 1u], result.OutputSections.Select(section => section.MaterialIndex));
		Assert.Equal([500u, 700u], result.OutputSections.Select(section => section.MaterialSlotId));
		Assert.Single(result.OutputSections[0].Triangles);
		Assert.Empty(result.OutputSections[1].Triangles);
	}

	private static UnitRawMeshData Mesh(IReadOnlyList<UnitRawMeshSectionData> sections)
	{
		var vertices = new[]
		{
			new UnitRawVertexRecord(0, [], []),
			new UnitRawVertexRecord(1, [], []),
			new UnitRawVertexRecord(2, [], [])
		};
		return new(0, 1, 0, 0, sections, sections.SelectMany(section => section.Triangles).ToArray(), vertices);
	}
}
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

public sealed class CanonicalUnitMaterialLayoutCompilerTests
{
	[Fact]
	public void Compile_SeparatesTransferredMaterialFromConflictingTargetSlot()
	{
		var result = new CanonicalUnitMaterialLayoutCompiler().TryCompile(
			Model([new UnitMaterialBinding(77, 0x111UL)]),
			[Mesh(0, 77, true), Mesh(1, 77, true)],
			[new CanonicalMaterialSectionProvenance(0, 0, 0xaaaUL, 77, 77, 0x222UL)]);

		Assert.True(result.IsValid);
		var slot = result.Meshes[0].Sections.Single().MaterialSlotId;
		Assert.NotEqual(77u, slot);
		Assert.Contains(new UnitMaterialBinding(77, 0x111UL), result.Bindings);
		Assert.Contains(new UnitMaterialBinding(slot, 0x222UL), result.Bindings);
	}

	[Fact]
	public void Compile_ReusesOneSourceIdentityAcrossMeshes()
	{
		var result = new CanonicalUnitMaterialLayoutCompiler().TryCompile(
			Model([]),
			[Mesh(0, 1, true), Mesh(1, 2, true)],
			[
				new CanonicalMaterialSectionProvenance(0, 0, 0xaaaUL, 77, 77, 0x222UL),
				new CanonicalMaterialSectionProvenance(1, 0, 0xaaaUL, 77, 77, 0x222UL)
			]);

		Assert.True(result.IsValid);
		Assert.Equal(77u, result.Meshes[0].Sections[0].MaterialSlotId);
		Assert.Equal(77u, result.Meshes[1].Sections[0].MaterialSlotId);
		Assert.Equal(
			[(77u, 0x222UL), (77u, 0x222UL)],
			result.Bindings.Select(binding => (binding.SectionId, binding.MaterialId)));
	}

	[Fact]
	public void Compile_PreservesPreferredTargetSlotForOrdinaryReplacement()
	{
		var result = new CanonicalUnitMaterialLayoutCompiler().TryCompile(
			Model([]),
			[Mesh(0, 900, true)],
			[new CanonicalMaterialSectionProvenance(0, 0, 0xaaaUL, 77, 900, 0x222UL)]);

		Assert.True(result.IsValid);
		Assert.Equal(900u, result.Meshes.Single().Sections.Single().MaterialSlotId);
		Assert.Equal([new UnitMaterialBinding(900, 0x222UL)], result.Bindings);
	}

	[Fact]
	public void Compile_PreservesDistinctTargetSlotsForTheSameSourceMaterial()
	{
		var result = new CanonicalUnitMaterialLayoutCompiler().TryCompile(
			Model([]),
			[Mesh(0, 900, true), Mesh(1, 901, true)],
			[
				new CanonicalMaterialSectionProvenance(0, 0, 0xaaaUL, 77, 900, 0x222UL),
				new CanonicalMaterialSectionProvenance(1, 0, 0xaaaUL, 78, 901, 0x222UL)
			]);

		Assert.True(result.IsValid);
		Assert.Equal(900u, result.Meshes[0].Sections.Single().MaterialSlotId);
		Assert.Equal(901u, result.Meshes[1].Sections.Single().MaterialSlotId);
		Assert.Equal(
			[(900u, 0x222UL), (901u, 0x222UL)],
			result.Bindings.Select(binding => (binding.SectionId, binding.MaterialId)));
	}

	[Fact]
	public void Compile_ReusesEstablishedTargetSlotWhenLodsReverseTheirLocalSlotOrder()
	{
		var result = new CanonicalUnitMaterialLayoutCompiler().TryCompile(
			Model([]),
			[Mesh(0, 781, true), Mesh(1, 200, true)],
			[
				new CanonicalMaterialSectionProvenance(0, 0, 0xaaaUL, 77, 781, 0x222UL),
				new CanonicalMaterialSectionProvenance(1, 0, 0xaaaUL, 77, 200, 0x222UL)
			]);

		Assert.True(result.IsValid);
		Assert.Equal(781u, result.Meshes[0].Sections[0].MaterialSlotId);
		Assert.Equal(781u, result.Meshes[1].Sections[0].MaterialSlotId);
	}

	[Fact]
	public void Compile_ExpandedSectionUsesTargetUnitsKnownSlotForItsMaterial()
	{
		var result = new CanonicalUnitMaterialLayoutCompiler().TryCompile(
			Model([new UnitMaterialBinding(900, 0x222UL)]),
			[Mesh(0, 77, true)],
			[new CanonicalMaterialSectionProvenance(0, 0, 0xaaaUL, 77, 77, 0x222UL, true)]);

		Assert.True(result.IsValid);
		Assert.Equal(900u, result.Meshes.Single().Sections.Single().MaterialSlotId);
		Assert.Equal([new UnitMaterialBinding(900, 0x222UL)], result.Bindings);
	}

	private static UnitRawMeshData Mesh(int index, uint slot, bool visible)
		=> new(index, (uint)(index + 1), 0, 0,
			[new UnitRawMeshSectionData(0, slot, visible ? [new UnitTriangleIndices(0, 1, 2)] : [])],
			visible ? [new UnitTriangleIndices(0, 1, 2)] : [], []);

	private static UnitMeshModel Model(IReadOnlyList<UnitMaterialBinding> bindings)
		=> new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, [], [], [], bindings, [], []);
}

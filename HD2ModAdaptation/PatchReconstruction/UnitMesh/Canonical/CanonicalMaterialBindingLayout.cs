namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Combines target-owned and transferred external MaterialId bindings for final Canonical meshes.
public static class CanonicalMaterialBindingLayout
{
	public static IReadOnlyList<UnitMaterialBinding> Build(
		IReadOnlyList<UnitMaterialBinding> targetBindings,
		IReadOnlyList<UnitMaterialBinding> transferredBindings,
		IReadOnlyList<UnitRawMeshData> finalMeshes)
	{
		ArgumentNullException.ThrowIfNull(targetBindings);
		ArgumentNullException.ThrowIfNull(transferredBindings);
		ArgumentNullException.ThrowIfNull(finalMeshes);

		var usedSlots = finalMeshes
			.SelectMany(mesh => mesh.Sections)
			.Where(section => section.Triangles.Count != 0)
			.Select(section => section.MaterialSlotId)
			.ToHashSet();
		var replacedSlots = transferredBindings
			.Where(binding => binding.MaterialId != 0 && usedSlots.Contains(binding.SectionId))
			.Select(binding => binding.SectionId)
			.ToHashSet();

		return targetBindings
			.Where(binding => usedSlots.Contains(binding.SectionId) && !replacedSlots.Contains(binding.SectionId))
			.Concat(transferredBindings.Where(binding => binding.MaterialId != 0 && usedSlots.Contains(binding.SectionId)))
			.Distinct()
			.ToArray();
	}
}

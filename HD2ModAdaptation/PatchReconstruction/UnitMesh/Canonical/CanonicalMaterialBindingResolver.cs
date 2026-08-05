namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Resolves source Section material identities to final target slots without collapsing SDK material occurrences.
// SDK reference: GetMeshData creates one RawMaterial per Blender material slot, and Serialize writes every RawMaterial pair.
public sealed record CanonicalMaterialBindingResolution(
	IReadOnlyList<UnitMaterialBinding> Bindings,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Diagnostics.Count == 0;
}

public static class CanonicalMaterialBindingResolver
{
	private const uint DefaultMaterialSlotId = 155175220;

	public static CanonicalMaterialBindingResolution Resolve(
		UnitMeshModel source,
		UnitRawMeshData sourceRaw,
		UnitRawMeshData targetRaw,
		bool allowMultipleMaterialsPerTargetSlot = true)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(sourceRaw);
		ArgumentNullException.ThrowIfNull(targetRaw);

		var diagnostics = new List<CanonicalPlanDiagnostic>();
		var sourceMesh = source.Meshes.SingleOrDefault(mesh => mesh.Index == sourceRaw.MeshInfoIndex);
		if (sourceMesh is null)
			return new([], [new("SourceMeshMissingForMaterialBinding", $"Source MeshInfo {sourceRaw.MeshInfoIndex} is missing from the material binding model.")]);

		var visibleSections = sourceRaw.Sections.Where(section => section.Triangles.Count != 0).ToArray();
		if (visibleSections.Length > targetRaw.Sections.Count)
			return new([], [new("VisibleSectionCountMismatch", $"Source has {visibleSections.Length} visible material sections, but target has {targetRaw.Sections.Count} sections.")]);

		var sourceMaterialOccurrences = source.Materials
			.GroupBy(binding => binding.SectionId)
			.ToDictionary(group => group.Key, group => group.Select(binding => binding.MaterialId).ToArray());
		var occurrenceBySlot = new Dictionary<uint, int>();
		var bindings = new List<UnitMaterialBinding>();
		for (var index = 0; index < visibleSections.Length; index++)
		{
			var sourceSection = visibleSections[index];
			if (sourceSection.MaterialIndex >= sourceMesh.MaterialSlotIds.Count)
			{
				diagnostics.Add(new("SourceMaterialIndexOutOfRange", $"Source MeshInfo {sourceRaw.MeshInfoIndex} section {index} references material index {sourceSection.MaterialIndex} outside its material-slot table."));
				continue;
			}

			var sourceSlot = sourceMesh.MaterialSlotIds[(int)sourceSection.MaterialIndex];
			if (!sourceMaterialOccurrences.TryGetValue(sourceSlot, out var materialIds) || materialIds.Length == 0)
			{
				// Community SDK emits StingrayDefaultMaterial without a Unit root
				// Material binding. It is a valid placeholder and must retain the
				// target/default binding instead of failing source material resolution.
				if (sourceSlot == DefaultMaterialSlotId)
					continue;
				diagnostics.Add(new("SourceMaterialBindingMissing", $"Source material slot {sourceSlot} has no Unit Material binding."));
				continue;
			}

			var occurrence = occurrenceBySlot.TryGetValue(sourceSlot, out var current) ? current : 0;
			occurrenceBySlot[sourceSlot] = occurrence + 1;
			var materialId = materialIds[Math.Min(occurrence, materialIds.Length - 1)];
			var targetSlot = targetRaw.Sections[index].MaterialSlotId;
			if (!allowMultipleMaterialsPerTargetSlot && bindings.Any(binding => binding.SectionId == targetSlot && binding.MaterialId != materialId))
			{
				diagnostics.Add(new("CanonicalMaterialSlotConflict", $"Target material slot {targetSlot} maps to multiple source Material assets."));
				continue;
			}

			bindings.Add(new UnitMaterialBinding(targetSlot, materialId));
		}

		return new(bindings, diagnostics);
	}
}
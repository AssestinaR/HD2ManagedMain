namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Resolves source Section material identities to final target slots without collapsing SDK material occurrences.
// SDK reference: GetMeshData creates one RawMaterial per Blender material slot, and Serialize writes every RawMaterial pair.
public sealed record CanonicalMaterialBindingResolution(
	IReadOnlyList<UnitMaterialBinding> Bindings,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics,
	IReadOnlyList<CanonicalMaterialSectionBinding>? SectionBindings = null)
{
	public bool IsValid => Diagnostics.Count == 0;
	public IReadOnlyList<CanonicalMaterialSectionBinding> ResolvedSectionBindings => SectionBindings ?? [];
}

// A material claim is deliberately section-scoped. A Unit root Material table is
// shared by every MeshInfo, so slot identity cannot be finalized per Mesh.
public sealed record CanonicalMaterialSectionBinding(
	int FinalSectionIndex,
	uint SourceSlotId,
	uint PreferredTargetSlotId,
	ulong MaterialId,
	bool UsesTargetUnitMaterialSlotLookup);

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

		var sourceMaterialOccurrences = source.Materials
			.GroupBy(binding => binding.SectionId)
			.ToDictionary(group => group.Key, group => group.Select(binding => binding.MaterialId).ToArray());
		var occurrenceBySlot = new Dictionary<uint, int>();
		var bindings = new List<UnitMaterialBinding>();
		var sectionBindings = new List<CanonicalMaterialSectionBinding>();
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
			// SDK RawMaterial.IDFromName always resolves a final Blender material by
			// (target Unit, MaterialId, occurrence). It never reuses an unrelated
			// target shell slot merely because both meshes happen to have the same
			// number of sections. The compiler performs that lookup and allocates a
			// fresh slot when the target Unit has no matching MaterialId.
			var targetSlot = index < targetRaw.Sections.Count
				? targetRaw.Sections[index].MaterialSlotId
				: sourceSlot;
			if (!allowMultipleMaterialsPerTargetSlot && bindings.Any(binding => binding.SectionId == targetSlot && binding.MaterialId != materialId))
			{
				diagnostics.Add(new("CanonicalMaterialSlotConflict", $"Target material slot {targetSlot} maps to multiple source Material assets."));
				continue;
			}

			bindings.Add(new UnitMaterialBinding(targetSlot, materialId));
			sectionBindings.Add(new CanonicalMaterialSectionBinding(index, sourceSlot, targetSlot, materialId, UsesTargetUnitMaterialSlotLookup: true));
		}

		return new(bindings, diagnostics, sectionBindings);
	}
}

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Defines the final Blender-object material-slot order independently from Unit section IDs.
// SDK reference: GetMeshData enumerates object.material_slots and BoneInfo.SetRemap indexes that
// final ordinal list; MeshInfo serialization separately retains the portable material slot IDs.
public sealed record CanonicalFinalMaterialSlot(uint MaterialOrdinal, uint MaterialSlotId);

public sealed record CanonicalFinalMaterialSection(
	int SectionIndex,
	uint MaterialOrdinal,
	uint MaterialSlotId,
	uint TargetSectionMaterialIndex);

public sealed record CanonicalFinalMaterialLayoutResult(
	IReadOnlyList<CanonicalFinalMaterialSlot> Slots,
	IReadOnlyList<CanonicalFinalMaterialSection> Sections,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Diagnostics.Count == 0;

	public uint GetMaterialOrdinal(int sectionIndex) => Sections[sectionIndex].MaterialOrdinal;
}

public static class CanonicalFinalMaterialLayout
{
	public static CanonicalFinalMaterialLayoutResult TryCreate(UnitRawMeshData target)
	{
		ArgumentNullException.ThrowIfNull(target);
		if (target.Sections.Count == 0)
			return new([], [], [new("TargetSectionsMissing", "Canonical final material layout requires at least one target section.")]);

		var slots = new List<CanonicalFinalMaterialSlot>();
		var ordinalsBySlot = new Dictionary<uint, uint>();
		var sections = new List<CanonicalFinalMaterialSection>(target.Sections.Count);
		foreach (var (section, index) in target.Sections.Select((value, index) => (value, index)))
		{
			if (!ordinalsBySlot.TryGetValue(section.MaterialSlotId, out var ordinal))
			{
				ordinal = checked((uint)slots.Count);
				ordinalsBySlot.Add(section.MaterialSlotId, ordinal);
				slots.Add(new CanonicalFinalMaterialSlot(ordinal, section.MaterialSlotId));
			}
			sections.Add(new CanonicalFinalMaterialSection(index, ordinal, section.MaterialSlotId, section.MaterialIndex));
		}

		return new(slots, sections, Array.Empty<CanonicalPlanDiagnostic>());
	}

	public static IReadOnlyList<UnitRawMeshSectionData> ApplyToTargetSections(
		CanonicalFinalMaterialLayoutResult layout,
		IReadOnlyList<UnitRawMeshSectionData> sections)
	{
		ArgumentNullException.ThrowIfNull(layout);
		ArgumentNullException.ThrowIfNull(sections);
		if (!layout.IsValid || sections.Count != layout.Sections.Count)
			throw new InvalidDataException("Canonical final material layout does not match the target section contract.");
		return sections.Select((section, index) => section with
		{
			MaterialIndex = layout.GetMaterialOrdinal(index),
			MaterialSlotId = layout.Sections[index].MaterialSlotId
		}).ToArray();
	}
}
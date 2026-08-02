namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Lowers visible source polygons into the fixed target material-section contract before Canonical bone and GPU rebuilding.
// Blender/SDK reference: object.join assigns polygon material slots first; GetMeshData then emits sections from that final polygon set.
public sealed record CanonicalSectionAssignment(
	int SourceSectionIndex,
	UnitRawMeshSectionData SourceSection,
	int TargetSectionIndex,
	UnitRawMeshSectionData TargetSection);

public sealed record CanonicalSectionLayoutResult(
	IReadOnlyList<CanonicalSectionAssignment> Assignments,
	IReadOnlyList<UnitRawMeshSectionData> OutputSections,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Diagnostics.Count == 0;
}

public static class CanonicalSectionLayout
{
	public static CanonicalSectionLayoutResult TryCreate(UnitRawMeshData source, UnitRawMeshData target)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(target);
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		if (target.Sections.Count == 0)
			return new([], [], [new("TargetSectionsMissing", "Canonical section lowering requires at least one target material section.")]);
		var materialLayout = CanonicalFinalMaterialLayout.TryCreate(target);
		if (!materialLayout.IsValid)
			return new([], [], materialLayout.Diagnostics);

		var visible = source.Sections
			.Select((section, index) => new { Index = index, Section = section })
			.Where(item => item.Section.Triangles.Count != 0)
			.ToArray();
		if (visible.Length == 0)
			diagnostics.Add(new("EmptySourceMesh", "Canonical replacement requires at least one source section with triangles."));
		if (visible.Length > target.Sections.Count)
		{
			diagnostics.Add(new("VisibleSectionCountMismatch",
				$"Canonical target-material fallback can lower {visible.Length} visible source sections into only {target.Sections.Count} target sections. " +
				"This requires an explicit material/section merge plan; no ordinal, modulo, or automatic material guess was used."));
		}
		if (diagnostics.Count != 0)
			return new([], [], Array.AsReadOnly(diagnostics.ToArray()));

		var assignments = visible.Select((item, targetIndex) => new CanonicalSectionAssignment(
			item.Index, item.Section, targetIndex, target.Sections[targetIndex])).ToArray();
		var byTarget = assignments.ToDictionary(item => item.TargetSectionIndex);
		var output = target.Sections.Select((targetSection, targetIndex) => byTarget.TryGetValue(targetIndex, out var assignment)
			? new UnitRawMeshSectionData(materialLayout.GetMaterialOrdinal(targetIndex), targetSection.MaterialSlotId, assignment.SourceSection.Triangles.ToArray())
			: new UnitRawMeshSectionData(materialLayout.GetMaterialOrdinal(targetIndex), targetSection.MaterialSlotId, Array.Empty<UnitTriangleIndices>())).ToArray();
		return new(assignments, output, Array.Empty<CanonicalPlanDiagnostic>());
	}
}

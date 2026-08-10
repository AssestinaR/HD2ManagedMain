namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Builds the final material-section layout after the source mesh has been joined into the target shell.
// Blender/SDK reference: object.join preserves final material-slot/polygon groups; GetMeshData and Entry.Save
// emit one final section for each resulting material group. The original target section count is not a final cap.
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
	private static void Trace(string message) => System.Diagnostics.Trace.WriteLine($"[CanonicalSectionLayout] {message}");

	public static CanonicalSectionLayoutResult TryCreate(UnitRawMeshData source, UnitRawMeshData target)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(target);
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		if (target.Sections.Count == 0)
			return new([], [], [new("TargetSectionsMissing", "Canonical section rebuilding requires at least one target material section.")]);

		var visible = source.Sections
			.Select((section, index) => new { Index = index, Section = section })
			.Where(item => item.Section.Triangles.Count != 0)
			.ToArray();
		Trace($"sourceMesh={source.MeshInfoIndex} sourceSections={source.Sections.Count} visible={visible.Length} targetSections={target.Sections.Count} targetSlots={string.Join(',', target.Sections.Select(section => section.MaterialSlotId))}");
		if (visible.Length == 0)
			diagnostics.Add(new("EmptySourceMesh", "Canonical replacement requires at least one source section with triangles."));
		if (diagnostics.Count != 0)
			return new([], [], Array.AsReadOnly(diagnostics.ToArray()));

        var expandsTargetShell = visible.Length > target.Sections.Count;
        // For normal replacement the target shell keeps its material-slot identity. When
        // source geometry has more visible material groups, mirror the SDK: rebuild the
        // final section table from the source groups and let the writer expand MeshInfo.
        // The paired binding resolver carries the matching source Unit Material references;
        // this does not require the source Patch to embed Material assets.
        var output = expandsTargetShell
            ? visible.Select((item, sectionIndex) => new UnitRawMeshSectionData(
                checked((uint)sectionIndex),
                item.Section.MaterialSlotId,
                item.Section.Triangles.ToArray())).ToArray()
            : target.Sections.Select((targetSection, targetIndex) => new UnitRawMeshSectionData(
                checked((uint)targetIndex),
                targetSection.MaterialSlotId,
                targetIndex < visible.Length ? visible[targetIndex].Section.Triangles.ToArray() : Array.Empty<UnitTriangleIndices>())).ToArray();
		Trace($"finalSections={output.Length} finalSlots={string.Join(',', output.Select(section => section.MaterialSlotId))} triangles={output.Sum(section => section.Triangles.Count)}");
		var assignments = visible.Select((item, targetIndex) => new CanonicalSectionAssignment(
			item.Index, item.Section, targetIndex, output[targetIndex])).ToArray();
		return new(assignments, output, Array.Empty<CanonicalPlanDiagnostic>());
	}
}

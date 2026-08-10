namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Compiles one authoritative material identity table for every final MeshInfo in a Unit.
// The Unit root Material table is keyed by a 32-bit slot id, therefore source slot ids
// from different Units must be namespaced when they resolve to different Material assets.
public sealed record CanonicalMaterialSectionProvenance(
	int MeshInfoIndex,
	int SectionIndex,
	ulong SourceUnitFileId,
	uint SourceSlotId,
	uint PreferredTargetSlotId,
	ulong MaterialId);

public sealed record CanonicalUnitMaterialLayoutCompilation(
	IReadOnlyList<UnitRawMeshData> Meshes,
	IReadOnlyList<UnitMaterialBinding> Bindings,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Diagnostics.Count == 0;
}

public sealed class CanonicalUnitMaterialLayoutCompiler
{
	private const uint DefaultMaterialSlotId = 155175220;

	public CanonicalUnitMaterialLayoutCompilation TryCompile(
		UnitMeshModel target,
		IReadOnlyList<UnitRawMeshData> meshes,
		IReadOnlyList<CanonicalMaterialSectionProvenance> provenance)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(meshes);
		ArgumentNullException.ThrowIfNull(provenance);
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		var claims = provenance.GroupBy(item => (item.MeshInfoIndex, item.SectionIndex)).ToDictionary(group => group.Key, group => group.ToArray());
		if (claims.Any(pair => pair.Value.Select(item => (item.SourceUnitFileId, item.SourceSlotId, item.MaterialId)).Distinct().Count() != 1))
			diagnostics.Add(new("ConflictingSectionMaterialProvenance", "One final Canonical section resolved to multiple source materials."));
		if (diagnostics.Count != 0)
			return new([], [], diagnostics);

		var targetBindings = target.Materials
			.GroupBy(binding => binding.SectionId)
			.ToDictionary(group => group.Key, group => group.Select(binding => binding.MaterialId).Where(id => id != 0).Distinct().ToArray());
		var occupiedSlots = new HashSet<uint>();
		var bindingsBySlot = new Dictionary<uint, ulong>();
		var slotByIdentity = new Dictionary<MaterialIdentity, uint>();

		// Target-owned sections (minified, culling and default-material geometry) keep
		// their original slot identities. Reserve them before assigning transferred work.
		foreach (var mesh in meshes)
		{
			for (var index = 0; index < mesh.Sections.Count; index++)
			{
				var section = mesh.Sections[index];
				if (section.Triangles.Count == 0 || claims.ContainsKey((mesh.MeshInfoIndex, index))) continue;
				occupiedSlots.Add(section.MaterialSlotId);
				if (!targetBindings.TryGetValue(section.MaterialSlotId, out var materialIds) || materialIds.Length == 0) continue;
				if (materialIds.Length != 1)
				{
					diagnostics.Add(new("AmbiguousTargetMaterialSlot", $"Target slot {section.MaterialSlotId} has multiple Material assets."));
					continue;
				}
				bindingsBySlot[section.MaterialSlotId] = materialIds[0];
			}
		}
		if (diagnostics.Count != 0) return new([], [], diagnostics);

		var rewritten = new List<UnitRawMeshData>(meshes.Count);
		foreach (var mesh in meshes.OrderBy(mesh => mesh.MeshInfoIndex))
		{
			var sections = mesh.Sections.ToArray();
			for (var index = 0; index < sections.Length; index++)
			{
				if (!claims.TryGetValue((mesh.MeshInfoIndex, index), out var sectionClaims)) continue;
				var claim = sectionClaims[0];
				// A source material may be assigned to several distinct target shell
				// slots. Those slots are separate Blender material occurrences and must
				// survive in the final Unit even when their MaterialId is identical.
				var identity = new MaterialIdentity(claim.SourceUnitFileId, claim.SourceSlotId, claim.PreferredTargetSlotId, claim.MaterialId);
				if (!slotByIdentity.TryGetValue(identity, out var outputSlot))
				{
					outputSlot = claim.PreferredTargetSlotId;
					if (occupiedSlots.Contains(outputSlot) || bindingsBySlot.TryGetValue(outputSlot, out var existing) && existing != claim.MaterialId)
						outputSlot = AllocateSlot(identity, occupiedSlots);
					slotByIdentity.Add(identity, outputSlot);
					occupiedSlots.Add(outputSlot);
					bindingsBySlot[outputSlot] = claim.MaterialId;
				}
				sections[index] = sections[index] with { MaterialSlotId = outputSlot };
			}

			var provisional = mesh with { Sections = sections, Triangles = sections.SelectMany(section => section.Triangles).ToArray() };
			var localLayout = CanonicalFinalMaterialLayout.TryCreate(provisional);
			diagnostics.AddRange(localLayout.Diagnostics);
			rewritten.Add(localLayout.IsValid
				? provisional with { Sections = CanonicalFinalMaterialLayout.ApplyToTargetSections(localLayout, sections) }
				: provisional);
		}

		if (diagnostics.Count != 0) return new([], [], diagnostics);
		var usedSlots = rewritten.SelectMany(mesh => mesh.Sections).Where(section => section.Triangles.Count != 0).Select(section => section.MaterialSlotId).ToHashSet();
		return new(rewritten, bindingsBySlot.Where(pair => usedSlots.Contains(pair.Key)).OrderBy(pair => pair.Key).Select(pair => new UnitMaterialBinding(pair.Key, pair.Value)).ToArray(), []);
	}

	private static uint AllocateSlot(MaterialIdentity identity, IReadOnlySet<uint> occupied)
	{
		var candidate = StableHash(identity);
		if (candidate == 0 || candidate == DefaultMaterialSlotId) candidate = 1;
		while (occupied.Contains(candidate) || candidate == DefaultMaterialSlotId)
			candidate = candidate == uint.MaxValue ? 1 : candidate + 1;
		return candidate;
	}

	private static uint StableHash(MaterialIdentity identity)
	{
		ulong hash = 14695981039346656037UL;
		void Mix(ulong value)
		{
			for (var index = 0; index < sizeof(ulong); index++)
			{
				hash ^= (byte)(value >> (index * 8));
				hash *= 1099511628211UL;
			}
		}
		Mix(identity.SourceUnitFileId); Mix(identity.SourceSlotId); Mix(identity.PreferredTargetSlotId); Mix(identity.MaterialId);
		return unchecked((uint)(hash ^ (hash >> 32)));
	}

	private sealed record MaterialIdentity(ulong SourceUnitFileId, uint SourceSlotId, uint PreferredTargetSlotId, ulong MaterialId);
}

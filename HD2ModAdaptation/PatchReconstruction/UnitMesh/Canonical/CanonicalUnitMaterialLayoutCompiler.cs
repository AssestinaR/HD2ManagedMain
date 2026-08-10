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
	ulong MaterialId,
	bool UsesTargetUnitMaterialSlotLookup = false);

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
		var targetSlotsByMaterial = target.Materials
			.Where(binding => binding.MaterialId != 0)
			.GroupBy(binding => binding.MaterialId)
			.ToDictionary(group => group.Key, group => group.Select(binding => binding.SectionId).Distinct().ToArray());
		var occupiedSlots = new HashSet<uint>();
		var bindingsBySlot = new Dictionary<uint, ulong>();

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
			var expandedOccurrenceByMaterial = new Dictionary<ulong, int>();
			for (var index = 0; index < sections.Length; index++)
			{
				if (!claims.TryGetValue((mesh.MeshInfoIndex, index), out var sectionClaims)) continue;
				var claim = sectionClaims[0];
				var occurrence = 0;
				if (claim.UsesTargetUnitMaterialSlotLookup)
				{
					occurrence = expandedOccurrenceByMaterial.TryGetValue(claim.MaterialId, out var current) ? current : 0;
					expandedOccurrenceByMaterial[claim.MaterialId] = occurrence + 1;
				}
				var identity = new MaterialIdentity(target.NameHash, mesh.MeshInfoIndex, index, claim.MaterialId, occurrence, claim.PreferredTargetSlotId);
				var requestedSlot = claim.UsesTargetUnitMaterialSlotLookup
					? ResolveSdkTargetSlot(targetSlotsByMaterial, claim.MaterialId, occurrence, identity, occupiedSlots)
					: claim.PreferredTargetSlotId;
				var outputSlot = ResolveOutputSlot(requestedSlot, claim.MaterialId, identity, occupiedSlots, bindingsBySlot, targetBindings);
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

	private static uint ResolveSdkTargetSlot(
		IReadOnlyDictionary<ulong, uint[]> targetSlotsByMaterial,
		ulong materialId,
		int occurrence,
		MaterialIdentity identity,
		IReadOnlySet<uint> occupied)
		=> targetSlotsByMaterial.TryGetValue(materialId, out var slots) && occurrence < slots.Length
			? slots[occurrence]
			: AllocateSlot(identity, occupied);

	private static uint ResolveOutputSlot(
		uint requestedSlot,
		ulong materialId,
		MaterialIdentity identity,
		HashSet<uint> occupied,
		IDictionary<uint, ulong> bindingsBySlot,
		IReadOnlyDictionary<uint, ulong[]> targetBindings)
	{
		if (bindingsBySlot.TryGetValue(requestedSlot, out var existing))
			return existing == materialId
				? requestedSlot
				: ReserveNewSlot(AllocateSlot(identity, occupied), materialId, occupied, bindingsBySlot);
		if (targetBindings.TryGetValue(requestedSlot, out var targetMaterialIds) && targetMaterialIds.Length == 1 && targetMaterialIds[0] == materialId)
		{
			occupied.Add(requestedSlot);
			bindingsBySlot[requestedSlot] = materialId;
			return requestedSlot;
		}
		if (occupied.Contains(requestedSlot))
			return ReserveNewSlot(AllocateSlot(identity, occupied), materialId, occupied, bindingsBySlot);
		occupied.Add(requestedSlot);
		bindingsBySlot[requestedSlot] = materialId;
		return requestedSlot;
	}

	private static uint ReserveNewSlot(uint slot, ulong materialId, ISet<uint> occupied, IDictionary<uint, ulong> bindingsBySlot)
	{
		occupied.Add(slot);
		bindingsBySlot[slot] = materialId;
		return slot;
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
		Mix(identity.TargetUnitNameHash);
		Mix(unchecked((uint)identity.MeshInfoIndex));
		Mix(unchecked((uint)identity.SectionIndex));
		Mix(identity.MaterialId);
		Mix(unchecked((uint)identity.MaterialOccurrence));
		Mix(identity.FallbackSlotId);
		return unchecked((uint)(hash ^ (hash >> 32)));
	}

	private sealed record MaterialIdentity(ulong TargetUnitNameHash, int MeshInfoIndex, int SectionIndex, ulong MaterialId, int MaterialOccurrence, uint FallbackSlotId);
}

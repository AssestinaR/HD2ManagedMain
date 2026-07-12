using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Processing;

// Purpose: Maps material slots from source mesh to target mesh, resolving material asset bindings.
// Extracted from StrictUnitMeshTransfer.MaterialSlotMap for reusability.
public sealed class MaterialMapper
{
	private readonly Dictionary<uint, MaterialSlotReplacement> sourceSlotLookup;
	private readonly Dictionary<uint, MaterialSlotReplacement> targetSlotLookup;

	public MaterialMapper(IReadOnlyList<MaterialSlotReplacement> replacements, IReadOnlyList<uint> outputSlots)
	{
		Replacements = replacements;
		OutputSlots = outputSlots;
		sourceSlotLookup = replacements.ToDictionary(replacement => replacement.SourceSlotId);
		targetSlotLookup = replacements.ToDictionary(replacement => replacement.TargetSlotId);
	}

	public IReadOnlyList<MaterialSlotReplacement> Replacements { get; }

	public IReadOnlyList<uint> OutputSlots { get; }

	/// <summary>
	/// Attempts to map a source material slot to a target material slot.
	/// </summary>
	/// <param name="sourceSlotId">Source material slot ID</param>
	/// <param name="materialIndex">Target material index</param>
	/// <param name="materialSlotId">Target material slot ID</param>
	/// <returns>True if mapping was found, false otherwise</returns>
	public bool TryMap(uint sourceSlotId, out uint materialIndex, out uint materialSlotId)
	{
		if (sourceSlotLookup.TryGetValue(sourceSlotId, out var replacement))
		{
			materialIndex = replacement.TargetMaterialIndex;
			materialSlotId = replacement.TargetSlotId;
			return true;
		}

		materialIndex = 0;
		materialSlotId = 0;
		return false;
	}

	/// <summary>
	/// Attempts to find a replacement for a target slot ID.
	/// </summary>
	public bool TryReplaceTargetBinding(uint targetSlotId, out MaterialSlotReplacement replacement)
		=> targetSlotLookup.TryGetValue(targetSlotId, out replacement!);

	/// <summary>
	/// Creates a MaterialMapper by analyzing source and target meshes.
	/// </summary>
	public static MaterialMapper Create(
		UnitMeshModel targetModel,
		UnitRawMeshData targetRawMesh,
		UnitMeshModel sourceModel,
		UnitRawMeshData sourceRawMesh,
		int sourceMeshInfoIndex)
	{
		var targetMesh = FindMeshInfo(targetModel, targetRawMesh.MeshInfoIndex, "target");
		var sourceMesh = FindMeshInfo(sourceModel, sourceMeshInfoIndex, "source");

		if (sourceRawMesh.Sections.Count == 0 || targetMesh.MaterialSlotIds.Count == 0)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because source or target material sections are empty.");
		}

		if (sourceRawMesh.Sections.Any(section => section.MaterialIndex >= sourceMesh.MaterialSlotIds.Count))
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because a source section material index is outside its mesh slot table.");
		}

		var sourceSlots = targetMesh.MaterialSlotIds.Count >= sourceMesh.MaterialSlotIds.Count
			? sourceMesh.MaterialSlotIds.ToArray()
			: sourceRawMesh.Sections.Select(section => section.MaterialSlotId).Distinct().ToArray();

		var sourceBindings = sourceModel.Materials
			.Where(binding => sourceSlots.Contains(binding.SectionId))
			.GroupBy(binding => binding.SectionId)
			.ToDictionary(group => group.Key, group => group.Select(binding => binding.MaterialId).Distinct().ToArray());

		if (sourceSlots.Any(slot => !sourceBindings.TryGetValue(slot, out var materialIds) || materialIds.Length != 1))
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because a source material slot does not resolve to exactly one Material asset.");
		}

		var materialSlots = BuildMaterialSlots(
			targetModel,
			targetMesh,
			sourceSlots,
			sourceBindings.ToDictionary(pair => pair.Key, pair => pair.Value[0]));

		var replacements = new List<MaterialSlotReplacement>(sourceSlots.Length);
		for (var index = 0; index < sourceSlots.Length; index++)
		{
			var sourceSlot = sourceSlots[index];
			var targetSlot = materialSlots.SourceTargetSlots[index];
			var sourceMaterialId = sourceBindings[sourceSlot][0];
			var sourceMaterialIndex = checked((uint)IndexOf(sourceMesh.MaterialSlotIds, sourceSlot));
			var targetMaterialIndex = checked((uint)IndexOf(materialSlots.OutputSlots, targetSlot));
			replacements.Add(new MaterialSlotReplacement(targetSlot, sourceSlot, sourceMaterialId, sourceMaterialIndex, targetMaterialIndex));
		}

		if (replacements.Select(item => item.TargetSlotId).Distinct().Count() != replacements.Count)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because material slot mapping is ambiguous.");
		}

		return new MaterialMapper(replacements, materialSlots.OutputSlots);
	}

	private static MaterialSlots BuildMaterialSlots(
		UnitMeshModel targetModel,
		UnitMeshInfo targetMesh,
		IReadOnlyList<uint> sourceSlots,
		IReadOnlyDictionary<uint, ulong> sourceBindings)
	{
		var outputSlots = targetMesh.MaterialSlotIds.ToList();
		var targetBindings = targetModel.Materials
			.GroupBy(binding => binding.SectionId)
			.Where(group => group.Select(binding => binding.MaterialId).Distinct().Count() == 1)
			.ToDictionary(group => group.Key, group => group.First().MaterialId);

		var sourceTargetSlots = new List<uint>(sourceSlots.Count);
		var usedTargetSlots = new HashSet<uint>();

		foreach (var sourceSlot in sourceSlots)
		{
			var sourceMaterialId = sourceBindings[sourceSlot];
			var matchingSlot = outputSlots.Cast<uint?>().FirstOrDefault(slot =>
				slot is not null &&
				!usedTargetSlots.Contains(slot.Value) &&
				targetBindings.TryGetValue(slot.Value, out var materialId) &&
				materialId == sourceMaterialId);

			if (matchingSlot is not null)
			{
				sourceTargetSlots.Add(matchingSlot.Value);
				usedTargetSlots.Add(matchingSlot.Value);
				continue;
			}

			var reusableSlot = outputSlots.Cast<uint?>().FirstOrDefault(slot =>
				slot is not null && !usedTargetSlots.Contains(slot.Value));

			if (reusableSlot is not null)
			{
				sourceTargetSlots.Add(reusableSlot.Value);
				usedTargetSlots.Add(reusableSlot.Value);
				continue;
			}

			var addedSlot = FindNextAvailableSlot(targetModel, outputSlots);
			outputSlots.Add(addedSlot);
			targetBindings[addedSlot] = sourceMaterialId;
			sourceTargetSlots.Add(addedSlot);
			usedTargetSlots.Add(addedSlot);
		}

		return new MaterialSlots(sourceTargetSlots, outputSlots);
	}

	private static uint FindNextAvailableSlot(UnitMeshModel targetModel, IReadOnlyCollection<uint> localSlots)
	{
		var usedSlots = targetModel.Meshes.SelectMany(mesh => mesh.MaterialSlotIds)
			.Concat(targetModel.RawMeshData.SelectMany(mesh => mesh.Sections.Select(section => section.MaterialSlotId)))
			.Concat(targetModel.Materials.Select(binding => binding.SectionId))
			.Concat(localSlots)
			.ToHashSet();

		var nextSlot = 0u;
		while (usedSlots.Contains(nextSlot))
		{
			nextSlot++;
		}

		return nextSlot;
	}

	private static int IndexOf(IReadOnlyList<uint> values, uint value)
	{
		for (var index = 0; index < values.Count; index++)
		{
			if (values[index] == value)
			{
				return index;
			}
		}

		throw new InvalidDataException("Cannot transfer Unit mesh because a material slot is missing from its slot table.");
	}

	private static UnitMeshInfo FindMeshInfo(UnitMeshModel model, int meshInfoIndex, string role)
	{
		if (meshInfoIndex < 0 || meshInfoIndex >= model.Meshes.Count)
		{
			throw new InvalidDataException($"The {role} Unit does not contain MeshInfo {meshInfoIndex}.");
		}

		return model.Meshes[meshInfoIndex];
	}
}

public sealed record MaterialSlotReplacement(
	uint TargetSlotId,
	uint SourceSlotId,
	ulong SourceMaterialId,
	uint SourceMaterialIndex,
	uint TargetMaterialIndex);

internal sealed record MaterialSlots(
	IReadOnlyList<uint> SourceTargetSlots,
	IReadOnlyList<uint> OutputSlots);

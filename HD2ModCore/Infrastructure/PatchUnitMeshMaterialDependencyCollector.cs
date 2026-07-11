using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：从 Unit mesh 替换编辑中收集实际被替换 section 使用的 material id。
// Purpose: Collects material IDs used by actually replaced Unit mesh sections.
public static class PatchUnitMeshMaterialDependencyCollector
{
	public static IReadOnlySet<ulong> CollectReplacementMaterialIds(IReadOnlyCollection<PatchUnitMeshEditResult> edits)
	{
		ArgumentNullException.ThrowIfNull(edits);

		var materialIds = new HashSet<ulong>();
		foreach (var edit in edits)
		{
			foreach (var meshInfoIndex in GetReplacementMeshInfoIndexes(edit))
			{
				CollectReplacementMaterialIds(edit.EditedModel, meshInfoIndex, materialIds);
			}
		}

		return materialIds;
	}

	private static IEnumerable<int> GetReplacementMeshInfoIndexes(PatchUnitMeshEditResult edit)
		=> edit.AdaptationSteps?
			.Where(step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource)
			.Select(step => step.TargetMeshInfoIndex)
			.Distinct()
			?? Enumerable.Empty<int>();

	private static void CollectReplacementMaterialIds(UnitMeshModel model, int meshInfoIndex, ISet<ulong> materialIds)
	{
		var slotIds = model.RawMeshData
			.Where(rawMesh => rawMesh.MeshInfoIndex == meshInfoIndex)
			.SelectMany(rawMesh => rawMesh.Sections.Select(section => section.MaterialSlotId))
			.ToHashSet();
		if (slotIds.Count == 0)
		{
			return;
		}

		foreach (var binding in model.Materials)
		{
			if (slotIds.Contains(binding.SectionId))
			{
				materialIds.Add(binding.MaterialId);
			}
		}
	}
}
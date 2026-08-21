using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：把 GameData 的逻辑部件标签与来源 Patch 的实际 Mesh 几何重新绑定。
// Purpose: Rebinds GameData logical part labels to the source Patch's actual Mesh geometry.
internal static class EquipmentUnitSourcePartBinder
{
	public static IReadOnlyList<EquipmentUnitCatalogEntry> Bind(
		IReadOnlyList<EquipmentUnitCatalogEntry> candidates,
		IEnumerable<ModSourceUnitFacts> sourceUnits)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		ArgumentNullException.ThrowIfNull(sourceUnits);

		var candidateKeys = candidates.SelectMany(entry => entry.Parts).Select(part => part.UnitAssetKey).ToHashSet();
		var transferableMeshes = new HashSet<(AssetKey Unit, int Mesh)>();
		var currentMeshes = new Dictionary<(AssetKey Unit, int Mesh), ModSourceUnitMeshFact>();
		var currentVisualLod0Meshes = new Dictionary<AssetKey, List<ModSourceUnitMeshFact>>();
		var cullingMeshes = new Dictionary<(AssetKey Unit, int Mesh), ModSourceUnitMeshFact>();
		var ambiguousMeshIds = new HashSet<(AssetKey Unit, int Mesh)>();

		foreach (var unit in sourceUnits)
		{
			var unitKey = unit.UnitAssetKey;
			if (!candidateKeys.Contains(unitKey) || !unit.IsReadable || unit.IsHidden)
				continue;

			foreach (var mesh in unit.Meshes.Where(mesh => mesh.IsTransferable))
			{
				if (mesh.IsCullingMesh && mesh.LodIndex == -1)
				{
					cullingMeshes[(unitKey, mesh.MeshInfoIndex)] = mesh;
					continue;
				}
				if (mesh.LodIndex == 0 && mesh.IsVisualMesh)
				{
					if (!currentVisualLod0Meshes.TryGetValue(unitKey, out var currentVisualMeshes))
					{
						currentVisualMeshes = [];
						currentVisualLod0Meshes.Add(unitKey, currentVisualMeshes);
					}
					if (!currentVisualMeshes.Any(current => current.MeshInfoIndex == mesh.MeshInfoIndex && current.MeshId == mesh.MeshId))
						currentVisualMeshes.Add(mesh);
				}

				var key = (unitKey, mesh.MeshInfoIndex);
				if (currentMeshes.TryGetValue(key, out var existingMesh) && existingMesh.MeshId != mesh.MeshId)
				{
					ambiguousMeshIds.Add(key);
					transferableMeshes.Remove(key);
					continue;
				}
				if (!ambiguousMeshIds.Contains(key))
				{
					currentMeshes[key] = mesh;
					transferableMeshes.Add(key);
				}
			}
		}

		return candidates
			.Select(entry => entry with
			{
				Parts = entry.Parts
					.Where(part => transferableMeshes.Contains((part.UnitAssetKey, part.MeshInfoIndex)))
					.Select(part => ApplyCurrentGeometry(part, currentMeshes[(part.UnitAssetKey, part.MeshInfoIndex)]))
					.Concat(RebindSingleReindexedVisualLod0(entry, transferableMeshes, currentVisualLod0Meshes))
					.Concat(cullingMeshes
						.Where(item => item.Key.Unit == entry.Parts.FirstOrDefault()?.UnitAssetKey
							&& entry.Parts.Any(part => CullingMatchesPart(item.Value, part)))
						.Select(item =>
						{
							var template = entry.Parts.First(part => CullingMatchesPart(item.Value, part));
							return ApplyCurrentGeometry(template, item.Value) with
							{
								MeshInfoIndex = item.Key.Mesh,
								SemanticName = item.Value.SemanticName,
								PieceType = item.Value.PieceType,
								IsCullingMesh = true
							};
						}))
					.GroupBy(part => (part.UnitAssetKey, part.MeshInfoIndex))
					.Select(group => group.First())
					.ToArray()
			})
			.Where(entry => entry.Parts.Count != 0)
			.ToArray();
	}

	private static EquipmentUnitPart ApplyCurrentGeometry(EquipmentUnitPart part, ModSourceUnitMeshFact mesh)
		=> part with
		{
			MeshId = mesh.MeshId,
			VertexCount = mesh.VertexCount,
			TriangleCount = mesh.TriangleCount,
			GeometryQuality = mesh.GeometryQuality
		};

	private static bool CullingMatchesPart(ModSourceUnitMeshFact culling, EquipmentUnitPart part)
		=> string.Equals(culling.Slot, part.PartKind.ToString(), StringComparison.OrdinalIgnoreCase)
			&& (string.IsNullOrWhiteSpace(culling.PieceType)
				|| string.IsNullOrWhiteSpace(part.PieceType)
				|| string.Equals(culling.PieceType, part.PieceType, StringComparison.OrdinalIgnoreCase));

	private static IEnumerable<EquipmentUnitPart> RebindSingleReindexedVisualLod0(
		EquipmentUnitCatalogEntry entry,
		IReadOnlySet<(AssetKey Unit, int Mesh)> transferableMeshes,
		IReadOnlyDictionary<AssetKey, List<ModSourceUnitMeshFact>> currentVisualLod0Meshes)
	{
		return entry.Parts
			.GroupBy(part => part.UnitAssetKey)
			.Where(group => !group.Any(part => transferableMeshes.Contains((part.UnitAssetKey, part.MeshInfoIndex))))
			.Where(group => group.Count() == 1)
			.Where(group => currentVisualLod0Meshes.TryGetValue(group.Key, out var sourceMeshes) && sourceMeshes.Count == 1)
			.Select(group =>
			{
				var source = currentVisualLod0Meshes[group.Key][0];
				return ApplyCurrentGeometry(group.First(), source) with { MeshInfoIndex = source.MeshInfoIndex };
			});
	}
}

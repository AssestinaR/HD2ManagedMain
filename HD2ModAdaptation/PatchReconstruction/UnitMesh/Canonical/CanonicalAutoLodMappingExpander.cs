namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Expands one approved representative mapping into the target Unit's compatible LOD shells.
public static class CanonicalAutoLodMappingExpander
{
	public static IReadOnlyList<CanonicalReplacementMapping> Expand(
		UnitMeshModel targetModel,
		IReadOnlyDictionary<AssetKey, UnitMeshModel> sourceModels,
		IReadOnlyList<CanonicalReplacementMapping> approvedMappings)
	{
		ArgumentNullException.ThrowIfNull(targetModel);
		ArgumentNullException.ThrowIfNull(sourceModels);
		ArgumentNullException.ThrowIfNull(approvedMappings);

		var expandedMappings = new List<CanonicalReplacementMapping>();
		foreach (var approved in approvedMappings)
		{
			if (!sourceModels.TryGetValue(approved.Source.UnitKey, out var sourceModel))
				throw new InvalidDataException($"Canonical plan source Unit 0x{approved.Source.UnitKey.FileId:x16} is not loaded.");
			var sourceRepresentative = FindRaw(sourceModel, approved.Source.MeshInfoIndex, "Source");
			_ = FindRaw(targetModel, approved.Target.MeshInfoIndex, "Target");
			var sourceRepresentativeSemantic = FindSemantic(sourceModel, sourceRepresentative);
			if (sourceRepresentative.LodIndex == -1)
			{
				var sourceCullings = sourceModel.RawMeshData
					.Where(raw => raw.LodIndex == -1 && CountTriangles(raw) > 1 && raw.Vertices.Count > 3)
					.Where(raw => SemanticMatches(FindSemantic(sourceModel, raw), sourceRepresentativeSemantic))
					.ToArray();
				foreach (var sourceCutoutMesh in sourceCullings)
				{
					var targetCulling = targetModel.RawMeshData
						.Where(raw => raw.LodIndex == -1 && CountTriangles(raw) > 1 && raw.Vertices.Count > 3)
						.SingleOrDefault(raw => raw.MeshId == sourceCutoutMesh.MeshId);
					if (targetCulling is null)
						throw new InvalidDataException($"Target Unit 0x{approved.Target.UnitKey.FileId:x16} has no compatible culling mesh for source MeshId 0x{sourceCutoutMesh.MeshId:x8}.");
					expandedMappings.Add(new CanonicalReplacementMapping(
						new(approved.Source.UnitKey, sourceCutoutMesh.MeshInfoIndex),
						new(approved.Target.UnitKey, targetCulling.MeshInfoIndex),
						approved.SourceMeshState,
						approved.SkinningMode,
						approved.BoneAnchor));
				}
				continue;
			}
			var sourceLod0 = sourceModel.RawMeshData
				.Where(IsVisibleLod0)
				.Where(raw => SemanticMatches(FindSemantic(sourceModel, raw), sourceRepresentativeSemantic))
				.OrderByDescending(CountTriangles)
				.ThenByDescending(raw => raw.Vertices.Count)
				.FirstOrDefault()
				?? throw new InvalidDataException($"Source Unit 0x{approved.Source.UnitKey.FileId:x16} has no real LOD0 mesh.");
			var sourceCulling = sourceModel.RawMeshData
				.Where(raw => raw.LodIndex == -1 && CountTriangles(raw) > 1 && raw.Vertices.Count > 3)
				.Where(raw => SemanticMatches(FindSemantic(sourceModel, raw), sourceRepresentativeSemantic))
				.OrderByDescending(CountTriangles)
				.ThenByDescending(raw => raw.Vertices.Count)
				.FirstOrDefault();

			foreach (var targetLodSlot in targetModel.RawMeshData.Where(IsTargetLodSlot).OrderBy(raw => raw.LodIndex == -1 ? 0 : 1).ThenBy(raw => raw.LodIndex).ThenBy(raw => raw.MeshInfoIndex))
			{
				// A target culling mesh is not an ordinary visual LOD. Never fall back
				// to source LOD0 here: that duplicates visible geometry into the culling
				// stream when SDK exports describe culling with a different LOD marker.
				if (targetLodSlot.LodIndex == -1 && sourceCulling is null)
					continue;
				var sourceMesh = targetLodSlot.LodIndex == -1 ? sourceCulling! : sourceLod0;
				expandedMappings.Add(new CanonicalReplacementMapping(
					new(approved.Source.UnitKey, sourceMesh.MeshInfoIndex),
					new(approved.Target.UnitKey, targetLodSlot.MeshInfoIndex),
					approved.SourceMeshState,
					approved.SkinningMode,
					approved.BoneAnchor));
			}
		}

		return expandedMappings
			.GroupBy(mapping => (mapping.Target.UnitKey, mapping.Target.MeshInfoIndex))
			.Select(group => group.First())
			.ToArray();
	}

	private static UnitRawMeshData FindRaw(UnitMeshModel model, int meshInfoIndex, string role)
		=> model.RawMeshData.SingleOrDefault(raw => raw.MeshInfoIndex == meshInfoIndex)
			?? throw new InvalidDataException($"{role} RawMesh {meshInfoIndex} is unavailable for Auto-LOD expansion.");

	private static UnitMeshSemanticInfo? FindSemantic(UnitMeshModel model, UnitRawMeshData raw)
		=> model.Meshes.FirstOrDefault(mesh => mesh.Index == raw.MeshInfoIndex)?.SemanticInfo;

	private static bool IsVisibleLod0(UnitRawMeshData raw)
		=> raw.LodIndex == 0 && CountTriangles(raw) > 1 && raw.Vertices.Count > 3;

	private static bool IsTargetLodSlot(UnitRawMeshData raw)
		=> raw.LodIndex >= -1 && CountTriangles(raw) > 1 && raw.Vertices.Count > 3;

	private static int CountTriangles(UnitRawMeshData raw)
		=> raw.Triangles.Count != 0 ? raw.Triangles.Count : raw.Sections.Sum(section => section.Triangles.Count);

	private static bool SemanticMatches(UnitMeshSemanticInfo? candidate, UnitMeshSemanticInfo? representative)
		=> candidate is null || representative is null ||
			(string.Equals(candidate.Slot, representative.Slot, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(candidate.PieceType, representative.PieceType, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(candidate.BodyType, representative.BodyType, StringComparison.OrdinalIgnoreCase));
}

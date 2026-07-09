using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：基于 mesh id、LOD、material slot 与 stream 结构选择实验性 RawMesh 替换候选。
// Purpose: Selects experimental RawMesh replacement candidates using mesh id, LOD, material slots, and stream structure.
public sealed class UnitMeshReplacementStrategy : IUnitMeshReplacementStrategy
{
	private const double ExperimentalFallbackMaxGeometryRatio = 3.0;
	private const double ExperimentalFallbackMinGeometryRatio = 1.0 / ExperimentalFallbackMaxGeometryRatio;

	private readonly bool allowExperimentalFallback;

	public UnitMeshReplacementStrategy(bool allowExperimentalFallback = false)
	{
		this.allowExperimentalFallback = allowExperimentalFallback;
	}

	public IReadOnlyList<UnitMeshReplacementCandidate> FindCandidates(UnitMeshModel targetModel, UnitMeshModel sourceModel)
	{
		ArgumentNullException.ThrowIfNull(targetModel);
		ArgumentNullException.ThrowIfNull(sourceModel);

		var candidates = new List<UnitMeshReplacementCandidate>();
		foreach (var targetRawMesh in targetModel.RawMeshData)
		{
			var targetSemantic = FindSemanticInfo(targetModel, targetRawMesh);
			if (IsRejectedVisualReplacementSlot(targetSemantic))
			{
				continue;
			}

			var targetStream = FindStream(targetModel, targetRawMesh);
			if (targetStream is null)
			{
				continue;
			}

			foreach (var sourceRawMesh in sourceModel.RawMeshData)
			{
				var sourceSemantic = FindSemanticInfo(sourceModel, sourceRawMesh);
				if (IsRejectedVisualReplacementSlot(sourceSemantic))
				{
					continue;
				}

				var sourceStream = FindStream(sourceModel, sourceRawMesh);
				if (sourceStream is null)
				{
					continue;
				}

				var sameStreamLayout = HasSameStreamLayout(targetStream, sourceStream);
				var sameMeshId = targetRawMesh.MeshId == sourceRawMesh.MeshId;
				if (!sameStreamLayout && !allowExperimentalFallback)
				{
					continue;
				}
				if (!sameStreamLayout && !sameMeshId && !HasCompatibleExperimentalGeometryScale(targetRawMesh, sourceRawMesh))
				{
					continue;
				}

				var sameVertexStride = targetStream.VertexStride == sourceStream.VertexStride;
				var sameLod = targetRawMesh.LodIndex == sourceRawMesh.LodIndex;
				var sameMaterialSlots = HasSameMaterialSlots(targetRawMesh, sourceRawMesh);
				if (IsTinyOrEmpty(sourceRawMesh, sourceSemantic))
				{
					continue;
				}

				var semanticMatch = CalculateSemanticMatch(targetSemantic, sourceSemantic);
				if (targetSemantic.HasValue && sourceSemantic.HasValue && semanticMatch <= 0)
				{
					continue;
				}

				var kind = ResolveKind(sameStreamLayout, sameMeshId, sameLod, sameMaterialSlots);
				var score = CalculateScore(kind, targetRawMesh, sourceRawMesh, targetStream, sourceStream, sameStreamLayout, sameVertexStride, semanticMatch);
				candidates.Add(new UnitMeshReplacementCandidate(
					targetRawMesh.MeshInfoIndex,
					sourceRawMesh.MeshInfoIndex,
					targetRawMesh.MeshId,
					sourceRawMesh.MeshId,
					targetSemantic.Name,
					sourceSemantic.Name,
					targetRawMesh.LodIndex,
					targetRawMesh.StreamIndex,
					targetStream.VertexStride,
					BuildComponentLayout(targetStream),
					kind,
					score,
					BuildReason(kind, targetSemantic, sourceSemantic, semanticMatch)));
			}
		}

		return candidates
			.OrderByDescending(candidate => candidate.Score)
			.ThenBy(candidate => candidate.TargetMeshInfoIndex)
			.ThenBy(candidate => candidate.SourceMeshInfoIndex)
			.ToArray();
	}

	private static UnitStreamInfo? FindStream(UnitMeshModel model, UnitRawMeshData rawMesh)
		=> model.Streams.FirstOrDefault(stream => stream.Index == rawMesh.StreamIndex);

	private static UnitMeshSemanticInfo FindSemanticInfo(UnitMeshModel model, UnitRawMeshData rawMesh)
		=> model.Meshes.FirstOrDefault(mesh => mesh.Index == rawMesh.MeshInfoIndex)?.SemanticInfo
			?? UnitMeshSemanticInfo.Empty(rawMesh.LodIndex, rawMesh.MeshInfoIndex);

	private static bool IsTinyOrEmpty(UnitRawMeshData rawMesh, UnitMeshSemanticInfo semanticInfo)
		=> rawMesh.Triangles.Count == 0 || (semanticInfo.HasValue && rawMesh.Vertices.Count <= 10);

	private static bool IsRejectedVisualReplacementSlot(UnitMeshSemanticInfo semanticInfo)
		=> semanticInfo.IsCullingBody || semanticInfo.IsStaticMesh;

	private static bool HasSameStreamLayout(UnitStreamInfo targetStream, UnitStreamInfo sourceStream)
	{
		if (targetStream.VertexStride != sourceStream.VertexStride || targetStream.Components.Count != sourceStream.Components.Count)
		{
			return false;
		}

		for (var i = 0; i < targetStream.Components.Count; i++)
		{
			var target = targetStream.Components[i];
			var source = sourceStream.Components[i];
			if (target.Type != source.Type || target.Format != source.Format || target.Index != source.Index || target.Size != source.Size)
			{
				return false;
			}
		}

		return true;
	}

	private static bool HasSameMaterialSlots(UnitRawMeshData targetRawMesh, UnitRawMeshData sourceRawMesh)
	{
		if (targetRawMesh.Sections.Count != sourceRawMesh.Sections.Count)
		{
			return false;
		}

		for (var i = 0; i < targetRawMesh.Sections.Count; i++)
		{
			if (targetRawMesh.Sections[i].MaterialSlotId != sourceRawMesh.Sections[i].MaterialSlotId)
			{
				return false;
			}
		}

		return true;
	}

	private static bool HasCompatibleExperimentalGeometryScale(UnitRawMeshData targetRawMesh, UnitRawMeshData sourceRawMesh)
	{
		if (targetRawMesh.Vertices.Count == 0 || targetRawMesh.Triangles.Count == 0 || sourceRawMesh.Vertices.Count == 0 || sourceRawMesh.Triangles.Count == 0)
		{
			return false;
		}

		return IsWithinExperimentalRatio(sourceRawMesh.Vertices.Count, targetRawMesh.Vertices.Count)
			&& IsWithinExperimentalRatio(sourceRawMesh.Triangles.Count, targetRawMesh.Triangles.Count);
	}

	private static bool IsWithinExperimentalRatio(int sourceCount, int targetCount)
	{
		var ratio = (double)sourceCount / targetCount;
		return ratio >= ExperimentalFallbackMinGeometryRatio && ratio <= ExperimentalFallbackMaxGeometryRatio;
	}

	private static UnitMeshReplacementCandidateKind ResolveKind(bool sameStreamLayout, bool sameMeshId, bool sameLod, bool sameMaterialSlots)
	{
		if (!sameStreamLayout)
		{
			return UnitMeshReplacementCandidateKind.ExperimentalFallback;
		}
		if (sameMeshId)
		{
			return UnitMeshReplacementCandidateKind.SameMeshId;
		}
		if (sameLod && sameMaterialSlots)
		{
			return UnitMeshReplacementCandidateKind.SameLodAndMaterialSlots;
		}
		if (sameLod)
		{
			return UnitMeshReplacementCandidateKind.SameLod;
		}

		return UnitMeshReplacementCandidateKind.LayoutOnly;
	}

	private static int CalculateScore(
		UnitMeshReplacementCandidateKind kind,
		UnitRawMeshData targetRawMesh,
		UnitRawMeshData sourceRawMesh,
		UnitStreamInfo targetStream,
		UnitStreamInfo sourceStream,
		bool sameStreamLayout,
		bool sameVertexStride,
		int semanticMatch)
	{
		var score = kind switch
		{
			UnitMeshReplacementCandidateKind.SameMeshId => 400,
			UnitMeshReplacementCandidateKind.SameLodAndMaterialSlots => 300,
			UnitMeshReplacementCandidateKind.SameLod => 200,
			UnitMeshReplacementCandidateKind.LayoutOnly => 100,
			_ => 40,
		};
		if (sameStreamLayout)
		{
			score += 60;
		}
		else if (sameVertexStride)
		{
			score += 35;
		}
		else
		{
			var strideDistance = Math.Abs((int)targetStream.VertexStride - (int)sourceStream.VertexStride);
			score += Math.Max(0, 25 - Math.Min(25, strideDistance));
		}
		if (targetRawMesh.MeshInfoIndex == sourceRawMesh.MeshInfoIndex)
		{
			score += 45;
		}
		if (targetRawMesh.LodIndex == sourceRawMesh.LodIndex)
		{
			score += 40;
		}
		if (targetRawMesh.Sections.Count == sourceRawMesh.Sections.Count)
		{
			score += 20;
		}
		if (targetRawMesh.Vertices.Count == sourceRawMesh.Vertices.Count)
		{
			score += 10;
		}
		if (targetRawMesh.Triangles.Count == sourceRawMesh.Triangles.Count)
		{
			score += 10;
		}
		score += semanticMatch;

		return score;
	}

	private static int CalculateSemanticMatch(UnitMeshSemanticInfo target, UnitMeshSemanticInfo source)
	{
		if (!target.HasValue || !source.HasValue)
		{
			return 0;
		}

		if (IsRejectedVisualReplacementSlot(target) || IsRejectedVisualReplacementSlot(source) || target.IsVisualMesh != source.IsVisualMesh)
		{
			return -1;
		}

		if (!TextEquals(target.Slot, source.Slot) || !TextEquals(target.PieceType, source.PieceType))
		{
			return -1;
		}

		var score = 700;
		if (TextEquals(target.BodyType, source.BodyType))
		{
			score += 120;
		}
		else if (IsAnyBodyFallback(target.BodyType, source.BodyType))
		{
			score += 55;
		}
		else if (target.BodyType.Length > 0 && source.BodyType.Length > 0)
		{
			return -1;
		}

		if (TextEquals(target.Weight, source.Weight))
		{
			score += 30;
		}
		if (target.LodIndex == source.LodIndex)
		{
			score += 40;
		}

		return score;
	}

	private static bool TextEquals(string left, string right)
		=> string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	private static bool IsAnyBodyFallback(string left, string right)
		=> TextEquals(left, "Any") || TextEquals(right, "Any");

	private static IReadOnlyList<UnitMeshReplacementComponentSignature> BuildComponentLayout(UnitStreamInfo stream)
		=> stream.Components.Select(component => new UnitMeshReplacementComponentSignature(
			component.Type,
			component.Format,
			component.Index,
			component.Size)).ToArray();

	private static string BuildReason(UnitMeshReplacementCandidateKind kind, UnitMeshSemanticInfo targetSemantic, UnitMeshSemanticInfo sourceSemantic, int semanticMatch)
	{
		var structuralReason = kind switch
		{
			UnitMeshReplacementCandidateKind.SameMeshId => "Same mesh id and compatible stream layout.",
			UnitMeshReplacementCandidateKind.SameLodAndMaterialSlots => "Same LOD, same material slots, and compatible stream layout.",
			UnitMeshReplacementCandidateKind.SameLod => "Same LOD and compatible stream layout.",
			UnitMeshReplacementCandidateKind.ExperimentalFallback => "Experimental fallback candidate; stream layout may differ and vertex data will be normalized to the target stride.",
			_ => "Compatible stream layout only.",
		};
		if (semanticMatch <= 0 || !targetSemantic.HasValue || !sourceSemantic.HasValue)
		{
			return structuralReason;
		}

		var role = sourceSemantic.IsLod ? " lod" : string.Empty;
		return $"Semantic part match{role} {sourceSemantic.Name} -> {targetSemantic.Name}. {structuralReason}";
	}
}

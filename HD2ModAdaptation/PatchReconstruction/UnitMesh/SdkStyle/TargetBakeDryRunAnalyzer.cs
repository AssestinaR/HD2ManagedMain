namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Dry-runs canonical-rig target bone palette and section-remap construction without modifying a Unit or writing a Patch.
public sealed class TargetBakeDryRunAnalyzer
{
	private const float ActiveWeightThreshold = 0.001f;

	public TargetBakeDryRunDiagnostic Analyze(
		UnitMeshModel targetModel,
		int targetMeshInfoIndex,
		UnitMeshModel sourceModel,
		int sourceMeshInfoIndex,
		SdkStyleAvatarRigResource canonicalRig)
	{
		var targetMesh = FindRawMesh(targetModel, targetMeshInfoIndex, "target");
		var sourceMesh = FindRawMesh(sourceModel, sourceMeshInfoIndex, "source");
		var skinningLayoutIssue = FindSourceSkinningIssue(sourceModel, sourceMesh);
		if (skinningLayoutIssue is not null)
		{
			return new TargetBakeDryRunDiagnostic(targetMeshInfoIndex, sourceMeshInfoIndex, 0, targetMesh.Sections.Count, Array.Empty<uint>(), Array.Empty<TargetBakeSectionRemapDiagnostic>(), "TargetBakeSkinningLayoutBlocked", skinningLayoutIssue);
		}
		var effectiveSections = sourceMesh.Sections.Where(section => section.Triangles.Count != 0).ToArray();
		if (effectiveSections.Length == 0)
		{
			return new TargetBakeDryRunDiagnostic(targetMeshInfoIndex, sourceMeshInfoIndex, 0, 0, Array.Empty<uint>(), Array.Empty<TargetBakeSectionRemapDiagnostic>(), "NoSourceGeometry", "来源 mesh 没有有效三角形。");
		}
		if (effectiveSections.Length > targetMesh.Sections.Count)
		{
			return new TargetBakeDryRunDiagnostic(targetMeshInfoIndex, sourceMeshInfoIndex, effectiveSections.Length, targetMesh.Sections.Count, Array.Empty<uint>(), Array.Empty<TargetBakeSectionRemapDiagnostic>(), "TargetBakeMaterialLayoutBlocked", "来源有效 material section 数不能无损投影到当前目标 section 布局。");
		}

		var sourceBoneInfo = FindBoneInfo(sourceModel, sourceMesh, "source");
		var sectionHashes = effectiveSections.Select(section => CollectActiveBoneHashes(section, sourceMesh, sourceBoneInfo, sourceModel.TransformNameHashes)).ToArray();
		var allHashes = sectionHashes.SelectMany(hashes => hashes).Distinct().ToArray();
		var canonicalOrder = canonicalRig.TransformInfo.NameHashes
			.Select((hash, index) => new { hash, index })
			.Where(item => allHashes.Contains(item.hash))
			.OrderBy(item => item.index)
			.ToArray();
		if (canonicalOrder.Length != allHashes.Length)
		{
			return new TargetBakeDryRunDiagnostic(targetMeshInfoIndex, sourceMeshInfoIndex, effectiveSections.Length, targetMesh.Sections.Count, Array.Empty<uint>(), Array.Empty<TargetBakeSectionRemapDiagnostic>(), "MissingCanonicalBones", "至少一个活跃来源骨骼不存在于 canonical Avatar Rig。");
		}
		var palette = new List<uint>(canonicalOrder.Length);
		foreach (var item in canonicalOrder)
		{
			var targetIndex = IndexOf(targetModel.TransformNameHashes, item.hash);
			if (targetIndex < 0)
			{
				return new TargetBakeDryRunDiagnostic(targetMeshInfoIndex, sourceMeshInfoIndex, effectiveSections.Length, targetMesh.Sections.Count, palette, Array.Empty<TargetBakeSectionRemapDiagnostic>(), "NeedsTargetTransformExpansion", "至少一个活跃 canonical 骨骼不存在于目标 TransformInfo。");
			}
			palette.Add(checked((uint)targetIndex));
		}

		var remaps = new List<TargetBakeSectionRemapDiagnostic>(effectiveSections.Length);
		for (var index = 0; index < effectiveSections.Length; index++)
		{
			var hashes = sectionHashes[index];
			var fakeIndices = hashes.Select(hash =>
			{
				var targetIndex = checked((uint)IndexOf(targetModel.TransformNameHashes, hash));
				return checked((uint)palette.IndexOf(targetIndex));
			}).ToArray();
			if (fakeIndices.Any(fakeIndex => fakeIndex == uint.MaxValue))
			{
				return new TargetBakeDryRunDiagnostic(targetMeshInfoIndex, sourceMeshInfoIndex, effectiveSections.Length, targetMesh.Sections.Count, palette, remaps, "TargetPaletteConstructionFailed", "目标 palette 未包含某个 section 的活跃骨骼。");
			}
			remaps.Add(new TargetBakeSectionRemapDiagnostic(index, checked((int)targetMesh.Sections[index].MaterialIndex), hashes, fakeIndices));
		}
		return new TargetBakeDryRunDiagnostic(targetMeshInfoIndex, sourceMeshInfoIndex, effectiveSections.Length, targetMesh.Sections.Count, palette, remaps, "TargetBakeDryRunReady", null);
	}

	private static IReadOnlyList<uint> CollectActiveBoneHashes(UnitRawMeshSectionData section, UnitRawMeshData mesh, UnitBoneInfo boneInfo, IReadOnlyList<uint> transformHashes)
	{
		var result = new HashSet<uint>();
		if (section.MaterialIndex >= boneInfo.Remaps.Count) return Array.Empty<uint>();
		var remap = boneInfo.Remaps[(int)section.MaterialIndex];
		foreach (var vertexIndex in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct())
		{
			if (vertexIndex >= mesh.Vertices.Count) continue;
			var vertex = mesh.Vertices[(int)vertexIndex];
			var indices = vertex.Components.FirstOrDefault(component => component.Type == 6)?.UIntValues ?? Array.Empty<uint>();
			var weights = vertex.Components.FirstOrDefault(component => component.Type == 7)?.FloatValues ?? Array.Empty<float>();
			for (var index = 0; index < Math.Min(indices.Length, weights.Length); index++)
			{
				if (weights[index] <= ActiveWeightThreshold || indices[index] >= remap.FakeIndices.Count) continue;
				var realPosition = remap.FakeIndices[(int)indices[index]];
				if (realPosition >= boneInfo.RealIndices.Count) continue;
				var transformIndex = boneInfo.RealIndices[(int)realPosition];
				if (transformIndex < transformHashes.Count) result.Add(transformHashes[(int)transformIndex]);
			}
		}
		return result.OrderBy(hash => hash).ToArray();
	}

	private static UnitRawMeshData FindRawMesh(UnitMeshModel model, int meshInfoIndex, string role)
		=> model.RawMeshData.FirstOrDefault(mesh => mesh.MeshInfoIndex == meshInfoIndex)
			?? throw new KeyNotFoundException($"The {role} Unit does not contain mesh {meshInfoIndex}.");

	private static string? FindSourceSkinningIssue(UnitMeshModel sourceModel, UnitRawMeshData sourceMesh)
	{
		var sourceStream = sourceModel.Streams.FirstOrDefault(stream => stream.Index == sourceMesh.StreamIndex)
			?? throw new KeyNotFoundException($"The source Unit does not contain stream {sourceMesh.StreamIndex}.");
		var sourceIndices = sourceStream.Components.Where(component => component.Type == 6).OrderBy(component => component.Index).ToArray();
		var sourceWeights = sourceStream.Components.Where(component => component.Type == 7).OrderBy(component => component.Index).ToArray();
		if (sourceIndices.Length == 0 && sourceWeights.Length == 0) return null;
		if (sourceIndices.Length == 0 || sourceWeights.Length == 0) return "来源 stream 缺少成对的 bone-index/bone-weight 组件。";
		foreach (var vertex in sourceMesh.Vertices)
		{
			var weightsByIndex = vertex.Components.Where(component => component.Type == 7).ToDictionary(component => component.Index, component => component.FloatValues);
			IReadOnlyList<float> fallback = weightsByIndex.TryGetValue(0, out var zero) ? zero : weightsByIndex.Values.FirstOrDefault() ?? Array.Empty<float>();
			var active = 0;
			foreach (var indices in vertex.Components.Where(component => component.Type == 6))
			{
				IReadOnlyList<float> weights = weightsByIndex.TryGetValue(indices.Index, out var matched) ? matched : fallback;
				active += Enumerable.Range(0, Math.Min(indices.UIntValues.Length, weights.Count)).Count(index => weights[index] > ActiveWeightThreshold);
			}
			if (active > 4) return $"来源顶点包含 {active} 个活跃骨骼影响，当前已验证的 canonical skinning route 最多无损编码 4 个。";
		}
		return null;
	}

	private static int ComponentCapacity(string format)
		=> format switch
		{
			"float" => 1,
			"vec2_half" or "vec2_float" => 2,
			"vec4_half" or "vec4_float" or "vec4_uint8" or "vec4_uint32" => 4,
			_ => 0
		};

	private static UnitBoneInfo FindBoneInfo(UnitMeshModel model, UnitRawMeshData mesh, string role)
	{
		if (model.BoneInfos.Count == 0) throw new InvalidDataException($"The {role} Unit has no BoneInfo records.");
		return model.BoneInfos[mesh.LodIndex >= 0 && mesh.LodIndex < model.BoneInfos.Count ? mesh.LodIndex : 0];
	}

	private static int IndexOf(IReadOnlyList<uint> values, uint value)
	{
		for (var index = 0; index < values.Count; index++) if (values[index] == value) return index;
		return -1;
	}
}

public sealed record TargetBakeDryRunDiagnostic(
	int TargetMeshInfoIndex,
	int SourceMeshInfoIndex,
	int EffectiveSourceSectionCount,
	int TargetSectionCount,
	IReadOnlyList<uint> TargetPaletteTransformIndexes,
	IReadOnlyList<TargetBakeSectionRemapDiagnostic> SectionRemaps,
	string Status,
	string? BlockReason);

public sealed record TargetBakeSectionRemapDiagnostic(
	int SourceEffectiveSectionIndex,
	int TargetMaterialIndex,
	IReadOnlyList<uint> BoneHashes,
	IReadOnlyList<uint> TargetFakeIndices);
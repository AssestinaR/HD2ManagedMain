namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Plans cross-armor output streams from the actual replacement mesh semantics, matching the SDK's stream rebuild behavior.
public sealed class SdkStyleVertexStreamPlanner
{
	private const uint Vec4HalfFormat = 35;
	private const uint Vec4UInt8Format = 28;
	private const uint Vec2FloatFormat = 1;
	private const uint PackedNormalFormat = 30;

	public UnitMeshModel CanonicalizeAllSkinningStreams(UnitMeshModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		var streams = model.Streams.Select(stream =>
		{
			if (!stream.Components.Any(component => component.Type is 6 or 7)) return stream;
			var components = stream.Components
				.Where(component => component.Type is not 6 and not 7)
				.Select(CanonicalizeLegacyComponent)
				.Append(new UnitStreamComponentInfo(7, "bone_weight", Vec4HalfFormat, "vec4_half", 0, 0, 8))
				.Append(new UnitStreamComponentInfo(6, "bone_index", Vec4UInt8Format, "vec4_uint8", 0, 0, 4))
				.OrderBy(component => GetSdkComponentOrder(component.Type))
				.ThenBy(component => component.Index)
				.ToArray();
			return stream with { NumComponents = checked((ulong)components.Length), VertexStride = checked((uint)components.Sum(component => component.Size)), Components = components };
		}).ToArray();
		return model with { Streams = streams };
	}

	public UnitMeshModel PlanCanonicalSkinning(UnitMeshModel targetModel, IReadOnlyCollection<SdkStyleStreamReplacement> replacements, ICurrentGameStreamLayoutRegistry? streamLayoutRegistry = null)
	{
		ArgumentNullException.ThrowIfNull(targetModel);
		ArgumentNullException.ThrowIfNull(replacements);
		if (replacements.Count == 0) return targetModel;

		var requiredSkinningCapacityByStream = replacements
			.GroupBy(replacement => GetTargetStreamIndex(targetModel, replacement.TargetMeshInfoIndex))
			.ToDictionary(group => group.Key, group => group.Max(GetRequiredSkinningCapacity));
		var streams = targetModel.Streams.Select(stream =>
		{
			if (!requiredSkinningCapacityByStream.TryGetValue(stream.Index, out var requiredSkinningCapacity)) return stream;
			var sourceComponents = replacements
				.Where(replacement => GetTargetStreamIndex(targetModel, replacement.TargetMeshInfoIndex) == stream.Index)
				.SelectMany(replacement => GetSourceStream(replacement).Components)
				.Where(component => component.Type is not 6 and not 7)
				.Select(CanonicalizeLegacyComponent)
				.GroupBy(component => (component.Type, component.Index))
				.Select(group => group.OrderByDescending(component => component.Size).ThenByDescending(component => component.Format).First())
				.ToArray();
			var sdkComponents = stream.Components
				.Where(component => component.Type is not 6 and not 7)
				.Select(CanonicalizeLegacyComponent)
				.Concat(sourceComponents)
				.GroupBy(component => (component.Type, component.Index))
				.Select(group => group.OrderByDescending(component => component.Size).ThenByDescending(component => component.Format).First())
				.Append(new UnitStreamComponentInfo(7, "bone_weight", Vec4HalfFormat, "vec4_half", 0, 0, 8))
				.Append(new UnitStreamComponentInfo(6, "bone_index", Vec4UInt8Format, "vec4_uint8", 0, 0, 4))
				.OrderBy(component => GetSdkComponentOrder(component.Type))
				.ThenBy(component => component.Index)
				.ToArray();
			var planned = streamLayoutRegistry is not null && streamLayoutRegistry.TryResolveCanonicalSkinningLayout(stream, sourceComponents, requiredSkinningCapacity, out var registered)
				? registered
				: new UnitStreamInfo(stream.Index, stream.Offset, stream.ComponentInfoId, checked((ulong)sdkComponents.Length), stream.VertexBufferId, stream.NumVertices, checked((uint)sdkComponents.Sum(component => component.Size)), stream.IndexBufferId, stream.NumIndices, stream.IndexBufferType, stream.VertexBufferOffset, stream.VertexBufferSize, stream.IndexBufferOffset, stream.IndexBufferSize, sdkComponents);
			var components = planned.Components;
			if (!components.Any(component => component.Type == 0 && component.Index == 0))
			{
				throw new InvalidDataException($"Replacement stream {stream.Index} has no position component.");
			}
			return stream with
			{
				ComponentInfoId = planned.ComponentInfoId,
				NumComponents = checked((ulong)components.Count),
				VertexStride = planned.VertexStride,
				Components = components
			};
		}).ToArray();
		return targetModel with { Streams = streams };
	}

	private static int GetRequiredSkinningCapacity(SdkStyleStreamReplacement replacement)
	{
		var sourceMesh = replacement.SourceModel.RawMeshData.FirstOrDefault(mesh => mesh.MeshInfoIndex == replacement.SourceMeshInfoIndex)
			?? throw new KeyNotFoundException($"Source Unit does not contain mesh {replacement.SourceMeshInfoIndex}.");
		var maximum = 0;
		foreach (var vertex in sourceMesh.Vertices)
		{
			var weightsByGroup = vertex.Components.Where(component => component.Type == 7).ToDictionary(component => component.Index, component => component.FloatValues);
			var fallbackWeights = weightsByGroup.Values.FirstOrDefault() ?? Array.Empty<float>();
			var active = 0;
			foreach (var indices in vertex.Components.Where(component => component.Type == 6))
			{
				var weights = weightsByGroup.TryGetValue(indices.Index, out var matchingWeights) ? matchingWeights : fallbackWeights;
				var valueCount = indices.UIntValues.Length < weights.Length ? indices.UIntValues.Length : weights.Length;
				for (var index = 0; index < valueCount; index++)
				{
					if (weights[index] > 0.001f) active++;
				}
			}
			maximum = Math.Max(maximum, active);
		}
		return Math.Max(1, maximum);
	}

	private static UnitStreamComponentInfo CanonicalizeLegacyComponent(UnitStreamComponentInfo component)
		=> component switch
		{
			{ Type: 1, Format: 30 } => component with { Format = PackedNormalFormat, FormatName = "unk_normal", Size = 4 },
			{ Type: 4, Format: 33 } => component with { Format = Vec2FloatFormat, FormatName = "vec2_float", Size = 8 },
			_ => component
		};

	public UnitMeshModel Plan(UnitMeshModel targetModel, IReadOnlyCollection<SdkStyleStreamReplacement> replacements)
	{
		ArgumentNullException.ThrowIfNull(targetModel);
		ArgumentNullException.ThrowIfNull(replacements);
		if (replacements.Count == 0) return targetModel;

		var replacementStreams = replacements
			.GroupBy(replacement => GetTargetStreamIndex(targetModel, replacement.TargetMeshInfoIndex))
			.ToDictionary(group => group.Key, group => group.ToArray());
		var streams = targetModel.Streams.Select(stream =>
		{
			if (!replacementStreams.TryGetValue(stream.Index, out var streamReplacements)) return stream;
			var sourceStreamDeclarations = streamReplacements
				.Select(replacement => GetSourceStream(replacement))
				.Select(sourceStream => sourceStream.ComponentInfoId)
				.Distinct()
				.ToArray();
			if (sourceStreamDeclarations.Length != 1)
			{
				throw new InvalidDataException($"Replacement stream {stream.Index} cannot combine source meshes with different ComponentInfoId declarations.");
			}
			var components = streamReplacements
				.SelectMany(replacement => GetSourceStream(replacement).Components)
				.Select(component => component.Type <= 7
					? component
					: throw new InvalidDataException($"Replacement stream {stream.Index} contains unsupported vertex semantic type {component.Type}."))
				.GroupBy(component => (component.Type, component.Index))
				.Select(group => group
					.OrderByDescending(component => component.Size)
					.ThenByDescending(component => component.Format)
					.First())
				// The component record order is part of the interleaved vertex ABI. Keep the
				// SDK order rather than numeric semantic order: it also matches retained
				// ComponentInfoId declarations from current target shells.
				.OrderBy(component => GetSdkComponentOrder(component.Type))
				.ThenBy(component => component.Index)
				.ToArray();
			if (!components.Any(component => component.Type == 0 && component.Index == 0))
			{
				throw new InvalidDataException($"Replacement stream {stream.Index} has no position component.");
			}
			if (components.Length * 20 > 320) throw new InvalidDataException($"Replacement stream {stream.Index} exceeds the Unit component block capacity.");
			return stream with
			{
				ComponentInfoId = sourceStreamDeclarations[0],
				NumComponents = checked((ulong)components.Length),
				VertexStride = checked((uint)components.Sum(component => component.Size)),
				Components = components
			};
		}).ToArray();
		return targetModel with { Streams = streams };
	}

	private static int GetTargetStreamIndex(UnitMeshModel targetModel, int targetMeshInfoIndex)
	{
		var mesh = targetModel.RawMeshData.FirstOrDefault(item => item.MeshInfoIndex == targetMeshInfoIndex)
			?? throw new KeyNotFoundException($"Target Unit does not contain mesh {targetMeshInfoIndex}.");
		return checked((int)mesh.StreamIndex);
	}

	private static int GetSdkComponentOrder(uint type) => type switch
	{
		5 => 0, // color
		0 => 1, // position
		1 => 2, // normal
		2 => 3, // tangent
		3 => 4, // bitangent
		4 => 5, // UV sets
		7 => 6, // bone weights
		6 => 7, // bone-index groups
		_ => throw new InvalidDataException($"Unsupported vertex semantic type {type}.")
	};

	private static UnitStreamInfo GetSourceStream(SdkStyleStreamReplacement replacement)
	{
		var sourceMesh = replacement.SourceModel.RawMeshData.FirstOrDefault(item => item.MeshInfoIndex == replacement.SourceMeshInfoIndex)
			?? throw new KeyNotFoundException($"Source Unit does not contain mesh {replacement.SourceMeshInfoIndex}.");
		var sourceStream = replacement.SourceModel.Streams.FirstOrDefault(item => item.Index == sourceMesh.StreamIndex)
			?? throw new KeyNotFoundException($"Source Unit does not contain stream {sourceMesh.StreamIndex}.");
		return sourceStream;
	}
}

public sealed record SdkStyleStreamReplacement(int TargetMeshInfoIndex, UnitMeshModel SourceModel, int SourceMeshInfoIndex);
using System.Buffers.Binary;
using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Normalizes merged canonical RawMesh vertices and re-encodes them against the target stream ABI.
// SDK reference: tools/ref/HD2SDK-CommunityEdition/stingray/unit.py PrepareMesh/GetMeshData,
// especially Serialize*Component, StreamComponentType, and StreamComponentFormat. This deliberately
// does not call UnitMeshWriter or any legacy/Manager path; unsupported ABI is fail-closed.
public sealed record CanonicalMeshPreparationResult(
	UnitRawMeshData? Mesh,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Mesh is not null && Diagnostics.Count == 0;
}

public sealed class CanonicalMeshPreparation
{
	public CanonicalMeshPreparationResult TryPrepare(UnitRawMeshData merged, UnitStreamInfo targetStream)
	{
		ArgumentNullException.ThrowIfNull(merged);
		ArgumentNullException.ThrowIfNull(targetStream);

		var diagnostics = new List<CanonicalPlanDiagnostic>();
		ValidateIndices(merged, diagnostics);
		if (targetStream.VertexStride == 0)
			diagnostics.Add(new("InvalidTargetStream", "The target stream has a zero vertex stride."));

		var targetComponents = targetStream.Components.ToArray();
		merged = AddSdkDefaultComponents(merged, targetComponents);
		var sourceByKey = merged.Vertices
			.SelectMany(vertex => vertex.Components.Select(component => (vertex.Index, Component: component)))
			.GroupBy(item => (item.Component.Type, item.Component.Index))
			.ToDictionary(group => group.Key, group => group.First().Component);

		foreach (var component in targetComponents)
		{
			if (!IsSupportedFormat(component.FormatName, component.Format))
				diagnostics.Add(new("UnsupportedComponentFormat", $"Target component type {component.Type}, index {component.Index}, format {component.FormatName} cannot be safely encoded."));
			if (!sourceByKey.ContainsKey((component.Type, component.Index)))
				diagnostics.Add(new("MissingTargetComponent", $"Merged vertices do not contain target component type {component.Type}, index {component.Index}."));
		}

		foreach (var vertex in merged.Vertices)
		{
			var components = vertex.Components;
			foreach (var target in targetComponents)
				if (!components.Any(component => component.Type == target.Type && component.Index == target.Index))
					diagnostics.Add(new("MissingVertexComponent", $"Vertex {vertex.Index} does not contain target component type {target.Type}, index {target.Index}."));
			var weightComponents = components.Where(component => component.Type == 7).ToArray();
			if (weightComponents.Length > 1 || weightComponents.Any(component => component.Index != 0))
				diagnostics.Add(new("UnsupportedWeightLayout", "Only one bone weight component at index 0 is supported."));
		}

		if (diagnostics.Count != 0)
			return new(null, Array.AsReadOnly(diagnostics.ToArray()));

		var vertices = new List<UnitRawVertexRecord>(merged.Vertices.Count);
		foreach (var vertex in merged.Vertices)
		{
			var normalizedVertex = NormalizeSkinning(vertex, targetComponents, diagnostics);
			if (diagnostics.Count != 0)
				return new(null, Array.AsReadOnly(diagnostics.ToArray()));
			var source = normalizedVertex.Components.ToDictionary(component => (component.Type, component.Index));
			var encoded = new List<UnitVertexComponentValue>(targetComponents.Length);
			var data = new byte[checked((int)targetStream.VertexStride)];
			var offset = 0;
			foreach (var target in targetComponents)
			{
				var value = source[(target.Type, target.Index)];
				var result = Encode(target, value, data.AsSpan(offset, checked((int)target.Size)));
				encoded.Add(result);
				offset = checked(offset + (int)target.Size);
			}

			if (offset != data.Length)
			{
				diagnostics.Add(new("DataComponentsMismatch", $"Target stream component sizes total {offset}, but vertex stride is {data.Length}."));
				break;
			}
			vertices.Add(new UnitRawVertexRecord(checked((uint)vertices.Count), data, encoded));
		}

		if (diagnostics.Count != 0)
			return new(null, Array.AsReadOnly(diagnostics.ToArray()));
		return new(merged with { Vertices = vertices }, Array.Empty<CanonicalPlanDiagnostic>());
	}

	private static UnitRawMeshData AddSdkDefaultComponents(UnitRawMeshData mesh, IReadOnlyList<UnitStreamComponentInfo> profile)
		=> mesh with
		{
			Vertices = mesh.Vertices.Select(vertex =>
			{
				var present = vertex.Components.ToDictionary(component => (component.Type, component.Index));
				var components = profile.Select(component => present.TryGetValue((component.Type, component.Index), out var value)
					? value
					: CreateDefault(component)).ToArray();
				return vertex with { Components = components, Data = Array.Empty<byte>() };
			}).ToArray()
		};

	private static UnitVertexComponentValue CreateDefault(UnitStreamComponentInfo component)
	{
		float[] floats = component.Type switch
		{
			1 or 2 => [0f, 0f, 1f, 0f],
			3 => [0f, 1f, 0f, 0f],
			5 => [1f, 1f, 1f, 1f],
			7 => [1f, 0f, 0f, 0f],
			_ => [0f, 0f, 0f, 0f]
		};
		var uints = component.Type == 6 ? new uint[] { 0, 0, 0, 0 } : Array.Empty<uint>();
		return new(component.Type, component.TypeName, component.Format, component.FormatName, component.Index, floats, uints, Array.Empty<byte>());
	}

	private static UnitRawVertexRecord NormalizeSkinning(
		UnitRawVertexRecord vertex,
		IReadOnlyList<UnitStreamComponentInfo> targetComponents,
		List<CanonicalPlanDiagnostic> diagnostics)
	{
		var weights = vertex.Components.SingleOrDefault(component => component.Type == 7 && component.Index == 0);
		if (weights is null)
			return vertex;
		var indices = vertex.Components.SingleOrDefault(component => component.Type == 6 && component.Index == 0);
		if (indices is null)
		{
			// A weight-only stream is retained for existing non-skinning layouts. A skinned
			// stream always reaches the paired path below, which preserves index/weight identity.
			return vertex with { Components = vertex.Components.Select(component => component == weights ? NormalizeWeights(component) : component).ToArray() };
		}

		var sourceWeights = weights.FloatValues.Length > 0
			? weights.FloatValues
			: weights.UIntValues.Select(number => number / 255f).ToArray();
		if (indices.UIntValues.Length != sourceWeights.Length)
		{
			diagnostics.Add(new("BoneWeightIndexArityMismatch", $"Vertex {vertex.Index} has {indices.UIntValues.Length} bone indices but {sourceWeights.Length} bone weights."));
			return vertex;
		}

		var selected = sourceWeights
			.Select((weight, index) => (Weight: weight, Index: indices.UIntValues[index]))
			.Where(item => float.IsFinite(item.Weight) && item.Weight > 0)
			.OrderByDescending(item => item.Weight)
			.Take(4)
			.ToArray();
		var total = selected.Sum(item => item.Weight);
		var normalizedWeights = new float[4];
		var normalizedIndices = new uint[4];
		if (total > 0)
		{
			for (var index = 0; index < selected.Length; index++)
			{
				normalizedWeights[index] = selected[index].Weight / total;
				normalizedIndices[index] = selected[index].Index;
			}
		}

		return vertex with
		{
			Components = vertex.Components.Select(component =>
				component == weights
					? component with { FloatValues = normalizedWeights, UIntValues = Array.Empty<uint>(), RawData = Array.Empty<byte>() }
					: component == indices
						? component with { FloatValues = Array.Empty<float>(), UIntValues = normalizedIndices, RawData = Array.Empty<byte>() }
						: component).ToArray()
		};
	}

	private static void ValidateIndices(UnitRawMeshData mesh, List<CanonicalPlanDiagnostic> diagnostics)
	{
		foreach (var triangle in mesh.Triangles.Concat(mesh.Sections.SelectMany(section => section.Triangles)))
			if (triangle.A >= mesh.Vertices.Count || triangle.B >= mesh.Vertices.Count || triangle.C >= mesh.Vertices.Count)
				diagnostics.Add(new("IndexOutOfRange", "Canonical preparation received a triangle outside the vertex range."));
	}

	private static UnitVertexComponentValue NormalizeWeights(UnitVertexComponentValue value)
	{
		var weights = value.FloatValues.Length > 0 ? value.FloatValues : value.UIntValues.Select(number => number / 255f).ToArray();
		var selected = weights.Select((weight, index) => (Weight: weight, Index: index))
			.Where(item => float.IsFinite(item.Weight) && item.Weight > 0)
			.OrderByDescending(item => item.Weight).Take(4).ToArray();
		var normalized = new float[4];
		var total = selected.Sum(item => item.Weight);
		if (total > 0)
			for (var index = 0; index < selected.Length; index++) normalized[index] = selected[index].Weight / total;
		return value with { FloatValues = normalized, UIntValues = Array.Empty<uint>(), RawData = Array.Empty<byte>() };
	}

	private static UnitVertexComponentValue Encode(UnitStreamComponentInfo target, UnitVertexComponentValue source, Span<byte> destination)
	{
		var floats = source.FloatValues.ToArray();
		var uints = source.UIntValues.ToArray();
		if (target.Type == 0 || target.Type is 1 or 2 or 3 or 4)
			floats = source.FloatValues;
		else if (target.Type is 6)
			uints = source.UIntValues.Length > 0 ? source.UIntValues : source.FloatValues.Select(value => checked((uint)Math.Max(0, value))).ToArray();
		else if (target.Type is 5 && source.FloatValues.Length == 0)
			floats = source.UIntValues.Select(value => value / 255f).ToArray();

		var size = checked((int)target.Size);
		switch (target.FormatName)
		{
			case "float": BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(floats[0])); break;
			case "vec2_float": WriteFloats(destination, floats, 2); break;
			case "vec3_float": WriteFloats(destination, floats, 3); break;
			case "vec4_float": WriteFloats(destination, floats, 4); break;
			case "rgba_r8g8b8a8": for (var i = 0; i < 4; i++) destination[i] = (byte)Math.Clamp((int)MathF.Round((floats[i] <= 1 ? floats[i] * 255 : floats[i])), 0, 255); break;
			case "vec4_uint32": for (var i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(destination[(i * 4)..], uints[i]); break;
			case "vec4_uint8": for (var i = 0; i < 4; i++) destination[i] = checked((byte)uints[i]); break;
			case "vec4_1010102": BinaryPrimitives.WriteUInt32LittleEndian(destination, uints.Length > 0 ? uints[0] : PackTenBit(floats)); break;
			case "unk_normal": BinaryPrimitives.WriteUInt32LittleEndian(destination, PackOctNormal(floats)); break;
			case "vec4_half": for (var i = 0; i < 4; i++) BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], BitConverter.HalfToUInt16Bits((Half)floats[i])); break;
			case "vec2_half": for (var i = 0; i < 2; i++) BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], BitConverter.HalfToUInt16Bits((Half)floats[i])); break;
			default: throw new InvalidDataException($"Unsupported target component format {target.FormatName} ({target.Format}).");
		}
		if (size != destination.Length) throw new InvalidDataException("Encoded component size does not match target ABI.");
		return new(target.Type, target.TypeName, target.Format, target.FormatName, target.Index, floats, uints, destination.ToArray());
	}

	private static void WriteFloats(Span<byte> destination, float[] values, int count)
	{
		for (var i = 0; i < count; i++) BinaryPrimitives.WriteInt32LittleEndian(destination[(i * 4)..], BitConverter.SingleToInt32Bits(values[i]));
	}

	private static bool IsSupportedFormat(string name, uint format)
		=> name is "float" or "vec2_float" or "vec3_float" or "vec4_float" or "rgba_r8g8b8a8" or "vec4_uint32" or "vec4_uint8" or "vec4_1010102" or "unk_normal" or "vec2_half" or "vec4_half";

	private static uint PackTenBit(float[] values)
	{
		var packed = 0u;
		for (var index = 0; index < 3; index++)
			packed |= (uint)Math.Clamp((int)MathF.Round(values[index] * 1023f), 0, 1023) << (index * 10);
		return packed;
	}

	private static uint PackOctNormal(float[] values)
	{
		if (values.Length < 3) throw new InvalidDataException("Packed normal requires three float values.");
		var length = MathF.Abs(values[0]) + MathF.Abs(values[1]) + MathF.Abs(values[2]);
		if (!float.IsFinite(length) || length <= 0) throw new InvalidDataException("Packed normal cannot encode a zero or non-finite vector.");
		var x = values[0] / length;
		var y = values[1] / length;
		if (values[2] < 0)
		{
			(x, y) = ((1 - MathF.Abs(y)) * MathF.Sign(x), (1 - MathF.Abs(x)) * MathF.Sign(y));
		}
		var packedX = (uint)Math.Clamp((int)MathF.Round((x + 1) * 511.5f), 0, 1023);
		var packedY = (uint)Math.Clamp((int)MathF.Round((y + 1) * 511.5f), 0, 1023);
		return packedX | (packedY << 10);
	}
}

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
	private static readonly float[] DefaultZero = [0f, 0f, 0f, 0f];
	private static readonly float[] DefaultNormal = [0f, 0f, 1f, 0f];
	private static readonly float[] DefaultTangent = [0f, 1f, 0f, 0f];
	private static readonly float[] DefaultColor = [1f, 1f, 1f, 1f];
	private static readonly float[] DefaultWeight = [1f, 0f, 0f, 0f];
	private static readonly uint[] DefaultBoneIndices = [0, 0, 0, 0];

	public CanonicalMeshPreparationResult TryPrepare(UnitRawMeshData merged, UnitStreamInfo targetStream)
	{
		ArgumentNullException.ThrowIfNull(merged);
		ArgumentNullException.ThrowIfNull(targetStream);

		var diagnostics = new List<CanonicalPlanDiagnostic>();
		ValidateIndices(merged, diagnostics);
		if (targetStream.VertexStride == 0)
			diagnostics.Add(new("InvalidTargetStream", "The target stream has a zero vertex stride."));

		var targetComponents = targetStream.Components.ToArray();
		var hasWeights = false;
		foreach (var component in targetComponents)
		{
			if (!IsSupportedFormat(component.FormatName, component.Format))
				diagnostics.Add(new("UnsupportedComponentFormat", $"Target component type {component.Type}, index {component.Index}, format {component.FormatName} cannot be safely encoded."));
			if (component.Type != 7) continue;
			if (hasWeights || component.Index != 0)
				diagnostics.Add(new("UnsupportedWeightLayout", "Only one bone weight component at index 0 is supported."));
			hasWeights = true;
		}

		if (diagnostics.Count != 0)
			return new(null, Array.AsReadOnly(diagnostics.ToArray()));

		var vertices = new UnitRawVertexRecord[merged.Vertices.Count];
		var hasBoneIndices = targetComponents.Any(component => component.Type == 6 && component.Index == 0);
		for (var vertexIndex = 0; vertexIndex < merged.Vertices.Count; vertexIndex++)
		{
			var vertex = merged.Vertices[vertexIndex];
			if (!TryEncodeVertex(vertex, targetComponents, hasWeights, hasBoneIndices, targetStream.VertexStride, diagnostics, out var encoded))
				return new(null, Array.AsReadOnly(diagnostics.ToArray()));
			vertices[vertexIndex] = encoded! with { Index = checked((uint)vertexIndex) };
		}

		if (diagnostics.Count != 0)
			return new(null, Array.AsReadOnly(diagnostics.ToArray()));
		return new(merged with { Vertices = vertices }, Array.Empty<CanonicalPlanDiagnostic>());
	}

	private static bool TryEncodeVertex(
		UnitRawVertexRecord vertex,
		IReadOnlyList<UnitStreamComponentInfo> targets,
		bool hasWeights,
		bool hasBoneIndices,
		uint stride,
		List<CanonicalPlanDiagnostic> diagnostics,
		out UnitRawVertexRecord? encodedVertex)
	{
		var weights = hasWeights ? FindComponent(vertex.Components, 7, 0) : null;
		var indices = hasBoneIndices ? FindComponent(vertex.Components, 6, 0) : null;
		float[]? normalizedWeights = null;
		uint[]? normalizedIndices = null;
		if (weights is not null)
		{
			if (!TryNormalizeSkinning(vertex.Index, weights, indices, out normalizedWeights, out normalizedIndices, diagnostics))
			{
				encodedVertex = null;
				return false;
			}
		}

		var data = new byte[checked((int)stride)];
		var components = new UnitVertexComponentValue[targets.Count];
		var offset = 0;
		for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
		{
			var target = targets[targetIndex];
			var source = FindComponent(vertex.Components, target.Type, target.Index);
			var floats = source?.FloatValues ?? DefaultFloats(target.Type);
			var uints = source?.UIntValues ?? DefaultUInts(target.Type);
			if (target.Type == 7 && target.Index == 0 && normalizedWeights is not null)
			{
				floats = normalizedWeights;
				uints = Array.Empty<uint>();
			}
			else if (target.Type == 6 && target.Index == 0 && normalizedIndices is not null)
			{
				floats = Array.Empty<float>();
				uints = normalizedIndices;
			}

			var size = checked((int)target.Size);
			var destination = data.AsSpan(offset, size);
			Encode(target, floats, uints, destination);
			components[targetIndex] = new UnitVertexComponentValue(
				target.Type, target.TypeName, target.Format, target.FormatName, target.Index,
				floats, uints, destination.ToArray());
			offset = checked(offset + size);
		}

		if (offset != data.Length)
		{
			diagnostics.Add(new("DataComponentsMismatch", $"Target stream component sizes total {offset}, but vertex stride is {data.Length}."));
			encodedVertex = null;
			return false;
		}
		encodedVertex = new UnitRawVertexRecord(vertex.Index, data, components);
		return true;
	}

	private static bool TryNormalizeSkinning(
		uint vertexIndex,
		UnitVertexComponentValue weights,
		UnitVertexComponentValue? indices,
		out float[] normalizedWeights,
		out uint[]? normalizedIndices,
		List<CanonicalPlanDiagnostic> diagnostics)
	{
		var count = weights.FloatValues.Length > 0 ? weights.FloatValues.Length : weights.UIntValues.Length;
		if (indices is not null && indices.UIntValues.Length != count)
		{
			diagnostics.Add(new("BoneWeightIndexArityMismatch", $"Vertex {vertexIndex} has {indices.UIntValues.Length} bone indices but {count} bone weights."));
			normalizedWeights = Array.Empty<float>();
			normalizedIndices = null;
			return false;
		}

		Span<float> selectedWeights = stackalloc float[4];
		Span<uint> selectedIndices = stackalloc uint[4];
		var selectedCount = 0;
		for (var index = 0; index < count; index++)
		{
			var weight = weights.FloatValues.Length > 0 ? weights.FloatValues[index] : weights.UIntValues[index] / 255f;
			if (!float.IsFinite(weight) || weight <= 0) continue;
			var insertion = selectedCount;
			while (insertion > 0 && weight > selectedWeights[insertion - 1]) insertion--;
			if (insertion >= 4) continue;
			var limit = Math.Min(selectedCount, 3);
			for (var shift = limit; shift > insertion; shift--)
			{
				selectedWeights[shift] = selectedWeights[shift - 1];
				selectedIndices[shift] = selectedIndices[shift - 1];
			}
			selectedWeights[insertion] = weight;
			if (indices is not null) selectedIndices[insertion] = indices.UIntValues[index];
			selectedCount = Math.Min(selectedCount + 1, 4);
		}

		normalizedWeights = new float[4];
		normalizedIndices = indices is null ? null : new uint[4];
		var total = 0f;
		for (var index = 0; index < selectedCount; index++) total += selectedWeights[index];
		if (total <= 0) return true;
		for (var index = 0; index < selectedCount; index++)
		{
			normalizedWeights[index] = selectedWeights[index] / total;
			if (normalizedIndices is not null) normalizedIndices[index] = selectedIndices[index];
		}
		return true;
	}

	private static UnitVertexComponentValue? FindComponent(IReadOnlyList<UnitVertexComponentValue> components, uint type, uint index)
	{
		for (var position = 0; position < components.Count; position++)
			if (components[position].Type == type && components[position].Index == index)
				return components[position];
		return null;
	}

	private static float[] DefaultFloats(uint type) => type switch
	{
		1 or 2 => DefaultNormal,
		3 => DefaultTangent,
		5 => DefaultColor,
		7 => DefaultWeight,
		_ => DefaultZero
	};

	private static uint[] DefaultUInts(uint type)
		=> type == 6 ? DefaultBoneIndices : Array.Empty<uint>();

	private static void ValidateIndices(UnitRawMeshData mesh, List<CanonicalPlanDiagnostic> diagnostics)
	{
		foreach (var triangle in mesh.Triangles.Concat(mesh.Sections.SelectMany(section => section.Triangles)))
			if (triangle.A >= mesh.Vertices.Count || triangle.B >= mesh.Vertices.Count || triangle.C >= mesh.Vertices.Count)
				diagnostics.Add(new("IndexOutOfRange", "Canonical preparation received a triangle outside the vertex range."));
	}

	private static void Encode(UnitStreamComponentInfo target, float[] floats, uint[] uints, Span<byte> destination)
	{
		var size = checked((int)target.Size);
		switch (target.FormatName)
		{
			case "float": BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(floats[0])); break;
			case "vec2_float": WriteFloats(destination, floats, 2); break;
			case "vec3_float": WriteFloats(destination, floats, 3); break;
			case "vec4_float": WriteFloats(destination, floats, 4); break;
			case "rgba_r8g8b8a8": for (var i = 0; i < 4; i++) destination[i] = (byte)Math.Clamp((int)MathF.Round((FloatAt(floats, uints, i) <= 1 ? FloatAt(floats, uints, i) * 255 : FloatAt(floats, uints, i))), 0, 255); break;
			case "vec4_uint32": for (var i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(destination[(i * 4)..], uints[i]); break;
			case "vec4_uint8": for (var i = 0; i < 4; i++) destination[i] = checked((byte)UIntAt(floats, uints, i)); break;
			case "vec4_1010102": BinaryPrimitives.WriteUInt32LittleEndian(destination, uints.Length > 0 ? uints[0] : PackTenBit(floats)); break;
			case "unk_normal": BinaryPrimitives.WriteUInt32LittleEndian(destination, PackOctNormal(floats)); break;
			case "vec4_half": for (var i = 0; i < 4; i++) BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], BitConverter.HalfToUInt16Bits((Half)floats[i])); break;
			case "vec2_half": for (var i = 0; i < 2; i++) BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], BitConverter.HalfToUInt16Bits((Half)floats[i])); break;
			default: throw new InvalidDataException($"Unsupported target component format {target.FormatName} ({target.Format}).");
		}
		if (size != destination.Length) throw new InvalidDataException("Encoded component size does not match target ABI.");
	}

	private static float FloatAt(float[] floats, uint[] uints, int index)
		=> floats.Length > index ? floats[index] : uints[index] / 255f;

	private static uint UIntAt(float[] floats, uint[] uints, int index)
		=> uints.Length > index ? uints[index] : checked((uint)Math.Max(0, floats[index]));

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

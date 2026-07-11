namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Replaces Unit RawMeshes with tiny placeholder triangles for broad in-game coverage smoke patches.
public sealed class PlaceholderUnitMeshMinifier
{
	public UnitMeshModel MinifyAll(UnitMeshModel model)
	{
		var meshIndexes = model.RawMeshData.Select(mesh => mesh.MeshInfoIndex).ToHashSet();
		return Minify(model, meshIndexes);
	}

	public UnitMeshModel MinifyExcept(UnitMeshModel model, IReadOnlySet<int> preservedMeshInfoIndexes)
	{
		var meshIndexes = model.RawMeshData
			.Select(mesh => mesh.MeshInfoIndex)
			.Where(meshInfoIndex => !preservedMeshInfoIndexes.Contains(meshInfoIndex))
			.ToHashSet();
		return Minify(model, meshIndexes);
	}

	private static UnitMeshModel Minify(UnitMeshModel model, IReadOnlySet<int> meshInfoIndexes)
	{
		var rawMeshes = model.RawMeshData
			.Select(mesh => meshInfoIndexes.Contains(mesh.MeshInfoIndex) ? MinifyRawMeshData(model, mesh) : mesh)
			.ToArray();

		return model with { RawMeshData = rawMeshes };
	}

	private static UnitRawMeshData MinifyRawMeshData(UnitMeshModel model, UnitRawMeshData rawMesh)
	{
		var stream = model.Streams.FirstOrDefault(stream => stream.Index == rawMesh.StreamIndex);
		if (stream is null)
		{
			return rawMesh;
		}

		var vertices = BuildPlaceholderVertices(stream);
		var firstSection = rawMesh.Sections.FirstOrDefault();
		var section = new UnitRawMeshSectionData(
			firstSection?.MaterialIndex ?? 0,
			firstSection?.MaterialSlotId ?? 0,
			[new UnitTriangleIndices(0, 1, 2)]);

		return rawMesh with
		{
			Sections = [section],
			Triangles = section.Triangles,
			Vertices = vertices
		};
	}

	private static IReadOnlyList<UnitRawVertexRecord> BuildPlaceholderVertices(UnitStreamInfo stream)
	{
		var vertices = new List<UnitRawVertexRecord>(3);
		var positions = new[]
		{
			new[] { 0f, 0f, 0f },
			new[] { 0.001f, 0f, 0f },
			new[] { 0f, 0.001f, 0f }
		};

		for (var i = 0; i < 3; i++)
		{
			var data = new byte[checked((int)stream.VertexStride)];
			WritePlaceholderComponents(data, stream.Components, positions[i]);
			vertices.Add(new UnitRawVertexRecord((uint)i, data, Array.Empty<UnitVertexComponentValue>()));
		}

		return vertices;
	}

	private static void WritePlaceholderComponents(byte[] vertexData, IReadOnlyList<UnitStreamComponentInfo> components, IReadOnlyList<float> position)
	{
		var cursor = 0;
		foreach (var component in components)
		{
			var size = checked((int)component.Size);
			if (size <= 0 || cursor + size > vertexData.Length)
			{
				continue;
			}

			WritePlaceholderComponent(vertexData.AsSpan(cursor, size), component, position);
			cursor += size;
		}
	}

	private static void WritePlaceholderComponent(Span<byte> destination, UnitStreamComponentInfo component, IReadOnlyList<float> position)
	{
		switch (component.Type)
		{
			case 0:
				WriteFloatComponent(destination, component, position);
				break;
			case 1:
			case 2:
			case 3:
				WriteFloatComponent(destination, component, [0f, 0f, 1f, 1f]);
				break;
			case 4:
				WriteFloatComponent(destination, component, [0f, 0f, 0f, 1f]);
				break;
			case 5:
				WriteColorComponent(destination, component);
				break;
			case 6:
				WriteIntegerComponent(destination, component);
				break;
			case 7:
				WriteFloatComponent(destination, component, [1f, 0f, 0f, 0f]);
				break;
		}
	}

	private static void WriteFloatComponent(Span<byte> destination, UnitStreamComponentInfo component, IReadOnlyList<float> values)
	{
		switch (component.FormatName)
		{
			case "float":
				WriteSingle(destination, 0, values[0]);
				break;
			case "vec2_float":
				WriteSingle(destination, 0, values[0]);
				WriteSingle(destination, 4, values.Count > 1 ? values[1] : 0f);
				break;
			case "vec3_float":
				WriteSingle(destination, 0, values[0]);
				WriteSingle(destination, 4, values.Count > 1 ? values[1] : 0f);
				WriteSingle(destination, 8, values.Count > 2 ? values[2] : 0f);
				break;
			case "vec4_float":
				WriteSingle(destination, 0, values[0]);
				WriteSingle(destination, 4, values.Count > 1 ? values[1] : 0f);
				WriteSingle(destination, 8, values.Count > 2 ? values[2] : 0f);
				WriteSingle(destination, 12, values.Count > 3 ? values[3] : 0f);
				break;
			case "vec4_1010102":
				WriteUInt32(destination, 0, EncodeTenBitUnsigned(values));
				break;
			case "unk_normal":
				WriteUInt32(destination, 0, EncodePackedOctNormal(values));
				break;
			case "vec2_half":
				WriteHalf(destination, 0, values[0]);
				WriteHalf(destination, 2, values.Count > 1 ? values[1] : 0f);
				break;
			case "vec4_half":
				WriteHalf(destination, 0, values[0]);
				WriteHalf(destination, 2, values.Count > 1 ? values[1] : 0f);
				WriteHalf(destination, 4, values.Count > 2 ? values[2] : 0f);
				WriteHalf(destination, 6, values.Count > 3 ? values[3] : 0f);
				break;
		}
	}

	private static void WriteColorComponent(Span<byte> destination, UnitStreamComponentInfo component)
	{
		if (component.FormatName is "rgba_r8g8b8a8" or "vec4_uint8")
		{
			destination[0] = 255;
			destination[1] = 255;
			destination[2] = 255;
			destination[3] = 255;
		}
	}

	private static void WriteIntegerComponent(Span<byte> destination, UnitStreamComponentInfo component)
	{
		if (component.FormatName == "vec4_uint32")
		{
			WriteUInt32(destination, 0, 0);
			WriteUInt32(destination, 4, 0);
			WriteUInt32(destination, 8, 0);
			WriteUInt32(destination, 12, 0);
		}
		else if (component.FormatName == "vec4_uint8")
		{
			destination[0] = 0;
			destination[1] = 0;
			destination[2] = 0;
			destination[3] = 0;
		}
	}

	private static uint EncodeTenBitUnsigned(IReadOnlyList<float> values)
	{
		var x = ClampToBits(values[0], 1023);
		var y = ClampToBits(values.Count > 1 ? values[1] : 0f, 1023);
		var z = ClampToBits(values.Count > 2 ? values[2] : 0f, 1023);
		var w = ClampToBits(values.Count > 3 ? values[3] : 0f, 3);
		return x | (y << 10) | (z << 20) | (w << 30);
	}

	private static uint EncodePackedOctNormal(IReadOnlyList<float> values)
	{
		var x = values[0];
		var y = values.Count > 1 ? values[1] : 0f;
		var z = values.Count > 2 ? values[2] : 1f;
		var length = MathF.Sqrt(x * x + y * y + z * z);
		if (length > 0)
		{
			x /= length;
			y /= length;
			z /= length;
		}

		var l1 = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
		if (l1 > 0)
		{
			x /= l1;
			y /= l1;
		}

		if (z < 0)
		{
			var oldX = x;
			x = (1f - Math.Abs(y)) * Math.Sign(oldX == 0 ? 1f : oldX);
			y = (1f - Math.Abs(oldX)) * Math.Sign(y == 0 ? 1f : y);
		}

		var encodedX = ClampToBits((x + 1f) * 0.5f, 1023);
		var encodedY = ClampToBits((y + 1f) * 0.5f, 1023);
		return encodedX | (encodedY << 10);
	}

	private static uint ClampToBits(float value, uint max)
		=> (uint)Math.Clamp((int)MathF.Round(value * max), 0, (int)max);

	private static void WriteSingle(Span<byte> data, int offset, float value)
		=> WriteUInt32(data, offset, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

	private static void WriteHalf(Span<byte> data, int offset, float value)
	{
		var bits = BitConverter.HalfToUInt16Bits((Half)value);
		data[offset] = (byte)bits;
		data[offset + 1] = (byte)(bits >> 8);
	}

	private static void WriteUInt32(Span<byte> data, int offset, uint value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
		data[offset + 2] = (byte)(value >> 16);
		data[offset + 3] = (byte)(value >> 24);
	}
}

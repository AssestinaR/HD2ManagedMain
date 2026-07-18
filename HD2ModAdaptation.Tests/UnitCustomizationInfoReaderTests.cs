using System.Text;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Locks the HD2SDK-verified Unit CustomizationInfoOffset header layout used for armor variants.
public sealed class UnitCustomizationInfoReaderTests
{
	[Fact]
	public void Read_UsesSdkCustomizationOffsetAt0x4C()
	{
		const int customizationOffset = 0x100;
		const int streamInfoOffset = 0x80;
		const int meshInfoOffset = 0x90;
		var data = new byte[0x220];
		WriteUInt32(data, 0x4c, customizationOffset);
		WriteUInt32(data, 0x5c, streamInfoOffset);
		WriteUInt32(data, 0x64, meshInfoOffset);
		WriteUInt32(data, streamInfoOffset, 0);
		WriteUInt32(data, meshInfoOffset, 0);
		WriteCustomization(data, customizationOffset, "Slim", "Torso", "Any", "Undergarment");

		var model = new UnitMeshReader().Read(data, Array.Empty<byte>());

		Assert.Equal((uint)customizationOffset, model.CustomizationInfoOffset);
		Assert.Equal("Slim", model.CustomizationInfo.BodyType);
		Assert.Equal("Torso", model.CustomizationInfo.Slot);
		Assert.Equal("Any", model.CustomizationInfo.Weight);
		Assert.Equal("Undergarment", model.CustomizationInfo.PieceType);
	}

	[Fact]
	public void Read_PreservesCompleteTransformInfoLayout()
	{
		const int transformOffset = 0x100;
		const int streamInfoOffset = 0x80;
		const int meshInfoOffset = 0x90;
		var data = new byte[0x220];
		WriteUInt32(data, 0x34, transformOffset);
		WriteUInt32(data, 0x5c, streamInfoOffset);
		WriteUInt32(data, 0x64, meshInfoOffset);
		WriteUInt32(data, streamInfoOffset, 0);
		WriteUInt32(data, meshInfoOffset, 0);
		WriteUInt32(data, transformOffset, 1);
		WriteUInt32(data, transformOffset + 4, 11);
		WriteUInt32(data, transformOffset + 8, 12);
		WriteUInt32(data, transformOffset + 12, 13);
		for (var i = 0; i < 16; i++) WriteSingle(data, transformOffset + 16 + 64 + i * 4, i + 0.25f);
		WriteSingle(data, transformOffset + 16 + 36, 21.5f);
		WriteSingle(data, transformOffset + 16 + 48, 2.5f);
		WriteUInt16(data, transformOffset + 16 + 128, 3);
		WriteUInt16(data, transformOffset + 16 + 130, 7);
		WriteUInt32(data, transformOffset + 16 + 132, 0xabcdef01);

		var model = new UnitMeshReader().Read(data, Array.Empty<byte>());

		Assert.Equal((uint)11, model.TransformInfo.Reserved0);
		Assert.Equal(21.5f, Assert.Single(model.TransformInfo.LocalTransforms).Position[0]);
		Assert.Equal(2.5f, model.TransformInfo.LocalTransforms[0].Scale[0]);
		Assert.Equal(15.25f, Assert.Single(model.TransformInfo.Matrices).Values[15]);
		Assert.Equal((ushort)3, Assert.Single(model.TransformInfo.Entries).Increment);
		Assert.Equal((ushort)7, model.TransformInfo.Entries[0].ParentIndex);
		Assert.Equal((uint)0xabcdef01, Assert.Single(model.TransformNameHashes));
	}

	private static void WriteCustomization(byte[] data, int offset, params string[] values)
	{
		var cursor = offset + 24;
		foreach (var value in values)
		{
			var bytes = Encoding.UTF8.GetBytes(value);
			WriteUInt32(data, cursor, checked((uint)bytes.Length));
			cursor += 4;
			bytes.CopyTo(data, cursor);
			cursor += bytes.Length + 12;
		}
	}

	private static void WriteUInt32(byte[] data, int offset, uint value)
		=> BitConverter.GetBytes(value).CopyTo(data, offset);

	private static void WriteUInt16(byte[] data, int offset, ushort value)
		=> BitConverter.GetBytes(value).CopyTo(data, offset);

	private static void WriteSingle(byte[] data, int offset, float value)
		=> BitConverter.GetBytes(value).CopyTo(data, offset);
}
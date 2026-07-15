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
}
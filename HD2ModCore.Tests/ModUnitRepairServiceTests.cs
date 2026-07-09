using System.Buffers.Binary;
using System.Reflection;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies advanced unit repair keeps current game scaffolding while transplanting mod resource references.
public sealed class ModUnitRepairServiceTests
{
	[Fact]
	public void AdvancedRepair_UsesGameUnitScaffold_AndTransplantsModResourceReference()
	{
		var modUnit = CreateUnit(version: 0x00A4CD35, reference: 0x1111222233334444);
		var gameUnit = CreateUnit(version: 0x00A4CD36, reference: 0x9999888877776666);
		var gameUnitData = CreateGameUnitData(gameUnit);
		var modResourceIds = new HashSet<ulong> { 0x1111222233334444 };

		var repaired = InvokeAdvancedRepair(modUnit, gameUnitData, modResourceIds);

		Assert.Equal(0x00A4CD36U, BinaryPrimitives.ReadUInt32LittleEndian(repaired.AsSpan(0x2C, 4)));
		Assert.Equal(0x1111222233334444UL, BinaryPrimitives.ReadUInt64LittleEndian(repaired.AsSpan(0x80, 8)));
		Assert.Equal(0xAABBCCDDU, BinaryPrimitives.ReadUInt32LittleEndian(repaired.AsSpan(0x70, 4)));
	}

	private static byte[] InvokeAdvancedRepair(byte[] modUnit, object gameUnitData, IReadOnlySet<ulong> modResourceIds)
	{
		var method = typeof(ModUnitRepairService).GetMethod("RepairUnitDataAdvanced", BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new MissingMethodException(nameof(ModUnitRepairService), "RepairUnitDataAdvanced");
		return (byte[])method.Invoke(null, new[] { modUnit, gameUnitData, modResourceIds })!;
	}

	private static object CreateGameUnitData(byte[] data)
	{
		var nestedType = typeof(ModUnitRepairService).GetNestedType("GameUnitData", BindingFlags.NonPublic)
			?? throw new MissingMemberException(nameof(ModUnitRepairService), "GameUnitData");
		var lodGroup = data.AsSpan(0x70, 0x10).ToArray();
		return Activator.CreateInstance(nestedType, 0x00A4CD36U, lodGroup, data)
			?? throw new InvalidOperationException("Could not create GameUnitData.");
	}

	private static byte[] CreateUnit(uint version, ulong reference)
	{
		var data = new byte[0x120];
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2C, 4), version);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x30, 4), 0x70);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x34, 4), 0x80);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x70, 4), 0xAABBCCDD);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x80, 8), reference);
		return data;
	}
}

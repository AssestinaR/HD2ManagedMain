using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using Xunit;

namespace HD2ModCore.Tests;

// Purpose: Verifies cross-armor source assignment prefers explicit semantic layers before Unknown fallback targets.
public sealed class EquipmentUnitCatalogServiceTests
{
	private static readonly AssetKey SourceUnit = new(0xe0a48d0be9a7453f, 0x100);
	private static readonly AssetKey TargetUnit = new(0xe0a48d0be9a7453f, 0x200);

	[Fact]
	public async Task CreatePlanAsync_AssignsExactLayerBeforeEarlierUnknownTarget()
	{
		var sourcePart = Part(SourceUnit, 1, UnitMeshPartLayer.Undergarment, "g_leg_undergarment_r");
		var unknownTarget = Part(TargetUnit, 1, UnitMeshPartLayer.Unknown, "g_leg_r");
		var underTarget = Part(TargetUnit, 2, UnitMeshPartLayer.Undergarment, "g_leg_undergarment_r");
		var service = CreateService();

		var plan = await service.CreatePlanAsync(
			[Entry("source", sourcePart)], [Entry("target", unknownTarget, underTarget)], "source", null,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"]);

		Assert.Null(plan.Mappings.Single(mapping => mapping.Target.MeshInfoIndex == 1).Source);
		Assert.Equal(sourcePart, plan.Mappings.Single(mapping => mapping.Target.MeshInfoIndex == 2).Source);
	}

	[Fact]
	public async Task CreatePlanAsync_AssignsUnknownTargetWhenNoExplicitTargetCompetes()
	{
		var sourcePart = Part(SourceUnit, 1, UnitMeshPartLayer.Undergarment, "g_leg_undergarment_r");
		var unknownTarget = Part(TargetUnit, 1, UnitMeshPartLayer.Unknown, "g_leg_r");
		var service = CreateService();

		var plan = await service.CreatePlanAsync(
			[Entry("source", sourcePart)], [Entry("target", unknownTarget)], "source", null,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"]);

		Assert.Equal(sourcePart, Assert.Single(plan.Mappings).Source);
	}

	[Fact]
	public async Task CreatePlanAsync_DoesNotPlaceSlimSourceOnStockyArm()
	{
		var slimSource = Part(SourceUnit, 1, UnitMeshPartLayer.Undergarment, "g_torso_arm_l_female") with { PartKind = UnitMeshPartKind.LeftArm, BodyVariant = UnitMeshBodyVariant.Slim };
		var stockyTarget = Part(TargetUnit, 2, UnitMeshPartLayer.Undergarment, "g_torso_arm_l_male") with { PartKind = UnitMeshPartKind.LeftArm, BodyVariant = UnitMeshBodyVariant.Stocky };
		var service = CreateService();

		var plan = await service.CreatePlanAsync(
			[Entry("source", slimSource)], [Entry("target", stockyTarget)], "source", UnitMeshBodyVariant.Slim,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"]);

		Assert.Null(Assert.Single(plan.Mappings).Source);
	}

	[Fact]
	public async Task CreatePlanAsync_DoesNotMatchStandaloneArmToTorsoArm()
	{
		var source = Part(SourceUnit, 1, UnitMeshPartLayer.Undergarment, "g_torso_arm_l_female") with { PartKind = UnitMeshPartKind.LeftArm };
		var target = Part(TargetUnit, 2, UnitMeshPartLayer.Undergarment, "g_arm_l") with { PartKind = UnitMeshPartKind.LeftArm };
		var service = CreateService();

		var plan = await service.CreatePlanAsync(
			[Entry("source", source)], [Entry("target", target)], "source", null,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"]);

		Assert.Null(Assert.Single(plan.Mappings).Source);
	}

	[Fact]
	public async Task CreatePlanAsync_ManualSuppressionMinifiesTargetWithoutConsumingSource()
	{
		var source = Part(SourceUnit, 1, UnitMeshPartLayer.Undergarment, "g_leg_undergarment_r");
		var suppressedTarget = Part(TargetUnit, 2, UnitMeshPartLayer.Undergarment, "g_leg_undergarment_r");
		var activeTarget = Part(TargetUnit, 3, UnitMeshPartLayer.Undergarment, "g_leg_undergarment_r");
		var service = CreateService();

		var plan = await service.CreatePlanAsync(
			[Entry("source", source)], [Entry("target", suppressedTarget, activeTarget)], "source", null,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"],
			manualSuppressions: [new CrossArmorManualSuppression(new CrossArmorPhysicalTargetKey(TargetUnit, 2))]);

		var suppressed = plan.Mappings.Single(mapping => mapping.Target.MeshInfoIndex == 2);
		Assert.True(suppressed.IsSuppressed);
		Assert.False(suppressed.WillReplace);
		Assert.Equal(source, plan.Mappings.Single(mapping => mapping.Target.MeshInfoIndex == 3).Source);
	}

	private static EquipmentUnitCatalogService CreateService()
		=> new(new StoragePaths(Path.GetTempPath()));

	private static EquipmentUnitCatalogEntry Entry(string id, params EquipmentUnitPart[] parts)
		=> new(id, "Armor", id, parts);

	private static EquipmentUnitPart Part(AssetKey unit, int mesh, UnitMeshPartLayer layer, string name)
		=> new(unit, mesh, checked((uint)mesh), UnitMeshPartKind.RightLeg, layer, UnitMeshBodyVariant.Slim, name, 100, Array.Empty<string>());

}

using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using Xunit;

namespace HD2ModCore.Tests;

// Purpose: Verifies cross-armor planning follows the maximum-size source and explicit manual-mode rules.
public sealed class EquipmentUnitCatalogServiceTests
{
	private static readonly AssetKey SourceUnit = new(0xe0a48d0be9a7453f, 0x100);
	private static readonly AssetKey AdditionalSourceUnit = new(0xe0a48d0be9a7453f, 0x101);
	private static readonly AssetKey TargetUnit = new(0xe0a48d0be9a7453f, 0x200);

	[Fact]
	public async Task CreatePlanAsync_AutomaticModeChoosesLargestSourceOfMatchingPart()
	{
		var smaller = Part(SourceUnit, 1, "small") with { StoredBytes = 64 };
		var larger = Part(SourceUnit, 2, "large") with { StoredBytes = 1024 };
		var target = Part(TargetUnit, 3, "target");

		var plan = await CreateService().CreatePlanAsync(
			[Entry("source", smaller, larger)], [Entry("target", target)], "source", UnitMeshBodyVariant.Any,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"]);

		var mapping = Assert.Single(plan.Mappings);
		Assert.Equal(larger, mapping.Source);
		Assert.Equal("命中", mapping.Reason);
	}

	[Fact]
	public async Task CreatePlanAsync_MultipleSourceMeshesDoNotMultiplyPartHitBudget()
	{
		var smaller = Part(SourceUnit, 1, "small") with { StoredBytes = 64 };
		var larger = Part(SourceUnit, 2, "large") with { StoredBytes = 1024 };
		var largeTarget = Part(TargetUnit, 3, "large-target") with { StoredBytes = 2048 };
		var smallTarget = Part(TargetUnit, 4, "small-target") with { StoredBytes = 128 };

		var plan = await CreateService().CreatePlanAsync(
			[Entry("source", smaller, larger)], [Entry("target", largeTarget, smallTarget)], "source", UnitMeshBodyVariant.Any,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"]);

		Assert.Equal(larger, plan.Mappings.Single(mapping => mapping.Target.MeshInfoIndex == 3).Source);
		Assert.Null(plan.Mappings.Single(mapping => mapping.Target.MeshInfoIndex == 4).Source);
	}

	[Fact]
	public async Task CreatePlanAsync_EachSelectedArmorAddsOneBaseHitPerPart()
	{
		var source = Part(SourceUnit, 1, "arm") with { PartKind = UnitMeshPartKind.LeftArm, StoredBytes = 1024 };
		var firstTarget = Part(TargetUnit, 2, "first") with { PartKind = UnitMeshPartKind.LeftArm, StoredBytes = 2048 };
		var secondTarget = Part(new AssetKey(TargetUnit.TypeId, 0x201), 3, "second") with { PartKind = UnitMeshPartKind.LeftArm, StoredBytes = 1024 };

		var plan = await CreateService().CreatePlanAsync(
			[Entry("source", source)], [Entry("target-1", firstTarget), Entry("target-2", secondTarget)], "source", UnitMeshBodyVariant.Any,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target-1", "target-2"]);

		Assert.Equal(2, plan.Mappings.Count(mapping => mapping.WillReplace));
	}

	[Fact]
	public async Task CreatePlanAsync_SharedPhysicalTargetConsumesEveryAvailableTargetHit()
	{
		var source = Part(SourceUnit, 1, "source") with { StoredBytes = 1024 };
		var sharedTarget = Part(TargetUnit, 2, "shared") with { StoredBytes = 2048 };

		var plan = await CreateService().CreatePlanAsync(
			[Entry("source", source)], [Entry("target-1", sharedTarget), Entry("target-2", sharedTarget)], "source", UnitMeshBodyVariant.Any,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target-1", "target-2"]);

		var mapping = Assert.Single(plan.Mappings);
		Assert.Equal(2, mapping.UsedByArchiveIds.Count);
		Assert.Equal(2, mapping.HitCount);
	}

	[Fact]
	public async Task CreatePlanAsync_ManualModeDoesNotCreateAutomaticMapping()
	{
		var source = Part(SourceUnit, 1, "source") with { StoredBytes = 1024 };
		var target = Part(TargetUnit, 2, "target");

		var plan = await CreateService().CreatePlanAsync(
			[Entry("source", source)], [Entry("target", target)], "source", UnitMeshBodyVariant.Any,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"], manualMode: true);

		var mapping = Assert.Single(plan.Mappings);
		Assert.Null(mapping.Source);
		Assert.Equal("隐藏", mapping.Reason);
	}

	[Fact]
	public async Task CreatePlanAsync_ManualMappingProducesForcedHit()
	{
		var source = Part(SourceUnit, 1, "source");
		var target = Part(TargetUnit, 2, "target");

		var plan = await CreateService().CreatePlanAsync(
			[Entry("source", source)], [Entry("target", target)], "source", UnitMeshBodyVariant.Any,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"],
			manualMappings: [new CrossArmorManualMapping(new CrossArmorPhysicalTargetKey(TargetUnit, 2), SourceUnit, 1)], manualMode: true);

		var mapping = Assert.Single(plan.Mappings);
		Assert.Equal(source, mapping.Source);
		Assert.True(mapping.IsManual);
		Assert.Equal("强制命中", mapping.Reason);
	}

	[Fact]
	public async Task CreatePlanAsync_AdditionalSelectedSourceIsIncluded()
	{
		var armorSource = Part(SourceUnit, 1, "armor") with { PartKind = UnitMeshPartKind.LeftArm, StoredBytes = 1024 };
		var helmetSource = Part(AdditionalSourceUnit, 2, "helmet") with { PartKind = UnitMeshPartKind.RightLeg, StoredBytes = 2048 };
		var target = Part(TargetUnit, 3, "target") with { PartKind = UnitMeshPartKind.RightLeg };

		var plan = await CreateService().CreatePlanAsync(
			[Entry("armor", armorSource), Entry("helmet", helmetSource)], [Entry("target", target)], "armor", UnitMeshBodyVariant.Any,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"], additionalSourceArchiveIds: ["helmet"]);

		Assert.Equal(helmetSource, Assert.Single(plan.Mappings).Source);
	}

	[Fact]
	public async Task CreatePlanAsync_ManualSuppressionProducesForcedHide()
	{
		var source = Part(SourceUnit, 1, "source");
		var target = Part(TargetUnit, 2, "target");

		var plan = await CreateService().CreatePlanAsync(
			[Entry("source", source)], [Entry("target", target)], "source", UnitMeshBodyVariant.Any,
			CrossArmorBodyVariantPreference.Slim, CrossArmorLayerPreference.Armor, ["target"],
			manualSuppressions: [new CrossArmorManualSuppression(new CrossArmorPhysicalTargetKey(TargetUnit, 2))]);

		var mapping = Assert.Single(plan.Mappings);
		Assert.True(mapping.IsSuppressed);
		Assert.Equal("强制隐藏", mapping.Reason);
	}

	private static EquipmentUnitCatalogService CreateService() => new(new StoragePaths(Path.GetTempPath()));
	private static EquipmentUnitCatalogEntry Entry(string id, params EquipmentUnitPart[] parts) => new(id, "Armor", id, parts);
	private static EquipmentUnitPart Part(AssetKey unit, int mesh, string name)
		=> new(unit, mesh, checked((uint)mesh), UnitMeshPartKind.RightLeg, UnitMeshPartLayer.Armor, UnitMeshBodyVariant.Slim, name, 100, Array.Empty<string>());
}

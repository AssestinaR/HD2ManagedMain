using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies compact list summaries use semantic archive names rather than asset tags or raw archive ids.
public sealed class ModAssetSummaryFormatterTests
{
	[Fact]
	public void Format_DeduplicatesSameArchiveNameAcrossArmorAndHelmet()
	{
		var summary = CreateSummary(
			Group("Armor", 0, Item("RE-2310 Honorary Guard", "armor-a")),
			Group("Helmet", 1, Item("RE-2310 Honorary Guard", "helmet-a")));

		Assert.Equal("RE-2310 Honorary Guard", ModAssetSummaryFormatter.Format(summary));
	}

	[Fact]
	public void Format_ShowsTwoSemanticArchiveNamesDespiteMultipleRawIds()
	{
		var summary = CreateSummary(
			Group("Armor", 0,
				Item("CW-22 Kodiak", "cw22-body", "cw22-helmet"),
				Item("CW-36 Winter Warrior", "cw36-body", "cw36-helmet")));

		Assert.Equal("CW-22 Kodiak · CW-36 Winter Warrior", ModAssetSummaryFormatter.Format(summary));
	}

	[Fact]
	public void Format_WhenMoreThanThreeNames_CountsNamesPerCategory()
	{
		var armor = Enumerable.Range(1, 100).Select(index => Item($"Armor {index}", $"armor-{index}")).ToArray();
		var helmets = Enumerable.Range(1, 100).Select(index => Item($"Helmet {index}", $"helmet-{index}")).ToArray();
		var summary = CreateSummary(Group("Armor", 0, armor), Group("Helmet", 1, helmets));

		Assert.Equal("armor(100) · helmet(100)", ModAssetSummaryFormatter.Format(summary));
	}

	[Fact]
	public void Format_LimitsCategorySummaryToThreeCategories()
	{
		var summary = CreateSummary(
			Group("Armor", 0, Item("Armor A", "a")),
			Group("Helmet", 1, Item("Helmet A", "h")),
			Group("Cape", 2, Item("Cape A", "c")),
			Group("Weapon", 3, Item("Weapon A", "w")));

		Assert.Equal("armor(1) · helmet(1) · cape(1)", ModAssetSummaryFormatter.Format(summary));
	}

	private static ModAssetSummary CreateSummary(params ModAssetTargetGroup[] groups)
		=> new(ModNodeId.New(), "Mod", Array.Empty<PatchAssetEntry>(), Array.Empty<string>(), groups);

	private static ModAssetTargetGroup Group(string category, int order, params ModAssetTargetItem[] items)
		=> new(category, order, items, items.Sum(item => item.AssetCount));

	private static ModAssetTargetItem Item(string displayName, params string[] archiveIds)
		=> new(displayName, 0, archiveIds, new[] { "unit" }, 1);
}

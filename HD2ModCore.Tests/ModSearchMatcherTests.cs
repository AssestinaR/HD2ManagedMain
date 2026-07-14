using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies cached mod search across metadata, assets, Chinese aliases, and multi-term queries.
public sealed class ModSearchMatcherTests
{
	[Fact]
	public void IsMatch_SearchesNameAndNotesWithoutAssetSummary()
	{
		Assert.True(ModSearchMatcher.IsMatch("Chiffon", "light armor replacement", null, "chiff"));
		Assert.True(ModSearchMatcher.IsMatch("Chiffon", "light armor replacement", null, "replacement"));
		Assert.False(ModSearchMatcher.IsMatch("Chiffon", "light armor replacement", null, "helmet"));
	}

	[Theory]
	[InlineData("护甲")]
	[InlineData("盔甲")]
	[InlineData("身体")]
	[InlineData("甲")]
	public void IsMatch_ExpandsChineseArmorAliases(string query)
	{
		Assert.True(ModSearchMatcher.IsMatch("Mod", null, CreateSummary("armor", "B-08 Light Gunner"), query));
	}

	[Fact]
	public void IsMatch_UsesAndSemanticsAcrossAssetTerms()
	{
		var summary = CreateSummary("helmet", "Hero Helmet Material");

		Assert.True(ModSearchMatcher.IsMatch("Mod", null, summary, "头盔 材质"));
		Assert.False(ModSearchMatcher.IsMatch("Mod", null, summary, "头盔 音频"));
	}

	private static ModAssetSummary CreateSummary(string tag, string displayName)
	{
		var entry = new PatchAssetEntry(
			new PatchAssetKey("archive", 1, 2),
			displayName,
			tag,
			0,
			0,
			displayName,
			"Material",
			AssetTypeCategory.Unknown,
			new[] { tag, "material" },
			Array.Empty<string>());
		return new ModAssetSummary(ModNodeId.New(), "Mod", new[] { entry }, new[] { tag, "material" }, Array.Empty<ModAssetTargetGroup>());
	}
}

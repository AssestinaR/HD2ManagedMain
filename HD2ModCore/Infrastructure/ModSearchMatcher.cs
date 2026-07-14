using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Matches user queries against cached mod metadata and derived asset facts without file-system access.
public static class ModSearchMatcher
{
	private static readonly IReadOnlyDictionary<string, string> AliasToCanonical = BuildAliasMap();

	public static bool IsMatch(string? name, string? notes, ModAssetSummary? assetSummary, string? query)
	{
		var terms = SplitTerms(query);
		if (terms.Count == 0)
		{
			return true;
		}

		var searchable = BuildSearchableValues(name, notes, assetSummary);
		return terms.All(term => MatchesTerm(searchable, term));
	}

	private static IReadOnlyList<string> SplitTerms(string? query)
		=> string.IsNullOrWhiteSpace(query)
			? Array.Empty<string>()
			: query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static IReadOnlyList<string> BuildSearchableValues(string? name, string? notes, ModAssetSummary? summary)
	{
		var values = new List<string>();
		Add(values, name);
		Add(values, notes);
		if (summary is null)
		{
			return values;
		}

		foreach (var tag in summary.DerivedTags) Add(values, tag);
		foreach (var asset in summary.Assets)
		{
			Add(values, asset.DisplayName);
			Add(values, asset.ArchiveDisplayName);
			Add(values, asset.ArchiveCategory);
			Add(values, asset.FileDisplayName);
			Add(values, asset.TypeDisplayName);
			foreach (var tag in asset.DerivedTags) Add(values, tag);
		}
		return values;
	}

	private static bool MatchesTerm(IReadOnlyList<string> searchable, string term)
	{
		var normalized = term.Trim().ToLowerInvariant();
		var canonical = AliasToCanonical.TryGetValue(normalized, out var mapped) ? mapped : normalized;
		return searchable.Any(value => value.Contains(normalized, StringComparison.OrdinalIgnoreCase)
			|| (!string.Equals(canonical, normalized, StringComparison.Ordinal) && value.Contains(canonical, StringComparison.OrdinalIgnoreCase)));
	}

	private static void Add(ICollection<string> values, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
	}

	private static IReadOnlyDictionary<string, string> BuildAliasMap()
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		AddAliases(result, "armor", "armor", "armour", "护甲", "盔甲", "装甲", "身体", "躯干", "甲");
		AddAliases(result, "helmet", "helmet", "头盔", "头部", "头");
		AddAliases(result, "cape", "cape", "披风", "斗篷");
		AddAliases(result, "weapon", "weapon", "武器", "枪", "主武器", "副武器");
		AddAliases(result, "material", "material", "材质");
		AddAliases(result, "texture", "texture", "贴图", "纹理");
		AddAliases(result, "audio", "audio", "声音", "音频", "语音", "音效");
		AddAliases(result, "model", "model", "模型", "网格");
		return result;
	}

	private static void AddAliases(IDictionary<string, string> target, string canonical, params string[] aliases)
	{
		foreach (var alias in aliases) target[alias] = canonical;
	}
}

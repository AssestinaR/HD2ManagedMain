using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Formats one cached mod asset summary into a compact list-row description.
public static class ModAssetSummaryFormatter
{
	public static string Format(ModAssetSummary? summary, int maxItems = 3)
	{
		if (summary is null) return "资产信息正在更新";
		if (maxItems <= 0) throw new ArgumentOutOfRangeException(nameof(maxItems));

		var semanticTargets = summary.TargetGroups
			.SelectMany(group => group.Items.Select(item => new SemanticTarget(group.Category, group.CategoryOrder, item.DisplayName, item.ArchiveOrder)))
			.Where(target => IsReadable(target.DisplayName))
			.ToList();
		var uniqueDisplayNames = semanticTargets
			.GroupBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.OrderBy(target => target.CategoryOrder).ThenBy(target => target.ArchiveOrder).First())
			.OrderBy(target => target.CategoryOrder)
			.ThenBy(target => target.ArchiveOrder)
			.ThenBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (uniqueDisplayNames.Count is > 0 and <= 3)
		{
			return string.Join(" · ", uniqueDisplayNames.Take(maxItems).Select(target => target.DisplayName));
		}

		if (uniqueDisplayNames.Count > 3)
		{
			var categories = semanticTargets
				.Where(target => IsReadable(target.Category))
				.GroupBy(target => target.Category, StringComparer.OrdinalIgnoreCase)
				.Select(group => new
				{
					Category = group.Key,
					CategoryOrder = group.Min(target => target.CategoryOrder),
					TargetCount = group.Select(target => target.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
				})
				.OrderBy(group => group.CategoryOrder)
				.ThenBy(group => group.Category, StringComparer.OrdinalIgnoreCase)
				.Take(maxItems)
				.Select(group => $"{group.Category.ToLowerInvariant()}({group.TargetCount})")
				.ToList();
			if (categories.Count > 0) return string.Join(" · ", categories);
		}

		if (summary.Assets.Count == 0) return "未发现可解析资产";
		// Target archives are optional GameData enrichment. Imported patch facts still carry
		// stable semantic tags and must remain useful while no archive mapping is available.
		var tags = summary.DerivedTags.Where(IsReadable).Take(maxItems).ToArray();
		return tags.Length != 0 ? string.Join(" · ", tags) : "已解析资产";
	}

	private static bool IsReadable(string value)
	{
		var normalized = value.Trim();
		return normalized.Length > 0
			&& !string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase)
			&& !normalized.All(char.IsDigit)
			&& !(normalized.Length is 16 or 18 && normalized.TrimStart('0', 'x').All(Uri.IsHexDigit));
	}

	private sealed record SemanticTarget(string Category, int CategoryOrder, string DisplayName, int ArchiveOrder);
}

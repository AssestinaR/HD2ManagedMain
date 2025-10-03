namespace HD2ModCore.Domain;

public sealed record IndexFilterSettings(
	IndexFilterMode Mode,
	double? PercentageThreshold,
	int? AbsoluteThreshold)
{
	public static IndexFilterSettings Default => new(IndexFilterMode.Percentage, PercentageThreshold: 0.25, AbsoluteThreshold: null);
}

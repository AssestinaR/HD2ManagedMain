namespace HD2ModCore.Domain;

// Purpose: Represents the independent, lightweight Unit-version compatibility evidence for one Mod.
public enum UnitCompatibilityStatus
{
	NotAnalyzed = 0,
	NoUnits = 1,
	CurrentCandidate = 2,
	OutdatedConfirmed = 3,
	Unreadable = 4,
}

public sealed record UnitVersionEvidence(
	string PatchFileName,
	AssetKey UnitAssetKey,
	uint? Version,
	string? ReadError = null)
{
	public bool IsReadable => Version.HasValue && string.IsNullOrWhiteSpace(ReadError);
}

public sealed record ModUnitCompatibilityReport(
	UnitCompatibilityStatus Status,
	int UnitCount,
	int ReadableUnitCount,
	int OutdatedUnitCount,
	IReadOnlyList<UnitVersionEvidence> Evidence)
{
	public const uint CurrentUnitVersion = 0x00a4cd36;
	public bool IsOutdated => Status == UnitCompatibilityStatus.OutdatedConfirmed;
	public string Summary => Status switch
	{
		UnitCompatibilityStatus.OutdatedConfirmed => $"发现 {OutdatedUnitCount} 个旧版 Unit。",
		UnitCompatibilityStatus.CurrentCandidate => $"{ReadableUnitCount} 个 Unit 使用当前已知版本。",
		UnitCompatibilityStatus.NoUnits => "不含 Unit，不适用模型版本检测。",
		UnitCompatibilityStatus.Unreadable => "含无法读取的 Unit，模型版本未确认。",
		_ => "模型版本尚未检测。"
	};

	public static ModUnitCompatibilityReport FromEvidence(IEnumerable<UnitVersionEvidence> evidence)
	{
		var values = evidence.ToArray();
		var readable = values.Count(item => item.IsReadable);
		var outdated = values.Count(item => item.Version is { } version && version < CurrentUnitVersion);
		var status = values.Length == 0
			? UnitCompatibilityStatus.NoUnits
			: outdated > 0
				? UnitCompatibilityStatus.OutdatedConfirmed
				: readable == values.Length && values.All(item => item.Version == CurrentUnitVersion)
					? UnitCompatibilityStatus.CurrentCandidate
					: UnitCompatibilityStatus.Unreadable;
		return new ModUnitCompatibilityReport(status, values.Length, readable, outdated, values);
	}
}
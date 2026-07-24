using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：承载独立 Unit 版本信息产品及其输入 generation。
// Purpose: Carries the independent Unit-version information product and input generation.
public sealed record ModUnitVersionFacts(
	ModNodeId NodeId,
	string RelativePath,
	string Generation,
	DateTimeOffset BuiltUtc,
	ModUnitCompatibilityReport Report,
	IReadOnlyList<CoreIssue> Issues);

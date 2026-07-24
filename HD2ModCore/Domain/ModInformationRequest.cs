namespace HD2ModCore.Domain;

// 作用：统一描述一次 Mod 信息产品请求及其来源。
// Purpose: Describes a Mod information request and its business source.
public sealed record ModInformationRequest(
	ModInformationKind Kind,
	string Source,
	string? Generation = null,
	bool RequireFresh = false);
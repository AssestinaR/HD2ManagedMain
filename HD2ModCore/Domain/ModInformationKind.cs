namespace HD2ModCore.Domain;

// 作用：标识 Mod 信息产品等级，避免使用含义不明确的“完整分析”请求。
// Purpose: Identifies a Mod information product without ambiguous full-analysis requests.
public enum ModInformationKind
{
	FileFacts,
	AssetInventory,
	ReferenceGraph,
	UnitVersion,
	AdvancedUnitAnalysis,
	Thumbnail,
	MaintenanceAnalysis,
}
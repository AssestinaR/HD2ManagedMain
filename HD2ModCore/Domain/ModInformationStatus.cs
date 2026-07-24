namespace HD2ModCore.Domain;

// 作用：描述信息产品本次返回的数据状态。
// Purpose: Describes the data state returned for an information product request.
public enum ModInformationStatus
{
	Fresh,
	Cached,
	Stale,
	Failed,
	Unavailable,
}
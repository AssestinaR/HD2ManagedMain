using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：根据对象的资产键集合（AssetKeySet）推导其可能替换的原版内容（按 archive 投票排序）。
// Purpose: Derives which base-game content an object likely replaces from its AssetKey set (ranked by archive votes).
public interface IReplacementTargetDeriver
{
	ValueTask<ReplacementTargetsResult> DeriveAsync(
		IReadOnlySet<AssetKey> assetKeys,
		IndexFilterSettings filterSettings,
		int topN = 5,
		CancellationToken cancellationToken = default);
}

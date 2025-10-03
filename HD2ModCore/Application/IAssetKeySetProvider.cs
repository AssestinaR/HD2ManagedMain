using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：为对象节点提供资产键集合（AssetKeySet），用于冲突检测与替换目标推导等。
// Purpose: Provides an AssetKey set for a mod node, used for conflict detection and replacement target derivation.
public interface IAssetKeySetProvider
{
	ValueTask<IReadOnlySet<AssetKey>> GetAssetKeysAsync(
		ModNode node,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}

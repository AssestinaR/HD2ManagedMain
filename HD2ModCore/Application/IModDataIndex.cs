using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：从已持久化的信息产品投影构建只读跨 Mod 资产索引。
// Purpose: Builds a read-only cross-Mod asset index from persisted information products.
public interface IModDataIndex
{
	ValueTask<IReadOnlyList<ModDataIndexEntry>> FindProvidersAsync(AssetKey assetKey, CancellationToken cancellationToken = default);
	ValueTask<IReadOnlyList<ModDataIndexEntry>> FindConsumersAsync(AssetKey assetKey, CancellationToken cancellationToken = default);
	ValueTask<ModDataIndexEntry?> ResolveFinalProviderAsync(AssetKey assetKey, Profile profile, CancellationToken cancellationToken = default);
	ValueTask RemoveNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default);
	void Update(ModContentFacts inventory);
	void Update(ReferenceGraphFacts graph);
}

public sealed record ModDataIndexEntry(ModNodeId NodeId, string RelativePath, string SourceArchiveHex, int PatchIndex, AssetKey AssetKey, string Relation);

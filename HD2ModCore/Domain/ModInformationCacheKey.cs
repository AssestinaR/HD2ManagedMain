namespace HD2ModCore.Domain;

// 作用：组合属性、内容修订、视图和选择器，形成不会误命中旧内容的缓存键。
// Purpose: Combines property, content revision, view, and selector into a collision-resistant cache key.
public readonly record struct ModInformationCacheKey(
	ModNodeId NodeId,
	ModInformationPropertyKind Property,
	string RevisionKey,
	ModInformationContentView ContentView = ModInformationContentView.Effective,
	string SelectorKey = "")
{
	public static ModInformationCacheKey Create(
		ModNodeId nodeId,
		ModInformationPropertyKind property,
		ModContentRevision revision,
		ModInformationContentView contentView = ModInformationContentView.Effective,
		ModInformationSelector? selector = null)
	{
		ArgumentNullException.ThrowIfNull(revision);
		return new ModInformationCacheKey(nodeId, property, revision.CacheKey, contentView, selector?.ToCacheKey() ?? string.Empty);
	}

	public override string ToString()
		=> $"{NodeId.Value:N}|{Property}|{ContentView}|{RevisionKey}|{SelectorKey}";
}

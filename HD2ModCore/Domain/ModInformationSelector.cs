namespace HD2ModCore.Domain;

// 作用：限定一次属性读取的 Patch、Asset 或 Mesh 范围；空集合表示不限制该维度。
// Purpose: Selects a Patch, Asset, or Mesh subset for one property read; an empty set means unrestricted.
public sealed record ModInformationSelector(
	IReadOnlyList<PatchGroupKey>? PatchGroups = null,
	IReadOnlyList<AssetKey>? AssetKeys = null,
	IReadOnlyList<int>? MeshInfoIndices = null,
	IReadOnlyList<string>? ArchiveIds = null,
	bool IncludeDependencies = false,
	bool IncludeReverseReferences = false)
{
	public static ModInformationSelector All { get; } = new();

	public IReadOnlyList<PatchGroupKey> SelectedPatchGroups => PatchGroups ?? Array.Empty<PatchGroupKey>();
	public IReadOnlyList<AssetKey> SelectedAssetKeys => AssetKeys ?? Array.Empty<AssetKey>();
	public IReadOnlyList<int> SelectedMeshInfoIndices => MeshInfoIndices ?? Array.Empty<int>();
	public IReadOnlyList<string> SelectedArchiveIds => ArchiveIds ?? Array.Empty<string>();

	public bool IsUnrestricted
		=> SelectedPatchGroups.Count == 0
			&& SelectedAssetKeys.Count == 0
			&& SelectedMeshInfoIndices.Count == 0
			&& SelectedArchiveIds.Count == 0
			&& !IncludeDependencies
			&& !IncludeReverseReferences;

	// 作用：生成稳定的选择器键，供流程内和持久化缓存区分不同子集。
	// Purpose: Produces a stable selector key so caches distinguish different subsets.
	public string ToCacheKey()
	{
		var groups = SelectedPatchGroups
			.OrderBy(group => group.ArchiveHex16, StringComparer.OrdinalIgnoreCase)
			.ThenBy(group => group.PatchIndex)
			.Select(group => $"{group.ArchiveHex16.ToLowerInvariant()}:{group.PatchIndex}");
		var assets = SelectedAssetKeys
			.OrderBy(asset => asset.TypeId)
			.ThenBy(asset => asset.FileId)
			.Select(asset => $"{asset.TypeId:x16}/{asset.FileId:x16}");
		var meshes = SelectedMeshInfoIndices.OrderBy(index => index).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture));
		var archives = SelectedArchiveIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Select(value => value.ToLowerInvariant());
		return string.Join('|',
			$"patch={string.Join(',', groups)}",
			$"asset={string.Join(',', assets)}",
			$"mesh={string.Join(',', meshes)}",
			$"archive={string.Join(',', archives)}",
			$"deps={(IncludeDependencies ? 1 : 0)}",
			$"reverse={(IncludeReverseReferences ? 1 : 0)}");
	}
}

using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Analysis;

// Purpose: Detects conservative resource reuse between Items without merging unrelated texture-only relationships.
public sealed class ResourceReuseDetector : IResourceReuseDetector
{
	public IReadOnlyList<ResourceReuseGroup> Detect(
		IReadOnlyList<GameItemResourceInfo> items,
		string sourceFingerprint)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);

		var groups = new List<ResourceReuseGroup>();
		foreach (var resourceGroup in items
			.SelectMany(item => item.Resources.Where(resource => resource.IsResolved)
				.Select(resource => (Item: item, Resource: resource)))
			.GroupBy(entry => entry.Resource.AssetKey))
		{
			var members = resourceGroup.Select(entry => entry.Item.ItemName).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
			if (members.Length < 2) continue;

			var level = GetLevel(resourceGroup.Key.TypeId, resourceGroup.Select(entry => entry.Resource).ToArray());
			groups.Add(new ResourceReuseGroup(
				$"{level}:{resourceGroup.Key.TypeId:x16}:{resourceGroup.Key.FileId:x16}",
				members,
				resourceGroup.Key,
				level,
				GetConfidence(level),
				GetExplanation(level, resourceGroup.Key),
				sourceFingerprint));
		}

		return groups
			.OrderBy(group => group.Level)
			.ThenBy(group => group.SharedAsset.TypeId)
			.ThenBy(group => group.SharedAsset.FileId)
			.ToArray();
	}

	private static ResourceReuseLevel GetLevel(ulong typeId, IReadOnlyList<ResourceDependencyFact> resources) => typeId switch
	{
		PatchUnitMeshReader.UnitTypeId => ResourceReuseLevel.ExactUnitReuse,
		PatchUnitMeshReader.CompositeUnitTypeId => ResourceReuseLevel.CompositeReuse,
		MaterialDependencyResolver.MaterialTypeId => ResourceReuseLevel.MaterialReuse,
		MaterialDependencyResolver.TextureTypeId => ResourceReuseLevel.TextureOnlyReuse,
		_ => resources.Any(resource => resource.ResourceKind.Equals("Mesh", StringComparison.OrdinalIgnoreCase))
			? ResourceReuseLevel.MeshReuse
			: ResourceReuseLevel.TextureOnlyReuse
	};

	private static ResourceReuseConfidence GetConfidence(ResourceReuseLevel level) => level switch
	{
		ResourceReuseLevel.ExactUnitReuse => ResourceReuseConfidence.High,
		ResourceReuseLevel.CompositeReuse or ResourceReuseLevel.MeshReuse => ResourceReuseConfidence.Medium,
		_ => ResourceReuseConfidence.Low
	};

	private static string GetExplanation(ResourceReuseLevel level, AssetKey asset) => level switch
	{
		ResourceReuseLevel.ExactUnitReuse => $"Items directly reference the same Unit 0x{asset.FileId:x16}.",
		ResourceReuseLevel.CompositeReuse => $"Items reference the same Composite Unit 0x{asset.FileId:x16}.",
		ResourceReuseLevel.MeshReuse => $"Items share an identified mesh resource 0x{asset.FileId:x16}.",
		ResourceReuseLevel.MaterialReuse => $"Items share Material 0x{asset.FileId:x16}; material changes may affect both.",
		_ => $"Items share Texture 0x{asset.FileId:x16}; this is informational and does not imply model reuse."
	};
}

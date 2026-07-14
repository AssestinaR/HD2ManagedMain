using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Analysis;

// Purpose: Builds a verified, payload-backed resource graph for selected Unit and Material roots.
public sealed class GameDataResourceGraphBuilder : IGameDataResourceGraphBuilder
{
	private const int BoneReferenceOffset = 8;
	private const int CompositeReferenceOffset = 16;
	private readonly Func<string, IGameDataPackageResolver> resolverFactory;
	private readonly StingrayMaterialReferenceReader materialReader;
	private readonly Func<PatchEntryPayload, IReadOnlyCollection<ulong>> unitMaterialReader;

	public GameDataResourceGraphBuilder(
		Func<string, IGameDataPackageResolver>? resolverFactory = null,
		IPatchTocScanner? tocScanner = null,
		StingrayMaterialReferenceReader? materialReader = null,
		Func<PatchEntryPayload, IReadOnlyCollection<ulong>>? unitMaterialReader = null)
	{
		this.resolverFactory = resolverFactory ?? (directory => new GameDataPackageResolver(directory));
		this.materialReader = materialReader ?? new StingrayMaterialReferenceReader();
		this.unitMaterialReader = unitMaterialReader ?? (payload => new UnitMeshReader().Read(payload.TocData, payload.GpuResourceData).Materials.Select(material => material.MaterialId).Distinct().ToArray());
	}

	public async ValueTask<GameDataResourceGraph> BuildAsync(
		GameDataArchiveIndex archiveIndex,
		IReadOnlyCollection<AssetKey> rootAssets,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(archiveIndex);
		ArgumentNullException.ThrowIfNull(rootAssets);
		var resolver = resolverFactory(archiveIndex.Input.GameDataDirectory);
		var nodes = new Dictionary<AssetKey, GameDataResourceNode>();
		var edges = new List<GameDataResourceEdge>();
		var issues = new List<PatchAnalysisIssue>();
		var visited = new HashSet<AssetKey>();
		foreach (var root in rootAssets.Distinct())
		{
			cancellationToken.ThrowIfCancellationRequested();
			await VisitAsync(root).ConfigureAwait(false);
		}
		return new GameDataResourceGraph(nodes.Values.ToArray(), edges.Distinct().ToArray(), issues);

		async ValueTask VisitAsync(AssetKey key)
		{
			if (!visited.Add(key)) return;
			var matches = archiveIndex.FindArchivesByAsset(key).ToArray();
			var match = matches.FirstOrDefault();
			var kind = GetResourceKind(key.TypeId);
			if (match is null)
			{
				nodes[key] = new GameDataResourceNode(key, kind, null, false);
				issues.Add(new PatchAnalysisIssue("MissingResourcePayload", $"Resource 0x{key.TypeId:x16}/0x{key.FileId:x16} is absent from the archive index.", null, key));
				return;
			}
			if (matches.Length > 1)
			{
				issues.Add(new PatchAnalysisIssue("AmbiguousResourceArchive", $"Resource 0x{key.TypeId:x16}/0x{key.FileId:x16} exists in {matches.Length} archives; the first indexed archive was used for payload inspection.", match.PackageName, key));
			}
			nodes[key] = new GameDataResourceNode(key, kind, match.PackageName, true);
			var payload = await TryReadPayloadAsync(resolver, match, cancellationToken).ConfigureAwait(false);
			if (payload is null)
			{
				issues.Add(new PatchAnalysisIssue("MissingResourcePayload", $"Resource payload could not be read from archive '{match.PackageName}'.", match.PackageName, key));
				return;
			}
			try
			{
				foreach (var dependency in ReadDependencies(key, payload))
				{
					var resolved = archiveIndex.FindArchivesByAsset(dependency).Any();
					edges.Add(new GameDataResourceEdge(key, dependency, GetRelation(key.TypeId, dependency.TypeId), resolved));
					await VisitAsync(dependency).ConfigureAwait(false);
				}
			}
			catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or OverflowException)
			{
				issues.Add(new PatchAnalysisIssue("InvalidResourcePayload", exception.Message, match.PackageName, key));
			}
		}
	}

	private async ValueTask<PatchEntryPayload?> TryReadPayloadAsync(IGameDataPackageResolver resolver, GameDataArchiveEntryFact entry, CancellationToken cancellationToken)
	{
		var tocData = await ReadAsync(resolver, entry.PackageName, entry.TocDataOffset, entry.TocDataSize, cancellationToken).ConfigureAwait(false);
		if (tocData is null) return null;
		var streamData = await ReadAsync(resolver, entry.PackageName + ".stream", entry.StreamOffset, entry.StreamSize, cancellationToken).ConfigureAwait(false) ?? Array.Empty<byte>();
		var gpuData = await ReadAsync(resolver, entry.PackageName + ".gpu_resources", entry.GpuResourceOffset, entry.GpuResourceSize, cancellationToken).ConfigureAwait(false) ?? Array.Empty<byte>();
		var patchEntry = new PatchTocEntry(entry.AssetKey, entry.PackageName, entry.PackageName, entry.TocDataOffset, entry.StreamOffset, entry.GpuResourceOffset, entry.Unknown1, entry.Unknown2, entry.TocDataSize, entry.StreamSize, entry.GpuResourceSize, entry.Unknown3, entry.Unknown4, entry.EntryIndex);
		return new PatchEntryPayload(patchEntry, tocData, streamData, gpuData);
	}

	private static async ValueTask<byte[]?> ReadAsync(IGameDataPackageResolver resolver, string packageName, ulong offset, uint size, CancellationToken cancellationToken)
	{
		if (size == 0) return Array.Empty<byte>();
		var data = await resolver.GetPackageResourceAsync(packageName, offset, size, cancellationToken).ConfigureAwait(false);
		return data is null || data.Length < size ? null : data.Length == size ? data : data.AsSpan(0, checked((int)size)).ToArray();
	}

	private IEnumerable<AssetKey> ReadDependencies(AssetKey key, PatchEntryPayload payload)
	{
		if (key.TypeId == PatchUnitMeshReader.UnitTypeId)
		{
			foreach (var materialId in unitMaterialReader(payload).Distinct())
				if (materialId != 0) yield return new AssetKey(MaterialDependencyResolver.MaterialTypeId, materialId);

			foreach (var (offset, typeId) in new[] { (BoneReferenceOffset, PatchUnitMeshReader.BoneTypeId), (CompositeReferenceOffset, PatchUnitMeshReader.CompositeUnitTypeId) })
			{
				if (payload.TocData.Length < offset + 8) throw new InvalidDataException($"Unit payload is too short to read reference at offset {offset}.");
				var fileId = BinaryPrimitives.ReadUInt64LittleEndian(payload.TocData.AsSpan(offset, 8));
				if (fileId != 0) yield return new AssetKey(typeId, fileId);
			}
		}
		else if (key.TypeId == MaterialDependencyResolver.MaterialTypeId)
		{
			foreach (var textureId in materialReader.ReadTextureIds(payload.TocData).Distinct())
				if (textureId != 0) yield return new AssetKey(MaterialDependencyResolver.TextureTypeId, textureId);
		}
	}

	private static string GetResourceKind(ulong typeId) => typeId switch
	{
		PatchUnitMeshReader.UnitTypeId => "Unit",
		PatchUnitMeshReader.CompositeUnitTypeId => "Composite",
		PatchUnitMeshReader.BoneTypeId => "Bone",
		MaterialDependencyResolver.MaterialTypeId => "Material",
		MaterialDependencyResolver.TextureTypeId => "Texture",
		_ => "Asset"
	};

	private static string GetRelation(ulong fromTypeId, ulong toTypeId) => fromTypeId switch
	{
		PatchUnitMeshReader.UnitTypeId when toTypeId == PatchUnitMeshReader.CompositeUnitTypeId => "CompositeReference",
		PatchUnitMeshReader.UnitTypeId when toTypeId == PatchUnitMeshReader.BoneTypeId => "BoneReference",
		MaterialDependencyResolver.MaterialTypeId when toTypeId == MaterialDependencyResolver.TextureTypeId => "TextureReference",
		_ => "Reference"
	};
}

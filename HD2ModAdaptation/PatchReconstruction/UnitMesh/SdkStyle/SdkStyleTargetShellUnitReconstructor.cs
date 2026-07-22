namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Rebuilds one current target Unit from explicit source mappings, SDK-style vertex re-encoding, and minify-only coverage for every remaining target mesh.
public sealed class SdkStyleTargetShellUnitReconstructor
{
	private readonly PlaceholderUnitMeshMinifier minifier;
	private readonly SdkStyleVertexStreamPlanner streamPlanner;
	private readonly SdkStyleMeshReencoder reencoder;
	private readonly UnitMeshWriter writer;
	private readonly ICurrentGameStreamLayoutRegistry? streamLayoutRegistry;
	private readonly bool planSourceStreamLayout;
	private readonly bool planCanonicalSkinningLayout;

	public SdkStyleTargetShellUnitReconstructor(
		PlaceholderUnitMeshMinifier? minifier = null,
		SdkStyleVertexStreamPlanner? streamPlanner = null,
		SdkStyleMeshReencoder? reencoder = null,
		UnitMeshWriter? writer = null,
		bool allowSectionRebuild = false,
		bool propagateSourceMaterials = true,
		IReadOnlySet<ulong>? allowedSourceMaterialIds = null,
		bool planSourceStreamLayout = false,
		bool planCanonicalSkinningLayout = false,
		ICurrentGameStreamLayoutRegistry? streamLayoutRegistry = null)
	{
		if (planSourceStreamLayout && planCanonicalSkinningLayout) throw new ArgumentException("Source stream planning and canonical skinning planning cannot be enabled together.");
		this.minifier = minifier ?? new PlaceholderUnitMeshMinifier();
		this.streamPlanner = streamPlanner ?? new SdkStyleVertexStreamPlanner();
		this.planSourceStreamLayout = planSourceStreamLayout;
		this.planCanonicalSkinningLayout = planCanonicalSkinningLayout;
		this.streamLayoutRegistry = streamLayoutRegistry;
		this.reencoder = reencoder ?? new SdkStyleMeshReencoder(
			allowSectionRebuild: allowSectionRebuild,
			propagateSourceMaterials: propagateSourceMaterials,
			transformMeshSpace: allowSectionRebuild,
			allowedSourceMaterialIds: allowedSourceMaterialIds);
		this.writer = writer ?? new UnitMeshWriter();
	}

	public SdkStyleTargetShellUnitReconstructionResult Reconstruct(
		GameDataUnitMesh targetUnit,
		IReadOnlyCollection<PatchUnitMesh> sourceUnits,
		IReadOnlyCollection<TargetShellMeshMapping> mappings)
	{
		ArgumentNullException.ThrowIfNull(targetUnit);
		ArgumentNullException.ThrowIfNull(sourceUnits);
		ArgumentNullException.ThrowIfNull(mappings);

		var sourceByKey = sourceUnits.ToDictionary(unit => unit.Entry.AssetKey);
		var targetIndexes = new HashSet<int>();
		foreach (var mapping in mappings)
		{
			if (!targetIndexes.Add(mapping.TargetMeshInfoIndex)) throw new InvalidDataException($"Target mesh {mapping.TargetMeshInfoIndex} has more than one source mapping.");
			if (!sourceByKey.ContainsKey(mapping.SourceUnitAssetKey)) throw new KeyNotFoundException($"Source Unit 0x{mapping.SourceUnitAssetKey.FileId:x16} was not supplied.");
			if (!targetUnit.Model.RawMeshData.Any(mesh => mesh.MeshInfoIndex == mapping.TargetMeshInfoIndex)) throw new KeyNotFoundException($"Target Unit does not contain mesh {mapping.TargetMeshInfoIndex}.");
		}
		// A source patch can use a legacy Unit format table. Its numeric vertex-format IDs
		// cannot be copied into a current target Unit: the same ID has different semantics
		// between versions. Keep the current target declaration unless a caller explicitly
		// opts into a version-aware stream migration.
		var plannedTargetModel = planCanonicalSkinningLayout
			? streamPlanner.PlanCanonicalSkinning(targetUnit.Model, mappings.Select(mapping => new SdkStyleStreamReplacement(
				mapping.TargetMeshInfoIndex,
				sourceByKey[mapping.SourceUnitAssetKey].Model,
				mapping.SourceMeshInfoIndex)).ToArray(), streamLayoutRegistry)
			: planSourceStreamLayout
			? streamPlanner.Plan(targetUnit.Model, mappings.Select(mapping => new SdkStyleStreamReplacement(
				mapping.TargetMeshInfoIndex,
				sourceByKey[mapping.SourceUnitAssetKey].Model,
				mapping.SourceMeshInfoIndex)).ToArray())
			: targetUnit.Model;
		var allStreamCanonicalModel = planCanonicalSkinningLayout
			? streamPlanner.CanonicalizeAllSkinningStreams(plannedTargetModel)
			: plannedTargetModel;
		var preservedMeshIndexes = targetIndexes.Count == 0
			? targetIndexes
			: targetIndexes.Concat(allStreamCanonicalModel.RawMeshData
				.Where(mesh => mesh.LodIndex == -1)
				.Select(mesh => mesh.MeshInfoIndex))
			.ToHashSet();
		var model = targetIndexes.Count == 0
			? minifier.MinifyAll(allStreamCanonicalModel)
			: minifier.MinifyExcept(allStreamCanonicalModel, preservedMeshIndexes);
		model = NormalizeMappedLodSectionLayouts(model, targetIndexes);
		var rebuiltBoneInfoIndexes = new HashSet<int>();
		var replacementMaterialIds = new HashSet<ulong>();
		foreach (var mapping in mappings.OrderBy(mapping => mapping.TargetMeshInfoIndex))
		{
			var sourceUnit = sourceByKey[mapping.SourceUnitAssetKey];
			var preserveSourceSectionMetadata = mapping.SourceUnitAssetKey == targetUnit.AssetKey;
			var result = reencoder.Reencode(model, mapping.TargetMeshInfoIndex, sourceUnit.Model, mapping.SourceMeshInfoIndex, preserveSourceSectionMetadata);
			model = result.Model;
			if (preserveSourceSectionMetadata)
			{
				model = PreserveSourceCullingProxy(model, sourceUnit.Model);
			}
			rebuiltBoneInfoIndexes.Add(result.TargetBoneInfoIndex);
			foreach (var materialId in result.SourceMaterialIds) replacementMaterialIds.Add(materialId);
		}

		var targetMeshIndexes = targetUnit.Model.RawMeshData.Select(mesh => mesh.MeshInfoIndex).ToHashSet();
		var coveredIndexes = model.RawMeshData.Select(mesh => mesh.MeshInfoIndex).ToHashSet();
		if (!targetMeshIndexes.SetEquals(coveredIndexes)) throw new InvalidDataException("The reconstructed target shell does not cover every current target RawMesh.");
		var replacements = mappings.Select(mapping => mapping.TargetMeshInfoIndex).ToHashSet();
		var minified = targetMeshIndexes.Where(index => !preservedMeshIndexes.Contains(index)).OrderBy(index => index).ToArray();
		if (minified.Any(index => !IsPlaceholder(model.RawMeshData.Single(mesh => mesh.MeshInfoIndex == index)))) throw new InvalidDataException("An unreplaced target mesh was not reduced to a placeholder.");

		var write = targetUnit.CompositePayload is null
			? writer.Write(model, targetUnit.Payload.TocData)
			: writer.Write(model, targetUnit.Payload.TocData, targetUnit.CompositePayload.TocData);
		return new SdkStyleTargetShellUnitReconstructionResult(model, write, mappings.ToArray(), minified, rebuiltBoneInfoIndexes.OrderBy(index => index).ToArray(), replacementMaterialIds.OrderBy(id => id).ToArray());
	}

	private static bool IsPlaceholder(UnitRawMeshData mesh)
		=> mesh.Vertices.Count <= 3 && mesh.Triangles.Count <= 1;

	private static UnitMeshModel PreserveSourceCullingProxy(UnitMeshModel targetModel, UnitMeshModel sourceModel)
	{
		var sourceProxies = sourceModel.RawMeshData.Where(mesh => mesh.LodIndex == -1).ToArray();
		var targetProxies = targetModel.RawMeshData.Where(mesh => mesh.LodIndex == -1).ToArray();
		if (sourceProxies.Length != 1 || targetProxies.Length != 1) return targetModel;
		var sourceProxy = sourceProxies[0];
		var targetProxy = targetProxies[0];
		if (sourceProxy is null || targetProxy is null) return targetModel;
		if (sourceProxy.StreamIndex != targetProxy.StreamIndex || sourceProxy.Sections.Count != targetProxy.Sections.Count) return targetModel;
		if (sourceProxy.Vertices.Count == 0 || sourceProxy.Triangles.Count == 0) return targetModel;
		var sourceStream = sourceModel.Streams.SingleOrDefault(stream => stream.Index == sourceProxy.StreamIndex);
		var targetStream = targetModel.Streams.SingleOrDefault(stream => stream.Index == targetProxy.StreamIndex);
		if (sourceStream is null || targetStream is null || sourceStream.VertexStride != targetStream.VertexStride || !sourceStream.Components.SequenceEqual(targetStream.Components)) return targetModel;
		return targetModel with
		{
			RawMeshData = targetModel.RawMeshData.Select(mesh => mesh.MeshInfoIndex == targetProxy.MeshInfoIndex
				? sourceProxy with { MeshInfoIndex = targetProxy.MeshInfoIndex, MeshId = targetProxy.MeshId, LodIndex = targetProxy.LodIndex, StreamIndex = targetProxy.StreamIndex }
				: mesh).ToArray(),
			Meshes = targetModel.Meshes.Select(mesh => mesh.Index == targetProxy.MeshInfoIndex
				? mesh with { CullingBounds = sourceModel.Meshes.Single(source => source.Index == sourceProxy.MeshInfoIndex).CullingBounds }
				: mesh).ToArray()
		};
	}

	private static UnitMeshModel NormalizeMappedLodSectionLayouts(UnitMeshModel model, IReadOnlySet<int> mappedMeshIndexes)
	{
		var lod0 = model.RawMeshData.SingleOrDefault(mesh => mesh.LodIndex == 0 && mappedMeshIndexes.Contains(mesh.MeshInfoIndex));
		if (lod0 is null) return model;
		var canonicalSlots = model.Meshes.Single(mesh => mesh.Index == lod0.MeshInfoIndex).MaterialSlotIds;
		if (canonicalSlots.Count == 0) return model;

		var meshes = model.Meshes.Select(mesh =>
		{
			if (!mappedMeshIndexes.Contains(mesh.Index) || mesh.LodIndex < 0 || mesh.LodIndex > 3 || mesh.MaterialSlotIds.Count != canonicalSlots.Count || mesh.Sections.Count != canonicalSlots.Count) return mesh;
			return mesh with
			{
				MaterialSlotIds = canonicalSlots.ToArray(),
				Sections = mesh.Sections.Select((section, index) => section with { MaterialIndex = checked((uint)index), MaterialSlotId = canonicalSlots[index] }).ToArray()
			};
		}).ToArray();
		var rawMeshes = model.RawMeshData.Select(rawMesh =>
		{
			if (!mappedMeshIndexes.Contains(rawMesh.MeshInfoIndex) || rawMesh.LodIndex < 0 || rawMesh.LodIndex > 3 || rawMesh.Sections.Count != canonicalSlots.Count) return rawMesh;
			return rawMesh with
			{
				Sections = rawMesh.Sections.Select((section, index) => section with { MaterialIndex = checked((uint)index), MaterialSlotId = canonicalSlots[index] }).ToArray()
			};
		}).ToArray();
		return model with { Meshes = meshes, RawMeshData = rawMeshes };
	}

}

public interface ICurrentGameStreamLayoutRegistry
{
	bool TryResolveCanonicalSkinningLayout(UnitStreamInfo targetStream, IReadOnlyCollection<UnitStreamComponentInfo> requiredSourceComponents, int requiredSkinningCapacity, out UnitStreamInfo layout);
}

public sealed record SdkStyleTargetShellUnitReconstructionResult(
	UnitMeshModel Model,
	UnitMeshWriteResult WriteResult,
	IReadOnlyList<TargetShellMeshMapping> Replacements,
	IReadOnlyList<int> MinifiedTargetMeshInfoIndexes,
	IReadOnlyList<int> RebuiltBoneInfoIndexes,
	IReadOnlyList<ulong> ReplacementMaterialIds)
{
	public int CoveredTargetMeshCount => Model.RawMeshData.Count;
}
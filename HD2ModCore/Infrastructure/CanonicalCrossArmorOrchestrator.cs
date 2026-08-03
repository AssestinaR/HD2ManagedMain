using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using HD2ModCore.Domain;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using CoreAssetKey = HD2ModCore.Domain.AssetKey;
using AdaptationPatchTocEntry = HD2ModAdaptation.PatchReconstruction.PatchTocEntry;
using AdaptationPatchTocScanner = HD2ModAdaptation.PatchReconstruction.PatchTocScanner;
using AdaptationGameDataPackageResolver = HD2ModAdaptation.PatchReconstruction.GameDataPackageResolver;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HD2ModCore.Infrastructure;

// Purpose: Orchestrates the isolated canonical replacement chain without entering legacy CrossArmor operations.
// SDK order: GetEntryByLoadArchive(IgnorePatch=True) -> Load -> AddEntryToPatchID -> RawMeshes[MeshInfoIndex] = mesh -> Entry.Save.
// Documentation: docs/sdk流程架构.md sections 1-8; tools/ref/HD2SDK-CommunityEdition/stingray/unit.py
// SDK references: TocEntry.SerializeData() makes each emitted TOC/GPU/stream payload entry-owned and StreamToc.Serialize()
// immediately rereads the final layout; unmatched target meshes use the isolated canonical tiny path.
public sealed class CanonicalCrossArmorOrchestrator
{
	private sealed record CanonicalRebuildSummary(
		int MeshCount,
		int StreamCount,
		int MaterialBindingCount,
		int RawMeshCount,
		int BoneInfoCount,
		int TransformNameHashCount,
		int TransformEntryCount,
		int TransformMatrixCount,
		IReadOnlyList<UnitRawMeshSummary> RawMeshes,
		IReadOnlyList<(int Index, uint NumVertices, uint NumIndices, uint VertexBufferSize, uint IndexBufferSize)> Streams,
		IReadOnlyList<(int Index, int RealIndicesCount, int RemapsCount)> BoneInfos);

	private readonly IPatchTocScanner scanner;
	private readonly PatchUnitMeshReader sourceReader;
	private readonly PatchUnitMeshReader outputReader;
	private readonly Func<string, GameDataUnitMeshReader> targetReaderFactory;
	private readonly CanonicalMeshSemanticMerger merger;
	private readonly CanonicalTransformResolver transformResolver;
	private readonly CanonicalBoneRebuilder boneRebuilder;
	private readonly CanonicalLodBonePaletteCompiler lodBonePaletteCompiler;
	private readonly CanonicalStreamContractCompiler streamContractCompiler;
	private readonly CanonicalMeshPreparation preparation;
	private readonly CanonicalPlaceholderMinifier placeholderMinifier;
	private readonly CanonicalTransformInfoExpander transformInfoExpander;
	private readonly CanonicalStaticMeshBinder staticMeshBinder;
	private readonly CanonicalUnitRebuilder rebuilder;
	private readonly CanonicalDependencyClosure dependencyClosure;
	private readonly ICanonicalPatchWriter patchWriter;
	private readonly MaterialDependencyResolver materialResolver;

	private static void ReportProgress(CrossArmorTransferCandidateRequest request, string stage, string text, int completed, int total, System.Diagnostics.Stopwatch stopwatch)
		=> request.Progress?.Report(new CrossArmorTransferProgress(stage, text, Math.Clamp(completed, 0, total), total, stopwatch.Elapsed));

	private static CanonicalRebuildSummary CreateRebuildSummary(AdaptationAssetKey key, UnitMeshModel model)
		=> new(
			model.Meshes.Count,
			model.Streams.Count,
			model.Materials.Count,
			model.RawMeshes.Count,
			model.BoneInfos.Count,
			model.TransformNameHashes.Count,
			model.TransformInfo.Entries.Count,
			model.TransformInfo.Matrices.Count,
			model.RawMeshes.OrderBy(mesh => mesh.MeshInfoIndex).ToArray(),
			model.Streams.OrderBy(stream => stream.Index).Select(stream => (Index: stream.Index, NumVertices: stream.NumVertices, NumIndices: stream.NumIndices, VertexBufferSize: stream.VertexBufferSize, IndexBufferSize: stream.IndexBufferSize)).ToArray(),
			model.BoneInfos.Select(bone => (Index: bone.Index, RealIndicesCount: bone.RealIndices.Count, RemapsCount: bone.Remaps.Count)).ToArray());

	public CanonicalCrossArmorOrchestrator(
		IPatchTocScanner? scanner = null,
		PatchUnitMeshReader? sourceReader = null,
		PatchUnitMeshReader? outputReader = null,
		Func<string, GameDataUnitMeshReader>? targetReaderFactory = null,
		CanonicalMeshSemanticMerger? merger = null,
		CanonicalTransformResolver? transformResolver = null,
		CanonicalBoneRebuilder? boneRebuilder = null,
		CanonicalLodBonePaletteCompiler? lodBonePaletteCompiler = null,
		CanonicalStreamContractCompiler? streamContractCompiler = null,
		CanonicalMeshPreparation? preparation = null,
		CanonicalPlaceholderMinifier? placeholderMinifier = null,
		CanonicalTransformInfoExpander? transformInfoExpander = null,
		CanonicalStaticMeshBinder? staticMeshBinder = null,
		CanonicalUnitRebuilder? rebuilder = null,
		CanonicalDependencyClosure? dependencyClosure = null,
		ICanonicalPatchWriter? patchWriter = null,
		MaterialDependencyResolver? materialResolver = null)
	{
		this.scanner = scanner ?? new AdaptationPatchTocScanner();
		this.sourceReader = sourceReader ?? new PatchUnitMeshReader();
		this.outputReader = outputReader ?? new PatchUnitMeshReader();
		this.targetReaderFactory = targetReaderFactory ?? new Func<string, GameDataUnitMeshReader>(directory => new GameDataUnitMeshReader(new AdaptationGameDataPackageResolver(directory)));
		this.merger = merger ?? new CanonicalMeshSemanticMerger();
		this.transformResolver = transformResolver ?? new CanonicalTransformResolver();
		this.boneRebuilder = boneRebuilder ?? new CanonicalBoneRebuilder();
		this.lodBonePaletteCompiler = lodBonePaletteCompiler ?? new CanonicalLodBonePaletteCompiler();
		this.streamContractCompiler = streamContractCompiler ?? new CanonicalStreamContractCompiler();
		this.preparation = preparation ?? new CanonicalMeshPreparation();
		this.placeholderMinifier = placeholderMinifier ?? new CanonicalPlaceholderMinifier();
		this.transformInfoExpander = transformInfoExpander ?? new CanonicalTransformInfoExpander();
		this.staticMeshBinder = staticMeshBinder ?? new CanonicalStaticMeshBinder();
		this.rebuilder = rebuilder ?? new CanonicalUnitRebuilder();
		this.dependencyClosure = dependencyClosure ?? new CanonicalDependencyClosure();
		this.patchWriter = patchWriter ?? new CanonicalPatchWriter();
		this.materialResolver = materialResolver ?? new MaterialDependencyResolver();
	}

	public async ValueTask<CrossArmorTransferCandidateResult> ExecuteAsync(
		CrossArmorTransferCandidateRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var issues = new List<CoreIssue>();
		var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
		if (!request.Plan.CanContinue)
			return Failure(issues, "CanonicalPlanNotReady", "Canonical 链路要求现有 CrossArmorTransferPlan 已通过校验。");
		if (!File.Exists(request.SourcePatchTocPath))
			return Failure(issues, "CanonicalSourcePatchMissing", "Canonical 链路找不到 source patch TOC。");
		if (!Directory.Exists(request.GameDataDirectory))
			return Failure(issues, "CanonicalGameDataMissing", "Canonical 链路找不到 Game Data 目录。");

		try
		{
			ReportProgress(request, "CanonicalPreparing", "正在准备 Canonical 跨护甲重建。", 0, 1, totalStopwatch);
			var replacementPlanMappings = request.Plan.Mappings
				.Where(mapping => mapping.WillReplace)
				.Select(mapping => new CanonicalReplacementMapping(
					new(new AdaptationAssetKey(mapping.Source!.UnitAssetKey.TypeId, mapping.Source.UnitAssetKey.FileId), mapping.Source.MeshInfoIndex),
					new(new AdaptationAssetKey(mapping.PhysicalTarget.UnitAssetKey.TypeId, mapping.PhysicalTarget.UnitAssetKey.FileId), mapping.Target.MeshInfoIndex),
					SkinningMode: CanonicalSkinningMode.BindStaticToTargetMeshTransform,
					BoneAnchor: CanonicalBoneAnchor.TargetMeshTransform))
				.ToArray();
			var planValidation = replacementPlanMappings.Length == 0
				? null
				: CanonicalReplacementPlan.TryCreate(replacementPlanMappings);
			if (planValidation is { IsValid: false })
				return Failure(issues, planValidation.Diagnostics);

			var sourceEntries = request.PreparedSourceEntries is { Count: > 0 }
				? request.PreparedSourceEntries
				: await scanner.ScanEntriesAsync(request.SourcePatchTocPath, cancellationToken).ConfigureAwait(false);
			var sourceByKey = sourceEntries.ToDictionary(entry => entry.AssetKey);
			var sourceKeys = replacementPlanMappings.Select(mapping => mapping.Source.UnitKey).Distinct().ToArray();
			if (sourceKeys.Any(key => !sourceByKey.ContainsKey(key)))
				return Failure(issues, "CanonicalSourceUnitMissing", "Canonical 计划引用的 source Unit 不在 source patch 中。");

			var sourceUnits = new Dictionary<AdaptationAssetKey, PatchUnitMesh>();
			foreach (var key in sourceKeys)
			{
				ReportProgress(request, "ReadSourceUnits", $"正在读取来源 Unit 0x{key.FileId:x16}。", sourceUnits.Count, Math.Max(sourceKeys.Length, 1), totalStopwatch);
				sourceUnits[key] = await sourceReader.ReadAsync(sourceByKey[key], sourceEntries, PatchUnitDependencyPolicy.RequirePatchLocalComposite, cancellationToken).ConfigureAwait(false);
			}

			var targetReader = targetReaderFactory(request.GameDataDirectory);
			var canonicalAvatarTransforms = await new CanonicalAvatarRigReader(new AdaptationGameDataPackageResolver(request.GameDataDirectory)).ReadTransformInfoAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
			var outputEntries = new List<CanonicalPatchSessionEntry>();
			var stagedPayloadDirectory = Path.Combine(request.OutputDirectory, ".canonical-staged-payloads");
			Directory.CreateDirectory(stagedPayloadDirectory);
			var rebuiltTargets = new Dictionary<AdaptationAssetKey, CanonicalRebuildSummary>();
			var outputUnitCount = 0;
			var replacementCount = 0;
			var minifiedCount = 0;
			// All Units referenced by the selected target archives must be emitted. The plan only
			// contains classifiable visible meshes, whereas its parent entries also retain the
			// hidden/LOD shells that must be minified to suppress their original Game Data form.
			var targetUnits = request.Plan.SelectedTargets
				.SelectMany(target => target.Parts.Select(part => new TargetUnitSource(
					new AdaptationAssetKey(part.UnitAssetKey.TypeId, part.UnitAssetKey.FileId),
					target.ArchiveId)))
				.Concat(request.Plan.Mappings.Select(mapping => new TargetUnitSource(
					new AdaptationAssetKey(mapping.PhysicalTarget.UnitAssetKey.TypeId, mapping.PhysicalTarget.UnitAssetKey.FileId),
					FindTargetArchive(request.Plan, new AdaptationAssetKey(mapping.PhysicalTarget.UnitAssetKey.TypeId, mapping.PhysicalTarget.UnitAssetKey.FileId)))))
				.GroupBy(source => source.Key)
				.Select(group => new TargetUnitSource(group.Key, group.Select(source => source.ArchiveName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))))
				.ToArray();
			ReportProgress(request, "TargetUnitPlan", $"已准备 {targetUnits.Length} 个唯一目标 Unit，开始重建。", 0, Math.Max(targetUnits.Length, 1), totalStopwatch);
			foreach (var (targetUnit, targetIndex) in targetUnits.Select((value, index) => (value, index)))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var unitStopwatch = System.Diagnostics.Stopwatch.StartNew();
				ReportProgress(request, "RebuildTargetUnit", $"Canonical：重建 Unit {targetIndex + 1}/{targetUnits.Length} 当前Unit=0x{targetUnit.Key.FileId:x16}", targetIndex, Math.Max(targetUnits.Length, 1), totalStopwatch);
				var archiveName = targetUnit.ArchiveName;
				if (archiveName is null)
					return Failure(issues, "CanonicalTargetArchiveMissing", $"目标 Unit 0x{targetUnit.Key.FileId:x16} 没有明确的 Game Data archive。");
				var target = await targetReader.ReadAsync(archiveName, targetUnit.Key, allowGlobalDependencySearch: false, cancellationToken: cancellationToken).ConfigureAwait(false);
				var approvedUnitMappings = request.Plan.Mappings
					.Where(mapping => mapping.WillReplace && SameKey(mapping.PhysicalTarget.UnitAssetKey, targetUnit.Key))
					.Select(mapping => new CanonicalReplacementMapping(
						new(new AdaptationAssetKey(mapping.Source!.UnitAssetKey.TypeId, mapping.Source.UnitAssetKey.FileId), mapping.Source.MeshInfoIndex),
						new(targetUnit.Key, mapping.Target.MeshInfoIndex),
						SkinningMode: CanonicalSkinningMode.BindStaticToTargetMeshTransform,
						BoneAnchor: CanonicalBoneAnchor.TargetMeshTransform))
					.ToArray();
				var canonicalMappings = ExpandAutoLodMappings(target.Model, sourceUnits, approvedUnitMappings);
				var transformSources = canonicalMappings.Select(mapping =>
				{
					var source = sourceUnits[mapping.Source.UnitKey];
					var raw = source.Model.RawMeshData.SingleOrDefault(item => item.MeshInfoIndex == mapping.Source.MeshInfoIndex)
						?? throw new InvalidDataException("Source RawMesh payload is incomplete for Canonical TransformInfo expansion.");
					return (source.Model, raw);
				});
				target = target with { Model = transformInfoExpander.Expand(target.Model, transformSources, canonicalAvatarTransforms) };
				var mappingsByIndex = canonicalMappings.ToDictionary(mapping => mapping.Target.MeshInfoIndex);
				var finalRawMeshes = new List<UnitRawMeshData>(target.Model.Meshes.Count);
				var provisionalSkinnedMeshes = new List<CanonicalLodBoneInput>();
				var sourceMaterialBindings = new Dictionary<uint, ulong>();
				var rebuiltBoneInfos = target.Model.BoneInfos
					.Select((boneInfo, index) => new { Index = index, BoneInfo = boneInfo })
					.ToDictionary(item => item.Index, item => item.BoneInfo);
				foreach (var targetMesh in target.Model.Meshes)
				{
					var targetRaw = target.Model.RawMeshData.SingleOrDefault(raw => raw.MeshInfoIndex == targetMesh.Index);
					var stream = target.Model.Streams.SingleOrDefault(candidate => candidate.Index == (int)targetMesh.StreamIndex);
					if (targetRaw is null || stream is null)
						return Failure(issues, [new CanonicalPlanDiagnostic("RawMeshUnavailable", $"目标 RawMesh/stream payload 不完整，无法重建 MeshInfo {targetMesh.Index}。")]);

					UnitRawMeshData finalRaw;
					UnitBoneInfo? provisionalBoneInfo = null;
					if (!mappingsByIndex.TryGetValue(targetMesh.Index, out var mapping))
					{
						var tiny = placeholderMinifier.TryMinify(targetRaw, stream);
						if (!tiny.IsValid) return Failure(issues, tiny.Diagnostics);
						finalRaw = tiny.Mesh!;
						minifiedCount++;
					}
					else
					{
						var source = sourceUnits[mapping.Source.UnitKey];
						var sourceRaw = source.Model.RawMeshData.SingleOrDefault(raw => raw.MeshInfoIndex == mapping.Source.MeshInfoIndex);
						if (sourceRaw is null)
							return Failure(issues, [new CanonicalPlanDiagnostic("RawMeshUnavailable", $"Source RawMesh payload 不完整，无法重建 MeshInfo {targetMesh.Index}。")]);
						var sourceRawForMaterials = sourceRaw;
						var transform = transformResolver.TryResolve(source.Model, sourceRaw.MeshInfoIndex, target.Model, targetRaw.MeshInfoIndex);
						if (!transform.IsValid) return Failure(issues, transform.Diagnostics);
						var sourceHasBones = HasBoneData(source.Model, sourceRaw);
						var targetHasBones = UsesSkinningStream(stream);
						if (sourceHasBones)
						{
							var rebuiltBone = boneRebuilder.TryRebuild(source.Model, sourceRaw, target.Model, targetRaw);
							if (!rebuiltBone.IsValid) return Failure(issues, rebuiltBone.Diagnostics);
							sourceRaw = rebuiltBone.Mesh!;
							provisionalBoneInfo = rebuiltBone.BoneInfo;
						}
						else if (targetHasBones)
						{
							if (mapping.SkinningMode is not (CanonicalSkinningMode.BindStaticToTargetMeshTransform or CanonicalSkinningMode.BindStaticToAvatarBone))
								return Failure(issues, "CanonicalStaticAnchorRequired", $"静态来源 MeshInfo {mapping.Source.MeshInfoIndex} 写入骨骼目标时必须声明 Canonical 锚定策略。");
							var staticBind = staticMeshBinder.TryBind(target.Model, targetRaw, sourceRaw, stream,
								mapping.SkinningMode == CanonicalSkinningMode.BindStaticToAvatarBone ? mapping.BoneAnchor : CanonicalBoneAnchor.TargetMeshTransform);
							if (!staticBind.IsValid) return Failure(issues, staticBind.Diagnostics);
							sourceRaw = staticBind.Mesh!;
							provisionalBoneInfo = staticBind.BoneInfo;
						}
						var merged = merger.TryMerge(new(mapping.Source, mapping.Target, transform.SourceToTargetLocal!.Value), targetRaw, sourceRaw);
						if (!merged.IsValid) return Failure(issues, merged.Diagnostics);
						var materialBindings = CollectSourceMaterialBindings(source.Model, sourceRawForMaterials, targetRaw);
						foreach (var binding in materialBindings)
						{
							if (sourceMaterialBindings.TryGetValue(binding.Key, out var existing) && existing != binding.Value)
								return Failure(issues, "CanonicalMaterialSlotConflict", $"目标材质槽 {binding.Key} 被映射到多个来源 Material 资源。");
							sourceMaterialBindings[binding.Key] = binding.Value;
						}
						finalRaw = merged.Mesh!;
						replacementCount++;
					}
					finalRawMeshes.Add(finalRaw);
					if (provisionalBoneInfo is not null)
						provisionalSkinnedMeshes.Add(new CanonicalLodBoneInput(finalRaw, provisionalBoneInfo));
				}

				// SDK GetMeshData completes all final meshes before BoneInfo.SetRemap. Compile the
				// shared target LOD palette only after every replacement has reached final topology.
				foreach (var lodGroup in provisionalSkinnedMeshes.GroupBy(mesh => mesh.Mesh.LodIndex))
				{
					var compiled = lodBonePaletteCompiler.TryCompile(target.Model, lodGroup.Key, lodGroup.ToArray());
					if (!compiled.IsValid) return Failure(issues, compiled.Diagnostics);
					rebuiltBoneInfos[lodGroup.Key] = compiled.BoneInfo!;
					var byMeshIndex = compiled.Meshes.ToDictionary(mesh => mesh.MeshInfoIndex);
					for (var index = 0; index < finalRawMeshes.Count; index++)
						if (byMeshIndex.TryGetValue(finalRawMeshes[index].MeshInfoIndex, out var compiledMesh))
							finalRawMeshes[index] = compiledMesh;
				}
				// SetupRawMeshComponents is stream-wide in the community SDK. The Canonical
				// equivalent validates every completed RawMesh first and returns the one ABI
				// contract used for both TryPrepare and StreamInfo serialization.
				var compiledStreams = streamContractCompiler.TryCompile(target.Model, finalRawMeshes);
				if (!compiledStreams.IsValid) return Failure(issues, compiledStreams.Diagnostics);
				target = target with { Model = target.Model with { Streams = compiledStreams.Streams } };
				for (var index = 0; index < finalRawMeshes.Count; index++)
				{
					var stream = target.Model.Streams.Single(candidate => candidate.Index == (int)finalRawMeshes[index].StreamIndex);
					var prepared = preparation.TryPrepare(finalRawMeshes[index], stream);
					if (!prepared.IsValid) return Failure(issues, prepared.Diagnostics);
					finalRawMeshes[index] = prepared.Mesh!;
				}

				if (sourceMaterialBindings.Count != 0)
				{
					var bindings = target.Model.Materials.Where(binding => !sourceMaterialBindings.ContainsKey(binding.SectionId))
						.Concat(sourceMaterialBindings.OrderBy(binding => binding.Key).Select(binding => new UnitMaterialBinding(binding.Key, binding.Value)))
						.ToArray();
					target = target with { Model = target.Model with { Materials = bindings } };
				}
				var rebuilt = rebuilder.TryRebuild(target.Model, target.Payload.TocData, finalRawMeshes,
					rebuiltBoneInfos.Select(item => new CanonicalBoneInfoRebuild(item.Key, item.Value)).ToArray());
				if (!rebuilt.IsValid) return Failure(issues, rebuilt.Diagnostics);
				// A TargetOutput owns all three sidecars. The first Canonical rebuilder has no StreamData
				// serializer, so an existing target stream cannot be inherited as if it were new output.
				if (target.Payload.StreamData.Length != 0)
					return Failure(issues, "CanonicalTargetStreamPayloadUnsupported", $"目标 Unit 0x{targetUnit.Key.FileId:x16} 带有非空 source stream payload；Canonical 重建尚未生成等价 stream payload，因此拒绝静默沿用旧 stream。");
				var staged = StagePayloads(stagedPayloadDirectory, targetUnit.Key, rebuilt.Output!.TocData, rebuilt.Output.GpuData, Array.Empty<byte>());
				var outputTargetEntry = new CanonicalPatchSessionEntry(targetUnit.Key, CanonicalPatchEntryOwnership.TargetOutput,
					null, null, null, target.Payload.Entry.Unknown1,
					target.Payload.Entry.Unknown2, target.Payload.Entry.Unknown3, target.Payload.Entry.Unknown4)
				{
					TocDataPath = staged.TocData,
					GpuDataPath = staged.GpuData,
					StreamDataPath = staged.StreamData
				};
				outputEntries.Add(outputTargetEntry);
				rebuiltTargets.Add(targetUnit.Key, CreateRebuildSummary(targetUnit.Key, rebuilt.Model!));
				outputUnitCount++;
				ReportProgress(request, "RebuildTargetUnit", $"Canonical：重建 Unit {targetIndex + 1}/{targetUnits.Length} 当前Unit=0x{targetUnit.Key.FileId:x16} 用时={unitStopwatch.Elapsed:hh\\:mm\\:ss}", targetIndex + 1, Math.Max(targetUnits.Length, 1), totalStopwatch);
			}

			// Unit payloads have already been staged to disk. Do not keep the full source
			// mesh graph alive while the resolver starts scanning Game Data archives.
			targetReader.ClearCaches();
			sourceUnits.Clear();
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
			GC.WaitForPendingFinalizers();
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);

			var materialIds = outputEntries.SelectMany(entry => new UnitMaterialReferenceReader().ReadReferenceBindings(entry.EffectiveTocData).Select(binding => binding.MaterialId)).Distinct().ToArray();
			ReportProgress(request, "ResolveMaterials", $"Canonical：开始解析材质 0/{materialIds.Length}", 0, Math.Max(materialIds.Length, 1), totalStopwatch);
			var materialProgress = new Progress<MaterialDependencyProgress>(update =>
			{
				ReportProgress(request, update.StageId, update.StageText, update.Completed, Math.Max(update.Total, 1), totalStopwatch);
			});
			var materialResolution = await materialResolver.ResolveAsync(materialIds, sourceEntries, request.GameDataDirectory,
				new Dictionary<AdaptationAssetKey, IReadOnlyList<string>>(), cancellationToken, materialProgress).ConfigureAwait(false);
			if (materialResolution.RejectedMaterialReasons.Count != 0)
				return Failure(issues, materialResolution.RejectedMaterialReasons.Select(item => new CanonicalPlanDiagnostic("OriginalMaterialDependencyMissing", $"原版材质 {item.Key:x16} 依赖不完整：{item.Value}")));
			foreach (var dependency in materialResolution.Entries)
			{
				var staged = StagePayloads(stagedPayloadDirectory, dependency.AssetKey, dependency.TocData, dependency.GpuResourceData, dependency.StreamData);
				outputEntries.Add(new CanonicalPatchSessionEntry(dependency.AssetKey, CanonicalPatchEntryOwnership.RequiredDependency, null, null, null,
					dependency.Unknown1, dependency.Unknown2, dependency.Unknown3, dependency.Unknown4)
				{
					TocDataPath = staged.TocData,
					GpuDataPath = staged.GpuData,
					StreamDataPath = staged.StreamData
				});
			}

			var session = new CanonicalPatchSession();
			ReportProgress(request, "ValidateClosure", "正在验证输出依赖闭包。", targetUnits.Length, Math.Max(targetUnits.Length, 1), totalStopwatch);
			foreach (var entry in outputEntries) session.AddEntry(entry);
			var closureDiagnostics = new List<CanonicalPlanDiagnostic>();
			foreach (var targetEntry in outputEntries.Where(entry => entry.Ownership == CanonicalPatchEntryOwnership.TargetOutput))
			{
				var closure = await dependencyClosure.ValidateAsync(new(targetEntry.Key, targetEntry.EffectiveTocData, outputEntries, request.GameDataDirectory), cancellationToken).ConfigureAwait(false);
				closureDiagnostics.AddRange(closure.Diagnostics.Select(diagnostic => new CanonicalPlanDiagnostic(diagnostic.Code, diagnostic.Message)));
			}
			if (closureDiagnostics.Count != 0)
				return Failure(issues, closureDiagnostics);
			var finalized = session.Finalize(CanonicalDependencyClosureValidation.Valid);
			if (!finalized.IsValid) return Failure(issues, finalized.Diagnostics);
			var headerArchive = request.Plan.SelectedTargets.FirstOrDefault()?.ArchiveId;
			var header = headerArchive is null ? null : await new AdaptationGameDataPackageResolver(request.GameDataDirectory).GetPackageTocAsync(headerArchive, cancellationToken).ConfigureAwait(false);
			ReportProgress(request, "WritePatch", "正在写入 Canonical Patch 和 GPU sidecar。", targetUnits.Length, Math.Max(targetUnits.Length, 1), totalStopwatch);
			var written = await patchWriter.WriteAsync(session, request.OutputDirectory, ResolveOutputPatchFileName(request.SourcePatchTocPath), header?.Data, overwriteExisting: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			ReportProgress(request, "ReadbackValidation", "正在回读验证已写入的目标 Unit。", targetUnits.Length, Math.Max(targetUnits.Length, 1), totalStopwatch);
			var outputEntriesScanned = await scanner.ScanEntriesAsync(written.TocFilePath, cancellationToken).ConfigureAwait(false);
			var readbackDiagnostics = await ValidateWrittenTargetsAsync(outputEntriesScanned, outputEntries, rebuiltTargets, cancellationToken).ConfigureAwait(false);
			issues.AddRange(readbackDiagnostics.Select(diagnostic => new CoreIssue(CoreIssueSeverity.Error, diagnostic.Code, diagnostic.Message)));
			var reportPath = await WriteReportAsync(written.OutputDirectoryPath, replacementPlanMappings, outputEntries, rebuiltTargets, readbackDiagnostics, cancellationToken).ConfigureAwait(false);
			TryDeleteDirectory(stagedPayloadDirectory);
			ReportProgress(request, "CanonicalCompleted", "Canonical Patch 已完成写入并通过回读验证。", targetUnits.Length, Math.Max(targetUnits.Length, 1), totalStopwatch);
			if (readbackDiagnostics.Count != 0)
				return new CrossArmorTransferCandidateResult(false, written.OutputDirectoryPath, reportPath, outputUnitCount, replacementCount, minifiedCount, issues);
			return new CrossArmorTransferCandidateResult(true, written.OutputDirectoryPath, reportPath, outputUnitCount, replacementCount, minifiedCount, issues) { IsCommitted = true };
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
		catch (Exception exception) when (exception is IOException or InvalidDataException or KeyNotFoundException or ArgumentException or OverflowException)
		{
			issues.Add(new(CoreIssueSeverity.Error, "CanonicalExecutionFailed", exception.Message, ExceptionMessage: exception.ToString()));
			return new CrossArmorTransferCandidateResult(false, null, null, 0, 0, 0, issues);
		}
	}

	private static (string TocData, string GpuData, string StreamData) StagePayloads(string directory, AdaptationAssetKey key, byte[] tocData, byte[] gpuData, byte[] streamData)
	{
		var prefix = $"{key.TypeId:x16}-{key.FileId:x16}";
		var tocPath = Path.Combine(directory, prefix + ".toc");
		var gpuPath = Path.Combine(directory, prefix + ".gpu");
		var streamPath = Path.Combine(directory, prefix + ".stream");
		File.WriteAllBytes(tocPath, tocData);
		File.WriteAllBytes(gpuPath, gpuData);
		File.WriteAllBytes(streamPath, streamData);
		return (tocPath, gpuPath, streamPath);
	}

	private static void TryDeleteDirectory(string directory)
	{
		try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}

	private static string? FindTargetArchive(CrossArmorTransferPlan plan, AdaptationAssetKey key)
		=> plan.SelectedTargets.FirstOrDefault(target => target.Parts.Any(part => SameKey(part.UnitAssetKey, key)))?.ArchiveId
			?? plan.SelectedTargets.SelectMany(target => target.Parts).FirstOrDefault(part => SameKey(part.UnitAssetKey, key))?.SharedArchiveIds.FirstOrDefault();

	private static IReadOnlyList<CanonicalReplacementMapping> ExpandAutoLodMappings(
		UnitMeshModel targetModel,
		IReadOnlyDictionary<AdaptationAssetKey, PatchUnitMesh> sourceUnits,
		IReadOnlyList<CanonicalReplacementMapping> approvedUnitMappings)
	{
		var expandedMappings = new List<CanonicalReplacementMapping>();
		foreach (var approved in approvedUnitMappings)
		{
			if (!sourceUnits.TryGetValue(approved.Source.UnitKey, out var sourceUnit))
				throw new InvalidDataException($"Canonical 计划引用的 source Unit 0x{approved.Source.UnitKey.FileId:x16} 未加载。");

			var sourceRepresentative = sourceUnit.Model.RawMeshData
				.SingleOrDefault(raw => raw.MeshInfoIndex == approved.Source.MeshInfoIndex)
				?? throw new InvalidDataException($"Source RawMesh {approved.Source.MeshInfoIndex} 不存在，无法展开 AutoLods。");
			var sourceLod0 = sourceUnit.Model.RawMeshData
				.Where(raw => raw.LodIndex == 0 && raw.Triangles.Count > 1 && raw.Vertices.Count > 3)
				.Where(raw => SemanticMatches(
					sourceUnit.Model.Meshes.FirstOrDefault(mesh => mesh.Index == raw.MeshInfoIndex)?.SemanticInfo,
					sourceUnit.Model.Meshes.FirstOrDefault(mesh => mesh.Index == sourceRepresentative.MeshInfoIndex)?.SemanticInfo) )
				.OrderByDescending(raw => raw.Triangles.Count)
				.ThenByDescending(raw => raw.Vertices.Count)
				.FirstOrDefault()
				?? throw new InvalidDataException($"Source Unit 0x{approved.Source.UnitKey.FileId:x16} 缺少真实 LOD0。");

			var targetRepresentative = targetModel.RawMeshData
				.SingleOrDefault(raw => raw.MeshInfoIndex == approved.Target.MeshInfoIndex)
				?? throw new InvalidDataException($"Target RawMesh {approved.Target.MeshInfoIndex} 不存在，无法展开 AutoLods。");
			var targetLodSlots = targetModel.RawMeshData
				.Where(raw => raw.LodIndex >= 0 && raw.Triangles.Count > 1 && raw.Vertices.Count > 3)
				.OrderBy(raw => raw.LodIndex)
				.ThenBy(raw => raw.MeshInfoIndex);
			foreach (var targetLodSlot in targetLodSlots)
			{
				expandedMappings.Add(new CanonicalReplacementMapping(
					new(approved.Source.UnitKey, sourceLod0.MeshInfoIndex),
					new(approved.Target.UnitKey, targetLodSlot.MeshInfoIndex),
					approved.SourceMeshState,
					approved.SkinningMode,
					approved.BoneAnchor));
			}
		}

		return expandedMappings
			.GroupBy(mapping => (mapping.Target.UnitKey, mapping.Target.MeshInfoIndex))
			.Select(group => group.First())
			.ToArray();
	}

	private static bool SemanticMatches(UnitMeshSemanticInfo? candidate, UnitMeshSemanticInfo? representative)
	{
		if (candidate is null || representative is null) return true;
		return string.Equals(candidate.Slot, representative.Slot, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(candidate.PieceType, representative.PieceType, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(candidate.BodyType, representative.BodyType, StringComparison.OrdinalIgnoreCase);
	}

	private static bool SameKey(CoreAssetKey coreKey, AdaptationAssetKey adaptationKey)
		=> coreKey.TypeId == adaptationKey.TypeId && coreKey.FileId == adaptationKey.FileId;

	private sealed record TargetUnitSource(AdaptationAssetKey Key, string? ArchiveName);

	private static bool HasBoneData(UnitMeshModel model, UnitRawMeshData raw)
		=> raw.LodIndex >= 0 && raw.LodIndex < model.BoneInfos.Count && model.BoneInfos[raw.LodIndex].RealIndices.Count > 0;

	private static IReadOnlyDictionary<uint, ulong> CollectSourceMaterialBindings(UnitMeshModel source, UnitRawMeshData sourceRaw, UnitRawMeshData targetRaw)
	{
		var layout = CanonicalSectionLayout.TryCreate(sourceRaw, targetRaw);
		if (!layout.IsValid) throw new InvalidDataException(string.Join("; ", layout.Diagnostics.Select(diagnostic => diagnostic.Message)));
		var bindings = new Dictionary<uint, ulong>();
		foreach (var assignment in layout.Assignments)
		{
			var materialIds = source.Materials.Where(binding => binding.SectionId == assignment.SourceSection.MaterialSlotId)
				.Select(binding => binding.MaterialId).Distinct().ToArray();
			if (materialIds.Length != 1)
				throw new InvalidDataException($"Source material slot {assignment.SourceSection.MaterialSlotId} does not resolve to exactly one Material asset.");
			var targetSlot = assignment.TargetSection.MaterialSlotId;
			if (bindings.TryGetValue(targetSlot, out var existing) && existing != materialIds[0])
				throw new InvalidDataException($"Target material slot {targetSlot} cannot represent multiple source Material assets.");
			bindings[targetSlot] = materialIds[0];
		}
		return bindings;
	}

	private static string ResolveOutputPatchFileName(string sourcePatchTocPath)
	{
		var fileName = Path.GetFileName(sourcePatchTocPath);
		if (!Regex.IsMatch(fileName, "^[0-9a-fA-F]{16}\\.patch_0$", RegexOptions.CultureInvariant))
			throw new InvalidDataException("Canonical 输出要求来源 Patch 文件名为 16 位十六进制 ID 加 .patch_0。");
		return fileName.ToLowerInvariant();
	}

	private static bool UsesSkinningStream(UnitStreamInfo stream)
		=> stream.Components.Any(component => component.Type == 6) && stream.Components.Any(component => component.Type == 7);

	private async ValueTask<IReadOnlyList<CanonicalPlanDiagnostic>> ValidateWrittenTargetsAsync(
		IReadOnlyList<AdaptationPatchTocEntry> scannedEntries,
		IReadOnlyList<CanonicalPatchSessionEntry> expectedEntries,
		IReadOnlyDictionary<AdaptationAssetKey, CanonicalRebuildSummary> rebuiltTargets,
		CancellationToken cancellationToken)
	{
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		foreach (var expected in expectedEntries.Where(entry => entry.Ownership == CanonicalPatchEntryOwnership.TargetOutput))
		{
			var entry = scannedEntries.SingleOrDefault(candidate => candidate.AssetKey == expected.Key);
			if (entry is null)
			{
				diagnostics.Add(new("CanonicalReadbackEntryMissing", $"写出后回读找不到目标 Unit 0x{expected.Key.FileId:x16}。"));
				continue;
			}
			if (entry.TocDataSize != expected.EffectiveTocData.Length || entry.StreamSize != expected.EffectiveStreamData.Length || entry.GpuResourceSize != expected.EffectiveGpuData.Length)
				diagnostics.Add(new("CanonicalReadbackPayloadMismatch", $"目标 Unit 0x{expected.Key.FileId:x16} 的 stream/GPU size 与重建 payload 不一致。"));
			if (!rebuiltTargets.TryGetValue(expected.Key, out var expectedModel))
			{
				diagnostics.Add(new("CanonicalReadbackExpectedModelMissing", $"目标 Unit 0x{expected.Key.FileId:x16} 缺少 CanonicalUnitRebuilder 返回模型，拒绝验证写出结果。"));
				continue;
			}
			try
			{
				var readback = await outputReader.ReadAsync(entry, scannedEntries, PatchUnitDependencyPolicy.AllowExternalCompositeReference, cancellationToken).ConfigureAwait(false);
				ValidateModelSummary(expectedModel, readback.Model, expected.Key, diagnostics);
			}
			catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or IOException or KeyNotFoundException or ArgumentException or OverflowException)
			{
				diagnostics.Add(new("CanonicalReadbackFailed", $"目标 Unit 0x{expected.Key.FileId:x16} 回读失败：{exception.Message}"));
			}
		}
		return diagnostics;
	}

	private static void ValidateModelSummary(CanonicalRebuildSummary expected, UnitMeshModel actual, AdaptationAssetKey key, List<CanonicalPlanDiagnostic> diagnostics)
	{
		if (expected.MeshCount != actual.Meshes.Count || expected.StreamCount != actual.Streams.Count || expected.MaterialBindingCount != actual.Materials.Count)
			diagnostics.Add(new("CanonicalReadbackModelCoverageMismatch", $"目标 Unit 0x{key.FileId:x16} 回读后的 mesh/stream/material 数量与重建模型不一致。"));
		var expectedMeshes = expected.RawMeshes.OrderBy(mesh => mesh.MeshInfoIndex).ToArray();
		var actualMeshes = actual.RawMeshes.OrderBy(mesh => mesh.MeshInfoIndex).ToArray();
		if (expectedMeshes.Length != actualMeshes.Length || expectedMeshes.Where((mesh, index) => !RawMeshEquivalent(mesh, actualMeshes[index])).Any())
			diagnostics.Add(new("CanonicalReadbackRawMeshCoverageMismatch", $"目标 Unit 0x{key.FileId:x16} 回读后的 RawMesh coverage 与重建模型不一致。"));
		var expectedStreams = expected.Streams.OrderBy(stream => stream.Index).Select(stream => (stream.Index, stream.NumVertices, stream.NumIndices, stream.VertexBufferSize, stream.IndexBufferSize)).ToArray();
		var actualStreams = actual.Streams.OrderBy(stream => stream.Index).Select(stream => (stream.Index, stream.NumVertices, stream.NumIndices, stream.VertexBufferSize, stream.IndexBufferSize)).ToArray();
		if (!expectedStreams.SequenceEqual(actualStreams))
			diagnostics.Add(new("CanonicalReadbackStreamMismatch", $"目标 Unit 0x{key.FileId:x16} 回读后的 stream 摘要与重建模型不一致。"));
		var expectedBones = expected.BoneInfos.Select(bone => (bone.Index, bone.RealIndicesCount, bone.RemapsCount)).ToArray();
		var actualBones = actual.BoneInfos.Select(bone => (bone.Index, bone.RealIndices.Count, bone.Remaps.Count)).ToArray();
		if (!expectedBones.SequenceEqual(actualBones))
			diagnostics.Add(new("CanonicalReadbackBoneCoverageMismatch", $"目标 Unit 0x{key.FileId:x16} 回读后的 bone coverage 与重建模型不一致。"));
		if (expected.TransformNameHashCount != actual.TransformNameHashes.Count
			|| expected.TransformEntryCount != actual.TransformInfo.Entries.Count
			|| expected.TransformMatrixCount != actual.TransformInfo.Matrices.Count)
			diagnostics.Add(new("CanonicalReadbackTransformInfoMismatch", $"目标 Unit 0x{key.FileId:x16} 回读后的 TransformInfo 骨架布局与 Canonical Avatar 扩容结果不一致。"));
	}

	private static bool RawMeshEquivalent(UnitRawMeshSummary expected, UnitRawMeshSummary actual)
		=> expected.MeshInfoIndex == actual.MeshInfoIndex
			&& expected.MeshId == actual.MeshId
			&& expected.LodIndex == actual.LodIndex
			&& expected.StreamIndex == actual.StreamIndex
			&& expected.VertexCount == actual.VertexCount
			&& expected.IndexCount == actual.IndexCount
			&& expected.MaterialCount == actual.MaterialCount
			&& expected.SectionCount == actual.SectionCount;

	private static async ValueTask<string> WriteReportAsync(
		string outputDirectory,
		IReadOnlyList<CanonicalReplacementMapping> mappings,
		IReadOnlyList<CanonicalPatchSessionEntry> entries,
		IReadOnlyDictionary<AdaptationAssetKey, CanonicalRebuildSummary> rebuiltTargets,
		IReadOnlyList<CanonicalPlanDiagnostic> diagnostics,
		CancellationToken cancellationToken)
	{
		var reportPath = Path.Combine(Path.GetFullPath(outputDirectory), "canonical-report.json");
		var report = new
		{
			sourceMappings = mappings.Select(mapping => new { source = mapping.Source.UnitKey, sourceMeshInfoIndex = mapping.Source.MeshInfoIndex, target = mapping.Target.UnitKey, targetMeshInfoIndex = mapping.Target.MeshInfoIndex }),
			finalEntryKeys = entries.Select(entry => new { key = entry.Key, ownership = entry.Ownership.ToString() }),
			targetOutputUnitKeys = entries.Where(entry => entry.Ownership == CanonicalPatchEntryOwnership.TargetOutput).Select(entry => entry.Key),
			rebuildSummaries = rebuiltTargets.Select(target => new
			{
				targetUnitKey = target.Key,
				meshCount = target.Value.MeshCount,
				streamCount = target.Value.StreamCount,
				materialBindingCount = target.Value.MaterialBindingCount,
				rawMeshCount = target.Value.RawMeshCount,
				boneInfoCount = target.Value.BoneInfoCount,
				gpuRangeBytes = entries.Single(entry => entry.Key == target.Key).EffectiveGpuData.Length,
				rawMeshes = target.Value.RawMeshes
			}),
			diagnostics
		};
		await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
		return reportPath;
	}

	private static CrossArmorTransferCandidateResult Failure(List<CoreIssue> issues, string code, string message)
		=> Failure(issues, [new CanonicalPlanDiagnostic(code, message)]);

	private static CrossArmorTransferCandidateResult Failure(List<CoreIssue> issues, IEnumerable<CanonicalPlanDiagnostic> diagnostics)
	{
		issues.AddRange(diagnostics.Select(diagnostic => new CoreIssue(CoreIssueSeverity.Error, diagnostic.Code, diagnostic.Message)));
		return new(false, null, null, 0, 0, 0, issues);
	}
}

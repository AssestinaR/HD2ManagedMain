using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using AdaptationGameDataPackageResolver = HD2ModAdaptation.PatchReconstruction.GameDataPackageResolver;
using AdaptationGameDataUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.GameDataUnitMeshReader;
using AdaptationPatchArchiveWriter = HD2ModAdaptation.PatchReconstruction.PatchArchiveWriter;
using AdaptationMaterialDependencyResolver = HD2ModAdaptation.PatchReconstruction.MaterialDependencyResolver;
using AdaptationPatchEntryPayloadReader = HD2ModAdaptation.PatchReconstruction.PatchEntryPayloadReader;
using AdaptationPatchTocEntry = HD2ModAdaptation.PatchReconstruction.PatchTocEntry;
using AdaptationPatchTocScanner = HD2ModAdaptation.PatchReconstruction.PatchTocScanner;
using AdaptationPatchUnitMesh = HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMesh;
using AdaptationPatchUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMeshReader;
using AdaptationSdkStyleTargetShellPatchOutputBuilder = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchOutputBuilder;
using AdaptationSdkStyleTargetShellPatchWorkItem = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchWorkItem;
using AdaptationTargetShellMeshMapping = HD2ModAdaptation.PatchReconstruction.UnitMesh.TargetShellMeshMapping;
using AdaptationCrossArmorBoneDiagnosticAnalyzer = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.CrossArmorBoneDiagnosticAnalyzer;
using AdaptationCrossArmorTransformInfoExpander = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.CrossArmorTransformInfoExpander;
using AdaptationCrossArmorSkinningDiagnosticAnalyzer = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.CrossArmorSkinningDiagnosticAnalyzer;
using AdaptationSdkStyleAvatarRigReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleAvatarRigReader;

namespace HD2ModCore.Infrastructure;

// Purpose: Rebuilds current target shells from an approved cross-armor plan into an isolated test Patch.
public sealed class CrossArmorTransferCandidateService : ICrossArmorTransferCandidateService
{
	private const ulong CompositeUnitTypeId = 0xc4f0f4be7fb0c8d6;
	private readonly AdaptationPatchTocScanner scanner = new();
	private readonly AdaptationPatchUnitMeshReader unitReader = new();
	private readonly AdaptationPatchArchiveWriter archiveWriter = new();
	private readonly AdaptationCrossArmorBoneDiagnosticAnalyzer boneDiagnosticAnalyzer = new();
	private readonly AdaptationCrossArmorTransformInfoExpander transformInfoExpander = new();
	private readonly AdaptationCrossArmorSkinningDiagnosticAnalyzer skinningDiagnosticAnalyzer = new();
	private readonly AdaptationMaterialDependencyResolver materialDependencyResolver = new();

	public async ValueTask<CrossArmorTransferCandidateResult> GenerateCandidateAsync(CrossArmorTransferCandidateRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var issues = new List<CoreIssue>();
		var boneDiagnostics = new List<object>();
		var skinningDiagnostics = new List<object>();
		var transferLayoutDiagnostics = new List<object>();
		var outputTransferLayoutDiagnostics = new List<object>();
		if (!request.Plan.CanContinue) return Failure("PlanNotReady", "当前计划尚不可写出；请先选择来源、目标并排除所有错误。", issues);
		if (!File.Exists(request.SourcePatchTocPath)) return Failure("SourcePatchMissing", "源 Patch 主文件不存在。", issues);
		if (!Directory.Exists(request.GameDataDirectory)) return Failure("GameDataMissing", "Game Data 文件夹不存在。", issues);
		if (Directory.Exists(request.OutputDirectory) && Directory.EnumerateFileSystemEntries(request.OutputDirectory).Any()) return Failure("OutputNotEmpty", "输出文件夹必须为空。", issues);

		try
		{
			var unsafeBoneMappings = new List<string>();
			var sourceEntries = await scanner.ScanEntriesAsync(request.SourcePatchTocPath, cancellationToken).ConfigureAwait(false);
			var mappings = request.Plan.Mappings.Where(mapping => mapping.WillReplace).ToArray();
			var sourceKeys = mappings.Select(mapping => ToAdaptationKey(mapping.Source!.UnitAssetKey)).ToHashSet();
			var sourceUnits = new Dictionary<AdaptationAssetKey, AdaptationPatchUnitMesh>();
			foreach (var entry in sourceEntries.Where(entry => sourceKeys.Contains(entry.AssetKey)))
			{
				sourceUnits.Add(entry.AssetKey, await unitReader.ReadAsync(entry, sourceEntries, cancellationToken: cancellationToken).ConfigureAwait(false));
			}
			if (!sourceUnits.Keys.ToHashSet().SetEquals(sourceKeys)) throw new InvalidDataException("源 Patch 已变化或缺少计划中的真实来源 Unit；请重新打开并确认计划。");
			var requestedMaterialIds = CollectMappedSourceMaterialIds(mappings, sourceUnits);
			var materialDependencies = await materialDependencyResolver.ResolveAsync(
				requestedMaterialIds,
				sourceEntries,
				request.GameDataDirectory,
				new Dictionary<AdaptationAssetKey, IReadOnlyList<string>>(),
				cancellationToken).ConfigureAwait(false);
			IReadOnlySet<ulong>? allowedMaterialIds = null;
			if (request.MaterialBindingMode == CrossArmorMaterialBindingMode.RequireCompleteSourceClosure)
			{
				allowedMaterialIds = requestedMaterialIds.Except(materialDependencies.RejectedMaterialReasons.Keys).ToHashSet();
			}
			var outputBuilder = new AdaptationSdkStyleTargetShellPatchOutputBuilder(
				new HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellUnitReconstructor(
					reencoder: new HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true),
					writer: new HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshWriter(allowBoneInfoRelocation: true, allowTransformInfoRelocation: true),
					propagateSourceMaterials: true,
					allowedSourceMaterialIds: allowedMaterialIds));

			var resolver = new AdaptationGameDataPackageResolver(request.GameDataDirectory);
			var avatarRig = await new AdaptationSdkStyleAvatarRigReader(resolver).ReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
			var targetReader = new AdaptationGameDataUnitMeshReader(resolver);
			var workItems = new List<AdaptationSdkStyleTargetShellPatchWorkItem>();
			foreach (var group in request.Plan.Mappings.GroupBy(mapping => mapping.PhysicalTarget.UnitAssetKey).OrderBy(group => group.Key.FileId))
			{
				var targetArchiveId = FindTargetArchiveId(request.Plan, group.First().PhysicalTarget);
				if (targetArchiveId is null) throw new InvalidDataException($"目标 Unit 0x{group.Key.FileId:x16} 未关联到所选目标 archive。");
				var targetKey = ToAdaptationKey(group.Key);
				var targetUnit = await targetReader.ReadAsync(targetArchiveId, targetKey, allowGlobalDependencySearch: true, cancellationToken: cancellationToken).ConfigureAwait(false);
				var unitMappings = group.Where(mapping => mapping.WillReplace)
					.Select(mapping => new AdaptationTargetShellMeshMapping(ToAdaptationKey(mapping.Source!.UnitAssetKey), mapping.Source.MeshInfoIndex, mapping.PhysicalTarget.MeshInfoIndex))
					.ToArray();
				var effectiveUnitMappings = ExpandCompleteLodFamilyMappings(targetUnit.Model, sourceUnits, unitMappings);
				var requiredSources = effectiveUnitMappings.Select(mapping => sourceUnits[mapping.SourceUnitAssetKey]).Distinct().ToArray();
				var expandedTargetModel = targetUnit.Model;
				foreach (var mapping in effectiveUnitMappings)
				{
					expandedTargetModel = transformInfoExpander.Expand(expandedTargetModel, mapping.TargetMeshInfoIndex, sourceUnits[mapping.SourceUnitAssetKey].Model, mapping.SourceMeshInfoIndex, avatarRig.TransformInfo);
				}
				targetUnit = targetUnit with { Model = expandedTargetModel };
				foreach (var mapping in effectiveUnitMappings)
				{
					transferLayoutDiagnostics.Add(CreateTransferLayoutDiagnostic(
						targetKey,
						mapping.TargetMeshInfoIndex,
						mapping.SourceUnitAssetKey,
						mapping.SourceMeshInfoIndex,
						targetUnit.Model,
						sourceUnits[mapping.SourceUnitAssetKey].Model));
					var skinning = skinningDiagnosticAnalyzer.Analyze(sourceUnits[mapping.SourceUnitAssetKey].Model, mapping.SourceMeshInfoIndex);
					skinningDiagnostics.Add(new
					{
						TargetUnit = $"0x{targetKey.FileId:x16}",
						mapping.TargetMeshInfoIndex,
						SourceUnit = $"0x{mapping.SourceUnitAssetKey.FileId:x16}",
						mapping.SourceMeshInfoIndex,
						Diagnostic = skinning
					});
					var diagnostic = boneDiagnosticAnalyzer.Analyze(targetUnit.Model, mapping.TargetMeshInfoIndex, sourceUnits[mapping.SourceUnitAssetKey].Model, mapping.SourceMeshInfoIndex);
					boneDiagnostics.Add(new
					{
						TargetUnit = $"0x{targetKey.FileId:x16}",
						mapping.TargetMeshInfoIndex,
						SourceUnit = $"0x{mapping.SourceUnitAssetKey.FileId:x16}",
						mapping.SourceMeshInfoIndex,
						Diagnostic = diagnostic
					});
					if (diagnostic.Status is not ("DirectTargetCompatible" or "NeedsBoneInfoRelocation"))
					{
						unsafeBoneMappings.Add($"target 0x{targetKey.FileId:x16}/mesh {mapping.TargetMeshInfoIndex} <- source 0x{mapping.SourceUnitAssetKey.FileId:x16}/mesh {mapping.SourceMeshInfoIndex}: {diagnostic.Status}");
					}
				}
				workItems.Add(new AdaptationSdkStyleTargetShellPatchWorkItem(targetUnit, requiredSources, effectiveUnitMappings));
			}
			if (unsafeBoneMappings.Count != 0)
			{
				throw new InvalidDataException($"跨护甲候选包含尚不安全的骨骼映射，已在写出前阻止：{string.Join("; ", unsafeBoneMappings)}。请查看 cross-armor-bone-diagnostic.json。当前仅允许 DirectTargetCompatible 和 NeedsBoneInfoRelocation。");
			}
			var output = outputBuilder.Build(workItems);
			var removals = await GetSourceUnitAndCompositeRemovalsAsync(sourceEntries, cancellationToken).ConfigureAwait(false);
			var headerArchiveId = request.Plan.SelectedTargets.First().ArchiveId;
			var headerTemplate = await resolver.GetPackageTocAsync(headerArchiveId, cancellationToken).ConfigureAwait(false)
				?? throw new FileNotFoundException("无法读取所选目标 archive 的 current TOC。", headerArchiveId);
			Directory.CreateDirectory(request.OutputDirectory);
			var preservedSourceKeys = sourceEntries.Where(entry => !removals.Contains(entry)).Select(entry => entry.AssetKey).ToHashSet();
			var additionalEntries = output.AdditionalEntries
				.Concat((request.MaterialBindingMode == CrossArmorMaterialBindingMode.RequireCompleteSourceClosure
					? materialDependencies.Entries
					: Array.Empty<HD2ModAdaptation.PatchReconstruction.PatchArchiveAdditionalEntry>()).Where(entry => !preservedSourceKeys.Contains(entry.AssetKey)))
				.GroupBy(entry => entry.AssetKey)
				.Select(group => group.First())
				.ToArray();
			var write = await archiveWriter.WriteAsync(request.SourcePatchTocPath, request.OutputDirectory, Array.Empty<HD2ModAdaptation.PatchReconstruction.PatchUnitMeshEditResult>(), additionalEntries, removals, preserveOriginalStream: true, headerTemplateTocData: headerTemplate.Data, cancellationToken: cancellationToken).ConfigureAwait(false);
			await VerifyAsync(write.TocFilePath, output.UnitResults.Select(result => result.TargetUnitAssetKey).ToHashSet(), cancellationToken).ConfigureAwait(false);
			var outputEntries = await scanner.ScanEntriesAsync(write.TocFilePath, cancellationToken).ConfigureAwait(false);
			foreach (var mapping in mappings)
			{
				var targetKey = ToAdaptationKey(mapping.PhysicalTarget.UnitAssetKey);
				var outputEntry = outputEntries.SingleOrDefault(entry => entry.AssetKey == targetKey)
					?? throw new InvalidDataException($"输出 Patch 缺少目标 Unit 0x{targetKey.FileId:x16}。" );
				var outputUnit = await unitReader.ReadAsync(outputEntry, outputEntries, cancellationToken: cancellationToken).ConfigureAwait(false);
				EnsureOutputPreservesSourceVertexColor(outputUnit.Model, mapping.PhysicalTarget.MeshInfoIndex, sourceUnits[ToAdaptationKey(mapping.Source!.UnitAssetKey)].Model, mapping.Source.MeshInfoIndex);
				outputTransferLayoutDiagnostics.Add(CreateTransferLayoutDiagnostic(
					targetKey,
					mapping.PhysicalTarget.MeshInfoIndex,
					ToAdaptationKey(mapping.Source.UnitAssetKey),
					mapping.Source.MeshInfoIndex,
					outputUnit.Model,
					sourceUnits[ToAdaptationKey(mapping.Source.UnitAssetKey)].Model));
			}
			var reportPath = await WriteReportAsync(request, write.TocFilePath, output, boneDiagnostics, skinningDiagnostics, transferLayoutDiagnostics, outputTransferLayoutDiagnostics, requestedMaterialIds, materialDependencies, cancellationToken).ConfigureAwait(false);
			return new CrossArmorTransferCandidateResult(true, request.OutputDirectory, reportPath, output.UnitResults.Count, output.UnitResults.Sum(result => result.ReplacementCount), output.UnitResults.Sum(result => result.MinifiedCount), issues);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or KeyNotFoundException or OverflowException)
		{
			if (boneDiagnostics.Count != 0 && !string.IsNullOrWhiteSpace(request.OutputDirectory))
			{
				Directory.CreateDirectory(request.OutputDirectory);
				await WriteFailureDiagnosticAsync(request.OutputDirectory, exception, boneDiagnostics, cancellationToken).ConfigureAwait(false);
			}
			return Failure("CrossArmorWriteFailed", exception.Message, issues, request.OutputDirectory);
		}
	}

	private static IReadOnlyCollection<ulong> CollectMappedSourceMaterialIds(
		IReadOnlyCollection<CrossArmorTransferMapping> mappings,
		IReadOnlyDictionary<AdaptationAssetKey, AdaptationPatchUnitMesh> sourceUnits)
	{
		var result = new HashSet<ulong>();
		foreach (var mapping in mappings)
		{
			var source = mapping.Source!;
			var model = sourceUnits[ToAdaptationKey(source.UnitAssetKey)].Model;
			var mesh = model.Meshes.FirstOrDefault(item => item.Index == source.MeshInfoIndex)
				?? throw new KeyNotFoundException($"源 Unit 0x{source.UnitAssetKey.FileId:x16} 不包含 MeshInfo {source.MeshInfoIndex}。");
			var rawMesh = model.RawMeshData.FirstOrDefault(item => item.MeshInfoIndex == source.MeshInfoIndex)
				?? throw new KeyNotFoundException($"源 Unit 0x{source.UnitAssetKey.FileId:x16} 不包含 mesh {source.MeshInfoIndex}。");
			foreach (var section in rawMesh.Sections)
			{
				if (section.MaterialIndex >= mesh.MaterialSlotIds.Count) continue;
				var slot = mesh.MaterialSlotIds[(int)section.MaterialIndex];
				foreach (var material in model.Materials.Where(binding => binding.SectionId == slot)) result.Add(material.MaterialId);
			}
		}
		return result.OrderBy(id => id).ToArray();
	}

	private static string? FindTargetArchiveId(CrossArmorTransferPlan plan, CrossArmorPhysicalTargetKey target)
		=> plan.SelectedTargets.FirstOrDefault(entry => entry.Parts.Any(part => part.UnitAssetKey == target.UnitAssetKey && part.MeshInfoIndex == target.MeshInfoIndex))?.ArchiveId;

	private static IReadOnlyList<AdaptationTargetShellMeshMapping> ExpandCompleteLodFamilyMappings(
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel targetModel,
		IReadOnlyDictionary<AdaptationAssetKey, AdaptationPatchUnitMesh> sourceUnits,
		IReadOnlyList<AdaptationTargetShellMeshMapping> approvedMappings)
	{
		// A selected visual LOD0 is only one member of a Unit's render family.  SDK saves
		// the matching source geometry into every sibling LOD; minifying those siblings
		// leaves only the chest vulnerable to the target's fallback rendering path.
		if (approvedMappings.Count != 1) return approvedMappings;
		var approved = approvedMappings[0];
		var sourceModel = sourceUnits[approved.SourceUnitAssetKey].Model;
		var targetRenderFamily = targetModel.RawMeshData
			.Where(mesh => mesh.LodIndex is >= 0 and <= 4)
			.OrderBy(mesh => mesh.MeshInfoIndex)
			.ToArray();
		var sourceRenderFamily = sourceModel.RawMeshData
			.Where(mesh => mesh.LodIndex is -1 or >= 0 and <= 3)
			.OrderBy(mesh => mesh.MeshInfoIndex)
			.ToArray();
		if (approved.TargetMeshInfoIndex != targetRenderFamily.SingleOrDefault(mesh => mesh.LodIndex == 0)?.MeshInfoIndex
			|| approved.SourceMeshInfoIndex != sourceRenderFamily.SingleOrDefault(mesh => mesh.LodIndex == 0)?.MeshInfoIndex
			|| targetRenderFamily.Length != sourceRenderFamily.Length
			|| targetRenderFamily.Length < 2) return approvedMappings;

		var sourceByLod = sourceRenderFamily
			.GroupBy(mesh => mesh.LodIndex)
			.ToDictionary(group => group.Key, group => group.ToArray());
		var expanded = new List<AdaptationTargetShellMeshMapping>(targetRenderFamily.Length);
		foreach (var targetMesh in targetRenderFamily)
		{
			var sourceLod = targetMesh.LodIndex == 4 ? -1 : targetMesh.LodIndex;
			if (!sourceByLod.TryGetValue(sourceLod, out var sourceCandidates) || sourceCandidates.Length != 1)
			{
				return approvedMappings;
			}
			var sourceMesh = sourceCandidates[0];
			// Re-encoding is intentionally limited to equal section layouts.  The failed
			// section-rebuild route changes target metadata and can corrupt skinning. A
			// partial LOD family is also invalid: its untouched sibling BoneInfo palette
			// may lack matrices required by the expanded transform table.
			if (sourceMesh.Sections.Count != targetMesh.Sections.Count)
			{
				return approvedMappings;
			}
			expanded.Add(new AdaptationTargetShellMeshMapping(approved.SourceUnitAssetKey, sourceMesh.MeshInfoIndex, targetMesh.MeshInfoIndex));
		}

		return expanded.Any(mapping => mapping.TargetMeshInfoIndex == approved.TargetMeshInfoIndex && mapping.SourceMeshInfoIndex == approved.SourceMeshInfoIndex)
			? expanded
			: approvedMappings;
	}

	private async ValueTask<IReadOnlyList<AdaptationPatchTocEntry>> GetSourceUnitAndCompositeRemovalsAsync(IReadOnlyList<AdaptationPatchTocEntry> entries, CancellationToken cancellationToken)
	{
		var units = entries.Where(entry => entry.AssetKey.TypeId == AdaptationPatchUnitMeshReader.UnitTypeId).ToArray();
		var compositeIds = new HashSet<ulong>();
		var payloadReader = new AdaptationPatchEntryPayloadReader();
		foreach (var unit in units)
		{
			var payload = await payloadReader.ReadPayloadAsync(unit, cancellationToken).ConfigureAwait(false);
			if (payload.TocData.Length >= 24)
			{
				var compositeId = BitConverter.ToUInt64(payload.TocData, 16);
				if (compositeId != 0) compositeIds.Add(compositeId);
			}
		}
		return units.Concat(entries.Where(entry => entry.AssetKey.TypeId == CompositeUnitTypeId && compositeIds.Contains(entry.AssetKey.FileId))).ToArray();
	}

	private async ValueTask VerifyAsync(string tocPath, IReadOnlySet<AdaptationAssetKey> expectedUnits, CancellationToken cancellationToken)
	{
		var entries = await scanner.ScanEntriesAsync(tocPath, cancellationToken).ConfigureAwait(false);
		var actualUnits = entries.Where(entry => entry.AssetKey.TypeId == AdaptationPatchUnitMeshReader.UnitTypeId).Select(entry => entry.AssetKey).ToHashSet();
		if (!actualUnits.SetEquals(expectedUnits)) throw new InvalidDataException("输出 Unit 集合与批准的物理目标集合不一致。");
		if (entries.GroupBy(entry => entry.AssetKey).Any(group => group.Count() != 1)) throw new InvalidDataException("输出包含重复 AssetKey。");
	}

	private static async ValueTask<string> WriteReportAsync(
		CrossArmorTransferCandidateRequest request,
		string tocPath,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle.SdkStyleTargetShellPatchOutput output,
		IReadOnlyList<object> boneDiagnostics,
		IReadOnlyList<object> skinningDiagnostics,
		IReadOnlyList<object> transferLayoutDiagnostics,
		IReadOnlyList<object> outputTransferLayoutDiagnostics,
		IReadOnlyCollection<ulong> requestedMaterialIds,
		HD2ModAdaptation.PatchReconstruction.MaterialDependencyResolutionResult materialDependencies,
		CancellationToken cancellationToken)
	{
		var path = Path.Combine(request.OutputDirectory, "cross-armor-transfer-report.json");
		var report = new
		{
			GeneratedUtc = DateTimeOffset.UtcNow,
			SourcePatch = request.SourcePatchTocPath,
			OutputPatch = tocPath,
			IsolatedCandidate = true,
			MaterialPolicy = request.MaterialBindingMode == CrossArmorMaterialBindingMode.PreserveSourceReferences
				? "Source material bindings are preserved directly on rebuilt target slots. Existing non-Unit source Material and Texture entries remain in the output without dependency-closure validation."
				: "Closure-complete source materials are embedded and propagated per target slot; unresolved source materials fall back to current target bindings.",
			Materials = new
			{
				BindingMode = request.MaterialBindingMode.ToString(),
				Requested = requestedMaterialIds.Select(id => $"0x{id:x16}").ToArray(),
				Propagated = output.UnitResults.SelectMany(result => result.ReplacementMaterialIds).Distinct().OrderBy(id => id).Select(id => $"0x{id:x16}").ToArray(),
				Rejected = materialDependencies.RejectedMaterialReasons.OrderBy(pair => pair.Key).Select(pair => new { Material = $"0x{pair.Key:x16}", Reason = pair.Value }).ToArray(),
				ResolvedDependencyCount = materialDependencies.Entries.Count,
				EmbeddedDependencyCount = request.MaterialBindingMode == CrossArmorMaterialBindingMode.RequireCompleteSourceClosure ? materialDependencies.Entries.Count : 0,
				Origins = materialDependencies.Origins.OrderBy(pair => pair.Key.TypeId).ThenBy(pair => pair.Key.FileId).Select(pair => new { Asset = $"0x{pair.Key.TypeId:x16}/0x{pair.Key.FileId:x16}", Kind = pair.Value.Kind.ToString(), pair.Value.Name }).ToArray()
			},
			BoneDiagnostics = boneDiagnostics,
			SkinningDiagnostics = skinningDiagnostics,
			TransferLayoutDiagnostics = transferLayoutDiagnostics,
			OutputTransferLayoutDiagnostics = outputTransferLayoutDiagnostics,
			SkinningRisks = skinningDiagnostics
				.Select(diagnostic => JsonSerializer.SerializeToElement(diagnostic))
				.Where(diagnostic => diagnostic.GetProperty("Diagnostic").GetProperty("ZeroActiveWeightVertexCount").GetInt32() != 0
					|| diagnostic.GetProperty("Diagnostic").GetProperty("InvalidActiveInfluenceCount").GetInt32() != 0
					|| diagnostic.GetProperty("Diagnostic").GetProperty("NonFinitePositionCount").GetInt32() != 0)
				.ToArray(),
			Mappings = request.Plan.Mappings.Select(mapping => new
			{
				TargetUnit = $"0x{mapping.PhysicalTarget.UnitAssetKey.FileId:x16}",
				mapping.PhysicalTarget.MeshInfoIndex,
				Target = new { mapping.Target.PartKind, mapping.Target.Layer, mapping.Target.BodyVariant, mapping.Target.SemanticName },
				SourceUnit = mapping.Source is null ? null : $"0x{mapping.Source.UnitAssetKey.FileId:x16}",
				SourceMeshInfoIndex = mapping.Source?.MeshInfoIndex,
				Source = mapping.Source is null ? null : new { mapping.Source.PartKind, mapping.Source.Layer, mapping.Source.BodyVariant, mapping.Source.SemanticName },
				mapping.WillReplace,
				mapping.IsManual,
				mapping.IsSuppressed,
				mapping.Reason
			}).ToArray(),
			Units = output.UnitResults.Select(result => new { TargetUnit = $"0x{result.TargetUnitAssetKey.FileId:x16}", result.ReplacementCount, result.MinifiedCount, result.CoveredTargetMeshCount }).ToArray()
		};
		await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
		return path;
	}

	private static object CreateTransferLayoutDiagnostic(
		AdaptationAssetKey targetUnitKey,
		int targetMeshInfoIndex,
		AdaptationAssetKey sourceUnitKey,
		int sourceMeshInfoIndex,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel targetModel,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel sourceModel)
	{
		var sourceMesh = sourceModel.Meshes.Single(mesh => mesh.Index == sourceMeshInfoIndex);
		var targetMesh = targetModel.Meshes.Single(mesh => mesh.Index == targetMeshInfoIndex);
		var sourceRawMesh = sourceModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == sourceMeshInfoIndex);
		var targetRawMesh = targetModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == targetMeshInfoIndex);
		var sourceStream = sourceModel.Streams.Single(stream => stream.Index == sourceRawMesh.StreamIndex);
		var targetStream = targetModel.Streams.Single(stream => stream.Index == targetRawMesh.StreamIndex);
		var sourceSections = sourceRawMesh.Sections.Select((section, index) => DescribeSection(index, section, sourceMesh, sourceModel.Materials)).ToArray();
		var targetSections = targetRawMesh.Sections.Select((section, index) => DescribeSection(index, section, targetMesh, targetModel.Materials)).ToArray();
		var assignments = sourceSections.Select(source =>
		{
			var target = targetSections[source.Index % targetSections.Length];
			return new { SourceSectionIndex = source.Index, SourceMaterialId = source.MaterialId, TargetSectionIndex = target.Index, TargetMaterialId = target.MaterialId, TargetMaterialSlot = target.MaterialSlot };
		}).ToArray();
		var targetSectionCollisions = assignments
			.GroupBy(assignment => assignment.TargetSectionIndex)
			.Select(group => new
			{
				TargetSectionIndex = group.Key,
				SourceSectionIndexes = group.Select(item => item.SourceSectionIndex).ToArray(),
				SourceMaterialIds = group.Select(item => item.SourceMaterialId).Distinct().OrderBy(id => id).ToArray(),
				CanPreserveOneSourceMaterial = group.Select(item => item.SourceMaterialId).Distinct().Count() == 1
			})
			.Where(group => group.SourceSectionIndexes.Length > 1 || !group.CanPreserveOneSourceMaterial)
			.ToArray();
		return new
		{
			TargetUnit = $"0x{targetUnitKey.FileId:x16}",
			TargetMeshInfoIndex = targetMeshInfoIndex,
			SourceUnit = $"0x{sourceUnitKey.FileId:x16}",
			SourceMeshInfoIndex = sourceMeshInfoIndex,
			Source = new { StreamIndex = sourceRawMesh.StreamIndex, ComponentInfoId = $"0x{sourceStream.ComponentInfoId:x16}", sourceStream.VertexBufferId, sourceStream.IndexBufferId, sourceStream.VertexStride, SectionCount = sourceSections.Length, MaterialSlotCount = sourceMesh.MaterialSlotIds.Count, Sections = sourceSections, Components = DescribeComponents(sourceStream.Components) },
			Target = new { StreamIndex = targetRawMesh.StreamIndex, ComponentInfoId = $"0x{targetStream.ComponentInfoId:x16}", targetStream.VertexBufferId, targetStream.IndexBufferId, targetStream.VertexStride, SectionCount = targetSections.Length, MaterialSlotCount = targetMesh.MaterialSlotIds.Count, Sections = targetSections, Components = DescribeComponents(targetStream.Components) },
			SectionAssignments = assignments,
			TargetSectionCollisions = targetSectionCollisions,
			VertexComponentCompatibility = targetStream.Components.Select(target =>
			{
				var exact = sourceStream.Components.FirstOrDefault(source => source.Type == target.Type && source.Index == target.Index);
				var fallback = sourceStream.Components.FirstOrDefault(source => source.Type == target.Type);
				var selected = exact ?? fallback;
				return new
				{
					Target = DescribeComponent(target),
					SourceMatch = selected is null ? null : DescribeComponent(selected),
					MatchKind = exact is not null ? "ExactTypeAndIndex" : fallback is not null ? "TypeOnlyFallback" : "Missing",
					FormatMatches = selected is not null && selected.Format == target.Format,
					SizeMatches = selected is not null && selected.Size == target.Size
				};
			}).ToArray()
		};
	}

	private static void EnsureOutputPreservesSourceVertexColor(
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel outputModel,
		int outputMeshInfoIndex,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshModel sourceModel,
		int sourceMeshInfoIndex)
	{
		var sourceRawMesh = sourceModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == sourceMeshInfoIndex);
		var outputRawMesh = outputModel.RawMeshData.Single(mesh => mesh.MeshInfoIndex == outputMeshInfoIndex);
		var sourceStream = sourceModel.Streams.Single(stream => stream.Index == sourceRawMesh.StreamIndex);
		var outputStream = outputModel.Streams.Single(stream => stream.Index == outputRawMesh.StreamIndex);
		var sourceColor = sourceStream.Components.FirstOrDefault(component => component.Type == 5 && component.Index == 0);
		if (sourceColor is null) return;
		var outputColor = outputStream.Components.FirstOrDefault(component => component.Type == 5 && component.Index == 0);
		if (outputColor is null || outputColor.Format != sourceColor.Format || outputColor.Size != sourceColor.Size)
		{
			throw new InvalidDataException($"输出 target mesh {outputMeshInfoIndex} 未保留来源 vertex color 布局。" );
		}
	}

	private static TransferSectionDiagnostic DescribeSection(
		int index,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitRawMeshSectionData section,
		HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMeshInfo mesh,
		IReadOnlyList<HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitMaterialBinding> bindings)
	{
		var materialSlot = section.MaterialIndex < mesh.MaterialSlotIds.Count
			? mesh.MaterialSlotIds[(int)section.MaterialIndex]
			: section.MaterialSlotId;
		var materialIds = bindings.Where(binding => binding.SectionId == materialSlot).Select(binding => binding.MaterialId).Distinct().OrderBy(id => id).ToArray();
		return new TransferSectionDiagnostic(index, section.MaterialIndex, materialSlot, section.MaterialSlotId, section.Triangles.Count, materialIds.Length == 1 ? $"0x{materialIds[0]:x16}" : null, materialIds.Select(id => $"0x{id:x16}").ToArray());
	}

	private static object[] DescribeComponents(IReadOnlyList<HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitStreamComponentInfo> components)
		=> components.Select(DescribeComponent).ToArray();

	private static object DescribeComponent(HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitStreamComponentInfo component)
		=> new { component.Type, component.TypeName, component.Index, component.Format, component.FormatName, component.Size, Unknown = $"0x{component.Unknown:x16}" };

	private sealed record TransferSectionDiagnostic(int Index, uint MaterialIndex, uint MaterialSlot, uint RawMaterialSlot, int TriangleCount, string? MaterialId, IReadOnlyList<string> MaterialIds);

	private static async ValueTask WriteFailureDiagnosticAsync(string outputDirectory, Exception exception, IReadOnlyList<object> boneDiagnostics, CancellationToken cancellationToken)
	{
		var path = Path.Combine(outputDirectory, "cross-armor-bone-diagnostic.json");
		var report = new
		{
			GeneratedUtc = DateTimeOffset.UtcNow,
			WriteSucceeded = false,
			Failure = exception.Message,
			BoneDiagnostics = boneDiagnostics
		};
		await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
	}

	private static AdaptationAssetKey ToAdaptationKey(AssetKey key) => new(key.TypeId, key.FileId);
	private static CrossArmorTransferCandidateResult Failure(string code, string message, List<CoreIssue> issues, string? outputDirectory = null)
	{
		issues.Add(new CoreIssue(CoreIssueSeverity.Error, code, message));
		return new CrossArmorTransferCandidateResult(false, outputDirectory, null, 0, 0, 0, issues);
	}
}
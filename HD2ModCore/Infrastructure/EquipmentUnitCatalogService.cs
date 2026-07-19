using System.Text.Json;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using Microsoft.Data.Sqlite;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;
using AdaptationPatchTocScanner = HD2ModAdaptation.PatchReconstruction.PatchTocScanner;
using AdaptationPatchUnitMeshReader = HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMeshReader;

namespace HD2ModCore.Infrastructure;

// Purpose: Projects SQLite Armor and Helmet Unit-part facts into a no-write source/target transfer preview.
public sealed class EquipmentUnitCatalogService : IEquipmentUnitCatalogService
{
	private readonly StoragePaths paths;

	public EquipmentUnitCatalogService(StoragePaths paths)
	{
		this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
	}

	public async ValueTask<IReadOnlyList<EquipmentUnitCatalogEntry>> GetEntriesAsync(IReadOnlySet<AssetKey>? unitAssetKeys = null, CancellationToken cancellationToken = default)
	{
		if (!File.Exists(paths.DbPath)) return Array.Empty<EquipmentUnitCatalogEntry>();
		var unitIds = unitAssetKeys?
			.Where(key => key.TypeId == AdaptationPatchUnitMeshReader.UnitTypeId)
			.Select(key => unchecked((long)key.FileId))
			.Distinct()
			.ToArray();
		if (unitAssetKeys is not null && unitIds is { Length: 0 }) return Array.Empty<EquipmentUnitCatalogEntry>();
		await using var connection = new SqliteConnection($"Data Source={paths.DbPath};Mode=ReadOnly;Cache=Shared");
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = connection.CreateCommand();
		command.CommandText = @"
SELECT a.archive_id,a.category,a.display_name,
			 p.unit_type_id,p.unit_file_id,p.mesh_info_index,p.mesh_id,p.part_kind,p.part_layer,p.body_variant,p.semantic_name,p.confidence,
			 COALESCE((SELECT MAX(e.toc_data_size + e.stream_size + e.gpu_resource_size)
								 FROM archive_entries e
								 WHERE e.archive_id=a.archive_id
									 AND e.type_id=p.unit_type_id
									 AND e.file_id=p.unit_file_id), 0) AS stored_bytes
FROM archives a
JOIN game_data_unit_parts p ON p.archive_id=a.archive_id
WHERE lower(a.category) IN ('armor','helmet')
  AND p.is_visual=1 AND p.is_lod=0
	  AND ($unitIds IS NULL OR p.unit_file_id IN (SELECT value FROM json_each($unitIds)))
ORDER BY CASE lower(a.category) WHEN 'armor' THEN 0 ELSE 1 END,a.display_name,a.archive_id,p.unit_file_id,p.mesh_info_index;";
		command.Parameters.AddWithValue("$unitIds", unitIds is null ? DBNull.Value : JsonSerializer.Serialize(unitIds));

		var rawParts = new List<(string archiveId, string category, string name, EquipmentUnitPart part)>();
		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var unitKey = new AssetKey(unchecked((ulong)reader.GetInt64(3)), unchecked((ulong)reader.GetInt64(4)));
			rawParts.Add((
				reader.GetString(0), reader.GetString(1), reader.GetString(2),
				new EquipmentUnitPart(
					unitKey,
					reader.GetInt32(5),
					unchecked((uint)reader.GetInt64(6)),
					(UnitMeshPartKind)reader.GetInt32(7),
					(UnitMeshPartLayer)reader.GetInt32(8),
					(UnitMeshBodyVariant)reader.GetInt32(9),
					reader.GetString(10), reader.GetInt32(11), Array.Empty<string>())
					with { StoredBytes = reader.GetInt64(12) }));
		}

		var unitKeys = rawParts.Select(item => item.part.UnitAssetKey).Distinct().ToArray();
		var sharedByUnit = await ReadSharedArchiveIdsAsync(connection, unitKeys, cancellationToken).ConfigureAwait(false);
		return rawParts
			.GroupBy(item => new { item.archiveId, item.category, item.name })
			.Select(group => new EquipmentUnitCatalogEntry(
				group.Key.archiveId,
				group.Key.category,
				group.Key.name,
				group.Select(item => item.part with { SharedArchiveIds = sharedByUnit.GetValueOrDefault(item.part.UnitAssetKey, Array.Empty<string>()) }).ToArray()))
			.ToArray();
	}

	public async ValueTask<IReadOnlyList<EquipmentUnitCatalogEntry>> FilterTransferableSourcePartsAsync(
		IReadOnlyList<EquipmentUnitCatalogEntry> candidates,
		IReadOnlyList<string> patchTocPaths,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		ArgumentNullException.ThrowIfNull(patchTocPaths);
		var candidateKeys = candidates.SelectMany(entry => entry.Parts).Select(part => part.UnitAssetKey).ToHashSet();
		var transferableMeshes = new HashSet<(AssetKey unit, int mesh)>();
		var tocScanner = new AdaptationPatchTocScanner();
		var unitReader = new AdaptationPatchUnitMeshReader();
		foreach (var patchPath in patchTocPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var entries = await tocScanner.ScanEntriesAsync(patchPath, cancellationToken).ConfigureAwait(false);
			foreach (var entry in entries.Where(entry => candidateKeys.Contains(ToCoreKey(entry.AssetKey))))
			{
				try
				{
					var unit = await unitReader.ReadAsync(entry, entries, cancellationToken: cancellationToken).ConfigureAwait(false);
					foreach (var mesh in unit.Model.RawMeshData.Where(HasTransferableGeometry))
					{
						transferableMeshes.Add((ToCoreKey(entry.AssetKey), mesh.MeshInfoIndex));
					}
				}
				catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or OverflowException or KeyNotFoundException)
				{
					// A malformed source Unit must not be presented as a transferable model.
				}
			}
		}
		return candidates
			.Select(entry => entry with { Parts = entry.Parts.Where(part => transferableMeshes.Contains((part.UnitAssetKey, part.MeshInfoIndex))).ToArray() })
			.Where(entry => entry.Parts.Count != 0)
			.ToArray();
	}

	public ValueTask<CrossArmorTransferPlan> CreatePlanAsync(
		IReadOnlyList<EquipmentUnitCatalogEntry> sourceCandidates,
		IReadOnlyList<EquipmentUnitCatalogEntry> targetCandidates,
		string? selectedSourceArchiveId,
		UnitMeshBodyVariant? selectedSourceBodyVariant,
		CrossArmorBodyVariantPreference bodyVariantPreference,
		CrossArmorLayerPreference layerPreference,
		IReadOnlyCollection<string> selectedTargetArchiveIds,
		IReadOnlyList<CrossArmorManualMapping>? manualMappings = null,
		IReadOnlyList<CrossArmorManualSuppression>? manualSuppressions = null,
		bool manualMode = false,
		IReadOnlyCollection<string>? additionalSourceArchiveIds = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sourceCandidates);
		ArgumentNullException.ThrowIfNull(targetCandidates);
		ArgumentNullException.ThrowIfNull(selectedTargetArchiveIds);
		var issues = new List<CoreIssue>();
		var source = sourceCandidates.SingleOrDefault(candidate => string.Equals(candidate.ArchiveId, selectedSourceArchiveId, StringComparison.OrdinalIgnoreCase));
		if (source is null)
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, "SourceEquipmentRequired", "请选择一个来源 Armor 或 Helmet。"));
			return ValueTask.FromResult(new CrossArmorTransferPlan(sourceCandidates, null, Array.Empty<EquipmentUnitCatalogEntry>(), Array.Empty<CrossArmorTransferMapping>(), Array.Empty<CrossArmorTransferImpact>(), issues));
		}
		var targets = targetCandidates.Where(candidate => selectedTargetArchiveIds.Contains(candidate.ArchiveId, StringComparer.OrdinalIgnoreCase)).ToArray();
		if (targets.Length == 0)
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, "TargetEquipmentRequired", "请至少选择一个目标 Armor 或 Helmet。"));
			return ValueTask.FromResult(new CrossArmorTransferPlan(sourceCandidates, source, targets, Array.Empty<CrossArmorTransferMapping>(), Array.Empty<CrossArmorTransferImpact>(), issues));
		}

		var sourceArchiveIds = new[] { source.ArchiveId }.Concat(additionalSourceArchiveIds ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var sourceParts = sourceCandidates.Where(candidate => sourceArchiveIds.Contains(candidate.ArchiveId)).SelectMany(candidate => candidate.Parts)
			.Where(part => part.PartKind != UnitMeshPartKind.Unknown && part.Confidence == 100)
			.Where(part => selectedSourceBodyVariant is null or UnitMeshBodyVariant.Unknown or UnitMeshBodyVariant.Any || part.BodyVariant == selectedSourceBodyVariant || part.BodyVariant == UnitMeshBodyVariant.Any)
			.OrderBy(part => part.PartKind).ThenBy(part => part.Layer).ThenBy(part => part.MeshInfoIndex)
			.ToArray();
		var sourceCategoryByMesh = sourceCandidates.Where(candidate => sourceArchiveIds.Contains(candidate.ArchiveId))
			.SelectMany(candidate => candidate.Parts.Select(part => new { Key = new SourceMeshKey(part.UnitAssetKey, part.MeshInfoIndex), candidate.Category }))
			.GroupBy(item => item.Key)
			.ToDictionary(group => group.Key, group => group.First().Category);
		if (sourceParts.Length == 0)
		{
			issues.Add(new CoreIssue(CoreIssueSeverity.Error, "SelectedSourceHasNoTransferParts", "所选来源没有可识别的可见非 LOD 部件。"));
		}

		var targetsByPhysicalMesh = targets
			.SelectMany(target => target.Parts.Select(part => new TargetUse(target, part)))
			.Where(item => item.Part.PartKind != UnitMeshPartKind.Unknown && item.Part.Confidence == 100)
			.GroupBy(item => new CrossArmorPhysicalTargetKey(item.Part.UnitAssetKey, item.Part.MeshInfoIndex))
			.OrderBy(group => group.Key.UnitAssetKey.FileId).ThenBy(group => group.Key.MeshInfoIndex)
			.ToArray();
		var manualByTarget = (manualMappings ?? Array.Empty<CrossArmorManualMapping>()).ToDictionary(mapping => mapping.Target);
		var suppressedTargets = (manualSuppressions ?? Array.Empty<CrossArmorManualSuppression>()).Select(suppression => suppression.Target).ToHashSet();
		var assignments = new Dictionary<CrossArmorPhysicalTargetKey, (EquipmentUnitPart? Source, bool IsManual, bool IsSuppressed, int HitCount)>();
		var sourcePools = BuildSourceHitPools(sourceParts, sourceCategoryByMesh, targetsByPhysicalMesh, targets, selectedSourceBodyVariant);
		var targetBudgets = targetsByPhysicalMesh.ToDictionary(group => group.Key, group => group.Select(item => item.Entry.ArchiveId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
		foreach (var physicalTarget in OrderTargetsForAssignment(targetsByPhysicalMesh, manualByTarget))
		{
			var targetPart = physicalTarget.First().Part;
			EquipmentUnitPart? sourcePart = null;
			var isManual = false;
			var hitCount = 0;
			var isSuppressed = suppressedTargets.Contains(physicalTarget.Key);
			if (!isSuppressed && manualByTarget.TryGetValue(physicalTarget.Key, out var manual))
			{
				sourcePart = sourceParts.FirstOrDefault(part => part.UnitAssetKey == manual.SourceUnitAssetKey && part.MeshInfoIndex == manual.SourceMeshInfoIndex);
				isManual = sourcePart is not null;
				if (sourcePart is null) issues.Add(new CoreIssue(CoreIssueSeverity.Warning, "ManualSourceUnavailable", $"手动来源已不在当前筛选范围：目标 Unit 0x{targetPart.UnitAssetKey.FileId:x16} mesh {targetPart.MeshInfoIndex}。"));
				else
				{
					var sourceKey = new SourceMeshKey(sourcePart.UnitAssetKey, sourcePart.MeshInfoIndex);
					if (TryGetSourcePool(sourcePools, sourceKey, out var manualPool)) manualPool.RemainingHits = Math.Max(0, manualPool.RemainingHits - 1);
					targetBudgets[physicalTarget.Key] = Math.Max(0, targetBudgets[physicalTarget.Key] - 1);
					hitCount = 1;
				}
			}
			else if (!isSuppressed && !manualMode && targetBudgets[physicalTarget.Key] > 0)
			{
				while (targetBudgets[physicalTarget.Key] > 0)
				{
					var sourcePool = sourcePools
						.Where(pool => pool.Key.PartKind == targetPart.PartKind
							&& string.Equals(pool.Key.Category, physicalTarget.First().Entry.Category, StringComparison.OrdinalIgnoreCase)
							&& pool.RemainingHits > 0)
						.OrderByDescending(pool => pool.Representative.StoredBytes)
						.ThenBy(pool => pool.Representative.UnitAssetKey.FileId).ThenBy(pool => pool.Representative.MeshInfoIndex)
						.FirstOrDefault();
					if (sourcePool is null) break;
					sourcePart ??= sourcePool.Representative;
					sourcePool.RemainingHits--;
					targetBudgets[physicalTarget.Key]--;
					hitCount++;
				}
			}
			assignments.Add(physicalTarget.Key, (sourcePart, isManual, isSuppressed, hitCount));
		}

		var mappings = new List<CrossArmorTransferMapping>();
		var impacts = new List<CrossArmorTransferImpact>();
		foreach (var physicalTarget in targetsByPhysicalMesh)
		{
			var targetUse = physicalTarget.First();
			var targetPart = targetUse.Part;
			var (sourcePart, isManual, isSuppressed, hitCount) = assignments[physicalTarget.Key];
			var willReplace = sourcePart is not null;
			var usedByIds = physicalTarget.Select(item => item.Entry.ArchiveId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			var usedByNames = physicalTarget.Select(item => item.Entry.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			mappings.Add(new CrossArmorTransferMapping(
				physicalTarget.Key, targetPart, sourcePart, willReplace,
				willReplace
					? (isManual ? "强制命中" : "命中")
					: (isSuppressed ? "强制隐藏" : "隐藏"),
				usedByIds, usedByNames, isManual, isSuppressed) { HitCount = hitCount });
			if (!willReplace) continue;
			foreach (var sharedArchiveId in targetPart.SharedArchiveIds.Where(id => !usedByIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
			{
				var impacted = sourceCandidates.FirstOrDefault(candidate => string.Equals(candidate.ArchiveId, sharedArchiveId, StringComparison.OrdinalIgnoreCase));
				if (impacted is not null) impacts.Add(new CrossArmorTransferImpact(impacted.ArchiveId, impacted.DisplayName, targetPart.UnitAssetKey, targetPart.PartKind, targetPart.Layer));
			}
		}
		if (mappings.Count == 0) issues.Add(new CoreIssue(CoreIssueSeverity.Error, "NoTargetParts", "所选目标没有可识别的可见非 LOD 部件。"));
		return ValueTask.FromResult(new CrossArmorTransferPlan(sourceCandidates, source, targets, mappings, impacts.Distinct().ToArray(), issues));
	}

	private static IEnumerable<IGrouping<CrossArmorPhysicalTargetKey, TargetUse>> OrderTargetsForAssignment(
		IReadOnlyList<IGrouping<CrossArmorPhysicalTargetKey, TargetUse>> targets,
		IReadOnlyDictionary<CrossArmorPhysicalTargetKey, CrossArmorManualMapping> manualMappings)
		=> targets
			.OrderByDescending(group => manualMappings.ContainsKey(group.Key))
			.ThenByDescending(group => group.First().Part.StoredBytes)
			.ThenBy(group => group.Key.UnitAssetKey.FileId)
			.ThenBy(group => group.Key.MeshInfoIndex);

	private static IReadOnlyList<SourceHitPool> BuildSourceHitPools(
		IReadOnlyList<EquipmentUnitPart> sourceParts,
		IReadOnlyDictionary<SourceMeshKey, string> sourceCategoryByMesh,
		IReadOnlyList<IGrouping<CrossArmorPhysicalTargetKey, TargetUse>> physicalTargets,
		IReadOnlyList<EquipmentUnitCatalogEntry> selectedTargets,
		UnitMeshBodyVariant? selectedSourceBodyVariant)
	{
		var pools = sourceParts
			.GroupBy(part => new SourcePoolKey(
				sourceCategoryByMesh.GetValueOrDefault(new SourceMeshKey(part.UnitAssetKey, part.MeshInfoIndex)) ?? string.Empty,
				part.PartKind,
				part.BodyVariant))
			.Select(group => new SourceHitPool(
				group.Key,
				group.OrderByDescending(part => part.StoredBytes).ThenBy(part => part.UnitAssetKey.FileId).ThenBy(part => part.MeshInfoIndex).First(),
				Math.Max(1, selectedTargets.Count(target => string.Equals(target.Category, group.Key.Category, StringComparison.OrdinalIgnoreCase)))))
			.ToArray();
		var helmetTargetCount = selectedTargets.Count(target => string.Equals(target.Category, "Helmet", StringComparison.OrdinalIgnoreCase));
		foreach (var pool in pools.Where(pool => string.Equals(pool.Key.Category, "Helmet", StringComparison.OrdinalIgnoreCase)))
		{
			pool.RemainingHits = Math.Max(1, helmetTargetCount);
		}
		foreach (var pool in pools.Where(pool => string.Equals(pool.Key.Category, "Armor", StringComparison.OrdinalIgnoreCase)))
		{
			var matchingTargets = physicalTargets
				.Where(group => string.Equals(group.First().Entry.Category, "Armor", StringComparison.OrdinalIgnoreCase) && group.First().Part.PartKind == pool.Key.PartKind)
				.Select(group => group.First().Part)
				.ToArray();
			if (matchingTargets.Any(target => target.BodyVariant != UnitMeshBodyVariant.Any)
				&& (pool.Key.BodyVariant == UnitMeshBodyVariant.Any || selectedSourceBodyVariant is UnitMeshBodyVariant.Slim or UnitMeshBodyVariant.Stocky)) pool.RemainingHits++;
			if (matchingTargets.Any(target => target.BodyVariant == UnitMeshBodyVariant.Any)
				&& pool.Key.BodyVariant is UnitMeshBodyVariant.Slim or UnitMeshBodyVariant.Stocky)
			{
				foreach (var alternate in pools.Where(candidate => string.Equals(candidate.Key.Category, pool.Key.Category, StringComparison.OrdinalIgnoreCase)
					&& candidate.Key.PartKind == pool.Key.PartKind
					&& candidate.Key.BodyVariant is UnitMeshBodyVariant.Slim or UnitMeshBodyVariant.Stocky
					&& candidate.Key.BodyVariant != pool.Key.BodyVariant))
				{
					alternate.RemainingHits = Math.Max(0, alternate.RemainingHits - 1);
				}
			}
		}
		return pools;
	}

	private static bool TryGetSourcePool(IEnumerable<SourceHitPool> pools, SourceMeshKey source, out SourceHitPool pool)
	{
		pool = pools.FirstOrDefault(candidate => candidate.Representative.UnitAssetKey == source.UnitAssetKey && candidate.Representative.MeshInfoIndex == source.MeshInfoIndex)!;
		return pool is not null;
	}

	private static int BestAvailableScore(EquipmentUnitPart target, IReadOnlyList<EquipmentUnitPart> sources, CrossArmorBodyVariantPreference bodyPreference, CrossArmorLayerPreference layerPreference)
		=> sources.Where(source => source.PartKind == target.PartKind && IsSemanticFamilyCompatible(source, target) && IsBodyVariantCompatible(source.BodyVariant, target.BodyVariant))
			.Select(source => ScoreLayer(source.Layer, target.Layer, layerPreference) * 10 + ScoreBodyVariant(source.BodyVariant, target.BodyVariant, bodyPreference))
			.DefaultIfEmpty(int.MaxValue)
			.Min();

	private static int ScoreLayer(UnitMeshPartLayer source, UnitMeshPartLayer target, CrossArmorLayerPreference preference)
		=> source == target ? 0 : source == PreferredLayer(preference) ? 1 : source == UnitMeshPartLayer.Unknown ? 3 : 2;

	private static int ScoreBodyVariant(UnitMeshBodyVariant source, UnitMeshBodyVariant target, CrossArmorBodyVariantPreference preference)
		=> source == target ? 0 : source == UnitMeshBodyVariant.Any ? 1 : source == PreferredBodyVariant(preference) ? 2 : 3;

	private static bool IsBodyVariantCompatible(UnitMeshBodyVariant source, UnitMeshBodyVariant target)
		=> target is UnitMeshBodyVariant.Unknown or UnitMeshBodyVariant.Other or UnitMeshBodyVariant.Any
			|| source == UnitMeshBodyVariant.Any
			|| source == target;

	private static bool IsSemanticFamilyCompatible(EquipmentUnitPart source, EquipmentUnitPart target)
	{
		if (source.Layer == UnitMeshPartLayer.Unknown || target.Layer == UnitMeshPartLayer.Unknown) return true;
		var sourceFamily = SemanticFamily(source.SemanticName);
		var targetFamily = SemanticFamily(target.SemanticName);
		return sourceFamily is null || targetFamily is null || string.Equals(sourceFamily, targetFamily, StringComparison.Ordinal);
	}

	private static string? SemanticFamily(string semanticName)
	{
		var name = semanticName.Trim().ToLowerInvariant();
		if (name.StartsWith("g_torso_arm_l_", StringComparison.Ordinal)) return "torso-arm-l";
		if (name.StartsWith("g_torso_arm_r_", StringComparison.Ordinal)) return "torso-arm-r";
		if (name.StartsWith("g_arm_l", StringComparison.Ordinal)) return "arm-l";
		if (name.StartsWith("g_arm_r", StringComparison.Ordinal)) return "arm-r";
		if (name.StartsWith("g_legs_hips_undergarment", StringComparison.Ordinal)) return "hips-undergarment";
		if (name.StartsWith("g_legs_hips", StringComparison.Ordinal)) return "hips";
		if (name.StartsWith("g_leg_undergarment_l", StringComparison.Ordinal)) return "leg-undergarment-l";
		if (name.StartsWith("g_leg_undergarment_r", StringComparison.Ordinal)) return "leg-undergarment-r";
		if (name.StartsWith("g_leg_l", StringComparison.Ordinal)) return "leg-l";
		if (name.StartsWith("g_leg_r", StringComparison.Ordinal)) return "leg-r";
		if (name.StartsWith("g_torso_undergarment", StringComparison.Ordinal)) return "torso-undergarment";
		if (name.StartsWith("g_torso", StringComparison.Ordinal)) return "torso";
		return null;
	}

	private static UnitMeshBodyVariant NormalizeTargetVariant(UnitMeshBodyVariant target, CrossArmorBodyVariantPreference preference)
		=> target is UnitMeshBodyVariant.Slim or UnitMeshBodyVariant.Stocky ? target : PreferredBodyVariant(preference);

	private static UnitMeshBodyVariant PreferredBodyVariant(CrossArmorBodyVariantPreference preference)
		=> preference == CrossArmorBodyVariantPreference.Slim ? UnitMeshBodyVariant.Slim : UnitMeshBodyVariant.Stocky;

	private static UnitMeshPartLayer PreferredLayer(CrossArmorLayerPreference preference)
		=> preference switch
		{
			CrossArmorLayerPreference.Undergarment => UnitMeshPartLayer.Undergarment,
			CrossArmorLayerPreference.Accessory => UnitMeshPartLayer.Accessory,
			_ => UnitMeshPartLayer.Armor
		};

	private static bool HasTransferableGeometry(HD2ModAdaptation.PatchReconstruction.UnitMesh.UnitRawMeshData mesh)
		=> mesh.Vertices.Count > 3 || mesh.Triangles.Count > 1;

	private static AssetKey ToCoreKey(AdaptationAssetKey assetKey) => new(assetKey.TypeId, assetKey.FileId);

	private sealed record TargetUse(EquipmentUnitCatalogEntry Entry, EquipmentUnitPart Part);
	private sealed record SourceMeshKey(AssetKey UnitAssetKey, int MeshInfoIndex);
	private sealed record SourcePoolKey(string Category, UnitMeshPartKind PartKind, UnitMeshBodyVariant BodyVariant);
	private sealed class SourceHitPool(SourcePoolKey key, EquipmentUnitPart representative, int remainingHits)
	{
		public SourcePoolKey Key { get; } = key;
		public EquipmentUnitPart Representative { get; } = representative;
		public int RemainingHits { get; set; } = remainingHits;
	}

	private static async ValueTask<IReadOnlyDictionary<AssetKey, IReadOnlyList<string>>> ReadSharedArchiveIdsAsync(SqliteConnection connection, IReadOnlyList<AssetKey> unitKeys, CancellationToken cancellationToken)
	{
		if (unitKeys.Count == 0) return new Dictionary<AssetKey, IReadOnlyList<string>>();
		await using var command = connection.CreateCommand();
		command.CommandText = @"
SELECT p.unit_type_id,p.unit_file_id,p.archive_id
FROM game_data_unit_parts p
JOIN archives a ON a.archive_id=p.archive_id
WHERE lower(a.category) IN ('armor','helmet')
  AND p.unit_type_id=$unitType
  AND p.unit_file_id IN (SELECT value FROM json_each($unitIds))
GROUP BY p.unit_type_id,p.unit_file_id,p.archive_id
ORDER BY p.unit_file_id,p.archive_id;";
		command.Parameters.AddWithValue("$unitType", unchecked((long)AdaptationPatchUnitMeshReader.UnitTypeId));
		command.Parameters.AddWithValue("$unitIds", JsonSerializer.Serialize(unitKeys.Select(key => unchecked((long)key.FileId))));
		var result = new Dictionary<AssetKey, List<string>>();
		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var key = new AssetKey(unchecked((ulong)reader.GetInt64(0)), unchecked((ulong)reader.GetInt64(1)));
			if (!result.TryGetValue(key, out var archives)) result[key] = archives = new List<string>();
			archives.Add(reader.GetString(2));
		}
		return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);
	}
}

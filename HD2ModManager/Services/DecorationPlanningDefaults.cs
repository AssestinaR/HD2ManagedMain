using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModManager.Services;

// Purpose: Keeps single-source and batch-decoration defaults consistent without hiding manual controls.
public static class DecorationPlanningDefaults
{
    public static string SelectPreferredArchiveId(IEnumerable<EquipmentUnitCatalogEntry> entries)
        => entries
            .GroupBy(entry => entry.ArchiveId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ArchiveId = group.Key,
                UnitCount = group.SelectMany(entry => entry.Parts).Select(part => part.UnitAssetKey).Distinct().Count(),
                StoredBytes = group.SelectMany(entry => entry.Parts).GroupBy(part => part.UnitAssetKey).Sum(parts => parts.Max(part => part.StoredBytes))
            })
            .OrderByDescending(group => group.UnitCount)
            .ThenByDescending(group => group.StoredBytes)
            .ThenBy(group => group.ArchiveId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ArchiveId)
            .FirstOrDefault() ?? string.Empty;

    public static string ResolveTargetPart(IEnumerable<EquipmentUnitPart> parts)
    {
        var sourceParts = parts.Select(part => part.PartKind).ToHashSet();
        if (sourceParts.Any(IsTorsoMapped)) return "Torso";
        if (sourceParts.Any(IsHipsMapped)) return "Hips";
        return sourceParts.Contains(UnitMeshPartKind.Head) ? "Head" : "Torso";
    }

    public static string ToPartLayerKey(UnitMeshPartKind part, UnitMeshPartLayer layer)
        => $"{part}/{layer}";

    public static bool MatchesPartLayer(IEnumerable<string>? keys, UnitMeshPartKind part, UnitMeshPartLayer layer)
        => keys?.Contains(ToPartLayerKey(part, layer), StringComparer.OrdinalIgnoreCase) == true;

    private static bool IsTorsoMapped(UnitMeshPartKind kind)
        => kind is UnitMeshPartKind.Torso or UnitMeshPartKind.LeftArm or UnitMeshPartKind.RightArm or UnitMeshPartKind.LeftShoulder or UnitMeshPartKind.RightShoulder;

    private static bool IsHipsMapped(UnitMeshPartKind kind)
        => kind is UnitMeshPartKind.Pelvis or UnitMeshPartKind.LeftLeg or UnitMeshPartKind.RightLeg;
}

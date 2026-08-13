using System;
using System.Collections.Generic;

namespace HD2ModManager.Models;

// Purpose: Stable, editable contract for a decoration package before the mesh append engine is connected.
public sealed class DecorationPlanDocument
{
    public int Version { get; set; } = 1;
    public string SourceModId { get; set; } = string.Empty;
    public List<DecorationSourceUnit> SourceUnits { get; set; } = new();
    public List<string> TargetModIds { get; set; } = new();
    public string TargetPart { get; set; } = "LeftArm";
    public string TargetBodyVariant { get; set; } = "Dual";
    public string DualVariantMode { get; set; } = "Auto";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DecorationSourceUnit
{
    public ulong TypeId { get; set; }
    public ulong FileId { get; set; }
    public bool IncludeCulling { get; set; }
}

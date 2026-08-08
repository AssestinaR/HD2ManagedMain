namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

public sealed record CanonicalMeshAssemblyTelemetry(
	TimeSpan Route,
	TimeSpan Merge,
	TimeSpan Minify,
	TimeSpan MaterialResolution)
{
	public static CanonicalMeshAssemblyTelemetry Empty { get; } = new(
		TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

	public CanonicalMeshAssemblyTelemetry Add(CanonicalMeshAssemblyTelemetry other) => new(
		Route + other.Route,
		Merge + other.Merge,
		Minify + other.Minify,
		MaterialResolution + other.MaterialResolution);
}

public sealed record CanonicalUnitSerializationTelemetry(
	TimeSpan Setup,
	TimeSpan GpuWrite,
	TimeSpan TocRebuild,
	TimeSpan ModelFinalize)
{
	public static CanonicalUnitSerializationTelemetry Empty { get; } = new(
		TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

	public CanonicalUnitSerializationTelemetry Add(CanonicalUnitSerializationTelemetry other) => new(
		Setup + other.Setup,
		GpuWrite + other.GpuWrite,
		TocRebuild + other.TocRebuild,
		ModelFinalize + other.ModelFinalize);
}

// Purpose: Keeps Canonical Unit rebuild timing comparable across tool workflows.
public sealed record CanonicalUnitRebuildTelemetry(
	TimeSpan TransformExpansion,
	TimeSpan MeshAssembly,
	TimeSpan StreamContract,
	TimeSpan FirstPreparation,
	TimeSpan BonePalette,
	TimeSpan FinalPreparation,
	TimeSpan MaterialBindings,
	TimeSpan Serialization)
{
	public CanonicalMeshAssemblyTelemetry MeshBreakdown { get; init; } = CanonicalMeshAssemblyTelemetry.Empty;
	public CanonicalUnitSerializationTelemetry SerializationBreakdown { get; init; } = CanonicalUnitSerializationTelemetry.Empty;

	public static CanonicalUnitRebuildTelemetry Empty { get; } = new(
		TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
		TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

	public string Describe()
		=> $"Transform={TransformExpansion.TotalMilliseconds:F0}ms, Mesh={MeshAssembly.TotalMilliseconds:F0}ms (Route={MeshBreakdown.Route.TotalMilliseconds:F0}ms, Merge={MeshBreakdown.Merge.TotalMilliseconds:F0}ms, Minify={MeshBreakdown.Minify.TotalMilliseconds:F0}ms, MaterialResolve={MeshBreakdown.MaterialResolution.TotalMilliseconds:F0}ms), Stream={StreamContract.TotalMilliseconds:F0}ms, Prepare1={FirstPreparation.TotalMilliseconds:F0}ms, Palette={BonePalette.TotalMilliseconds:F0}ms, PrepareFinal={FinalPreparation.TotalMilliseconds:F0}ms, Materials={MaterialBindings.TotalMilliseconds:F0}ms, Serialize={Serialization.TotalMilliseconds:F0}ms (Setup={SerializationBreakdown.Setup.TotalMilliseconds:F0}ms, GPU={SerializationBreakdown.GpuWrite.TotalMilliseconds:F0}ms, TOC={SerializationBreakdown.TocRebuild.TotalMilliseconds:F0}ms, Finalize={SerializationBreakdown.ModelFinalize.TotalMilliseconds:F0}ms)";
}

public sealed class CanonicalUnitRebuildTelemetryAccumulator
{
	private CanonicalUnitRebuildTelemetry total = CanonicalUnitRebuildTelemetry.Empty;

	public void Add(CanonicalUnitRebuildTelemetry? telemetry)
	{
		if (telemetry is null) return;
		total = new(
			total.TransformExpansion + telemetry.TransformExpansion,
			total.MeshAssembly + telemetry.MeshAssembly,
			total.StreamContract + telemetry.StreamContract,
			total.FirstPreparation + telemetry.FirstPreparation,
			total.BonePalette + telemetry.BonePalette,
			total.FinalPreparation + telemetry.FinalPreparation,
			total.MaterialBindings + telemetry.MaterialBindings,
			total.Serialization + telemetry.Serialization)
		{
			MeshBreakdown = total.MeshBreakdown.Add(telemetry.MeshBreakdown),
			SerializationBreakdown = total.SerializationBreakdown.Add(telemetry.SerializationBreakdown)
		};
	}

	public CanonicalUnitRebuildTelemetry Snapshot() => total;
}

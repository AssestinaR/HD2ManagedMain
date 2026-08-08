namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

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
	public static CanonicalUnitRebuildTelemetry Empty { get; } = new(
		TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
		TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

	public string Describe()
		=> $"Transform={TransformExpansion.TotalMilliseconds:F0}ms, Mesh={MeshAssembly.TotalMilliseconds:F0}ms, Stream={StreamContract.TotalMilliseconds:F0}ms, Prepare1={FirstPreparation.TotalMilliseconds:F0}ms, Palette={BonePalette.TotalMilliseconds:F0}ms, PrepareFinal={FinalPreparation.TotalMilliseconds:F0}ms, Materials={MaterialBindings.TotalMilliseconds:F0}ms, Serialize={Serialization.TotalMilliseconds:F0}ms";
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
			total.Serialization + telemetry.Serialization);
	}

	public CanonicalUnitRebuildTelemetry Snapshot() => total;
}

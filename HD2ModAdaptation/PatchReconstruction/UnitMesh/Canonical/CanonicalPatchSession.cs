namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Defines the validated ownership and dependency boundary for an independent canonical patch session.
// Purpose: Defines ownership-safe entries for an independent canonical output patch session.
// SDK reference entry points: CreatePatchFromActive(), AddEntryToPatchID(), TocEntry.Serialize(), and StreamToc.Serialize().
public enum CanonicalPatchEntryOwnership
{
	TargetOutput = 0,
	RequiredDependency = 1,
	SourceRetained = 2
}

public enum CanonicalDependencyClosureValidation
{
	Invalid = 0,
	Valid = 1
}

public sealed record CanonicalPatchSessionEntry(
	AssetKey Key,
	CanonicalPatchEntryOwnership Ownership,
	byte[]? TocData = null,
	byte[]? GpuData = null,
	byte[]? StreamData = null,
	ulong Unknown1 = 0,
	ulong Unknown2 = 0,
	uint Unknown3 = 16,
	uint Unknown4 = 64)
{
	public string? TocDataPath { get; init; }
	public string? GpuDataPath { get; init; }
	public string? StreamDataPath { get; init; }
	public byte[] EffectiveTocData => TocData ?? ReadPayload(TocDataPath, "TocData");
	public byte[] EffectiveGpuData => GpuData ?? ReadPayload(GpuDataPath, "GpuData");
	public byte[] EffectiveStreamData => StreamData ?? ReadPayload(StreamDataPath, "StreamData");

	private byte[] ReadPayload(string? path, string name)
		=> path is null
			? throw new InvalidOperationException($"Canonical entry {Key} has no {name} payload.")
			: File.ReadAllBytes(path);
}

public sealed record CanonicalPatchSessionValidation(
	bool IsValid,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics,
	CanonicalDependencyClosureValidation DependencyClosureValidation);

public sealed class CanonicalPatchSession
{
	private readonly List<CanonicalPatchSessionEntry> entries = [];
	private bool isFinalized;
	private CanonicalDependencyClosureValidation dependencyClosureValidation = CanonicalDependencyClosureValidation.Invalid;

	public IReadOnlyList<CanonicalPatchSessionEntry> Entries => entries;
	public bool IsFinalized => isFinalized;
	public CanonicalDependencyClosureValidation DependencyClosureValidation => dependencyClosureValidation;

	public void AddEntry(CanonicalPatchSessionEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		if (isFinalized)
		{
			throw new InvalidOperationException("Canonical patch sessions cannot accept entries after Finalize().");
		}
		if (entry.Key == default)
		{
			throw new ArgumentException("Patch session entries require an explicit target/dependency key.", nameof(entry));
		}

		if (entry.Ownership == CanonicalPatchEntryOwnership.SourceRetained)
		{
			throw new InvalidOperationException("Canonical patch sessions cannot retain source entries.");
		}

		if (entries.Any(existing => existing.Key == entry.Key))
		{
			throw new InvalidOperationException($"Patch session entry {entry.Key} is already present.");
		}

		entries.Add(entry);
	}

	public CanonicalPatchSessionValidation Finalize(CanonicalDependencyClosureValidation dependencyClosureValidation)
	{
		isFinalized = true;
		this.dependencyClosureValidation = dependencyClosureValidation;
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		if (dependencyClosureValidation != CanonicalDependencyClosureValidation.Valid)
			diagnostics.Add(new("DependencyClosureNotValidated", "Canonical patch session finalization requires a valid dependency closure."));
		if (!entries.Any(entry => entry.Ownership == CanonicalPatchEntryOwnership.TargetOutput))
			diagnostics.Add(new("MissingTargetOutput", "Canonical patch session finalization requires at least one TargetOutput entry."));
		foreach (var entry in entries)
		{
			if ((entry.TocData is null && entry.TocDataPath is null)
				|| (entry.GpuData is null && entry.GpuDataPath is null)
				|| (entry.StreamData is null && entry.StreamDataPath is null))
				diagnostics.Add(new("MissingEntryPayload", $"Canonical entry {entry.Key} must own TocData, GpuData, and StreamData."));
		}
		return new(diagnostics.Count == 0, diagnostics, dependencyClosureValidation);
	}
}
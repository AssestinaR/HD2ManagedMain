using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Writes approved cross-armor target-shell work items while preserving Patch/sidecar and material-closure binary rules inside Adaptation.
namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

public sealed class CrossArmorTargetShellPatchOperation
{
	private const ulong CompositeUnitTypeId = 0xc4f0f4be7fb0c8d6;
	private readonly PatchTocScanner scanner;
	private readonly PatchEntryPayloadReader payloadReader;
	private readonly PatchArchiveWriter archiveWriter;
	private readonly ICrossArmorStagedWriter stagedWriter;
	private readonly ICrossArmorStagedVerifier stagedVerifier;
	private readonly ICrossArmorStagedCommitter stagedCommitter;

	public CrossArmorTargetShellPatchOperation(
		PatchTocScanner? scanner = null,
		PatchEntryPayloadReader? payloadReader = null,
		PatchArchiveWriter? archiveWriter = null,
		ICrossArmorStagedWriter? stagedWriter = null,
		ICrossArmorStagedVerifier? stagedVerifier = null,
		ICrossArmorStagedCommitter? stagedCommitter = null)
	{
		this.scanner = scanner ?? new PatchTocScanner();
		this.payloadReader = payloadReader ?? new PatchEntryPayloadReader();
		this.archiveWriter = archiveWriter ?? new PatchArchiveWriter(this.scanner, this.payloadReader);
		this.stagedWriter = stagedWriter ?? new DefaultCrossArmorStagedWriter(this.archiveWriter);
		this.stagedVerifier = stagedVerifier ?? new DefaultCrossArmorStagedVerifier(this.scanner);
		this.stagedCommitter = stagedCommitter ?? new DefaultCrossArmorStagedCommitter();
	}

	public async ValueTask<CrossArmorTargetShellPatchOperationResult> ExecuteAsync(
		CrossArmorTargetShellPatchOperationRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		request.Validate();
		var entries = request.PreparedSourceEntries is { Count: > 0 }
			? request.PreparedSourceEntries
			: await scanner.ScanEntriesAsync(request.SourcePatchTocPath, cancellationToken).ConfigureAwait(false);
		var output = BuildOutput(request.WorkItems, request.AllowedSourceMaterialIds, request.CanonicalBoneHashOrder, request.StreamLayoutRegistry, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		return await ExecuteOutputAsync(request, output, cancellationToken).ConfigureAwait(false);
	}

	// Purpose: Rebuilds a bounded batch of target shells without retaining unrelated target Unit models.
	public SdkStyleTargetShellPatchOutput BuildOutput(
		IReadOnlyCollection<SdkStyleTargetShellPatchWorkItem> workItems,
		IReadOnlySet<ulong>? allowedSourceMaterialIds,
		IReadOnlyList<uint>? canonicalBoneHashOrder,
		ICurrentGameStreamLayoutRegistry? streamLayoutRegistry,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var outputBuilder = new SdkStyleTargetShellPatchOutputBuilder(
			new SdkStyleTargetShellUnitReconstructor(
				reencoder: new SdkStyleMeshReencoder(allowSectionRebuild: true, rebuildTargetInverseJointMatrices: true, preserveCompleteSourcePalette: true),
				writer: new UnitMeshWriter(allowBoneInfoRelocation: true, allowTransformInfoRelocation: true),
				propagateSourceMaterials: true,
				allowedSourceMaterialIds: allowedSourceMaterialIds,
				planCanonicalSkinningLayout: true,
				streamLayoutRegistry: streamLayoutRegistry));
		return outputBuilder.Build(workItems, cancellationToken);
	}

	// Purpose: Serially commits previously rebuilt batches as one Patch archive.
	public async ValueTask<CrossArmorTargetShellPatchOperationResult> ExecuteOutputAsync(
		CrossArmorTargetShellPatchOperationRequest request,
		SdkStyleTargetShellPatchOutput output,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(output);
		request.Validate(requireWorkItems: false);
		if (cancellationToken.IsCancellationRequested)
		{
			await MarkCanceledDirectoryAsync(request.OutputDirectory).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
		}
		var ownership = CrossArmorOutputOwnership.Create(request.OutputDirectory, request.SourcePatchTocPath);
		IReadOnlyList<PatchTocEntry> entries;
		IReadOnlyList<PatchTocEntry> removals;
		try
		{
			entries = request.PreparedSourceEntries is { Count: > 0 }
				? request.PreparedSourceEntries
				: await scanner.ScanEntriesAsync(request.SourcePatchTocPath, cancellationToken).ConfigureAwait(false);
			removals = await GetSourceUnitAndCompositeRemovalsAsync(entries, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			await MarkCanceledOutputAsync(ownership).ConfigureAwait(false);
			throw;
		}
		var preservedSourceKeys = entries.Where(entry => !removals.Contains(entry)).Select(entry => entry.AssetKey).ToHashSet();
		var additionalEntries = output.AdditionalEntries
			.Concat(request.IncludeResolvedMaterialDependencies ? request.MaterialDependencies : Array.Empty<PatchArchiveAdditionalEntry>())
			.Where(entry => !preservedSourceKeys.Contains(entry.AssetKey))
			.GroupBy(entry => entry.AssetKey)
			.Select(group => group.First())
			.ToArray();
		PatchArchiveFileWriteResult write;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			write = await stagedWriter.WriteAsync(
				request.SourcePatchTocPath,
				ownership.StagingDirectory,
				Array.Empty<PatchUnitMeshEditResult>(),
				additionalEntries,
				removals,
				preserveOriginalStream: true,
				headerTemplateTocData: request.HeaderTemplateTocData,
				overwriteExisting: request.OverwriteExisting,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			await stagedVerifier.VerifyAsync(write.TocFilePath, output.UnitResults.Select(result => result.TargetUnitAssetKey).ToHashSet(), cancellationToken).ConfigureAwait(false);
			if (request.PreCommitValidation is not null)
				await request.PreCommitValidation(write.TocFilePath, cancellationToken).ConfigureAwait(false);
			write = stagedCommitter.Commit(ownership, write);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			await MarkCanceledOutputAsync(ownership).ConfigureAwait(false);
			throw;
		}
		catch (Exception)
		{
			ownership.Cleanup();
			throw;
		}
		return new CrossArmorTargetShellPatchOperationResult(write, output, entries, removals, ownership);
	}

	public void CleanupIncompleteOutput(CrossArmorTargetShellPatchOperationResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		result.Ownership.Cleanup();
	}

	private static async Task MarkCanceledOutputAsync(CrossArmorOutputOwnership ownership)
	{
		ownership.Cleanup();
		await MarkCanceledDirectoryAsync(ownership.OutputDirectory).ConfigureAwait(false);
	}

	private static async Task MarkCanceledDirectoryAsync(string outputDirectory)
	{
		Directory.CreateDirectory(outputDirectory);
		var markerPath = Path.Combine(outputDirectory, "cross-armor-output.canceled");
		if (!File.Exists(markerPath)) await File.WriteAllTextAsync(markerPath, "Canceled before a complete Patch output was committed." + Environment.NewLine).ConfigureAwait(false);
	}

	private async ValueTask<IReadOnlyList<PatchTocEntry>> GetSourceUnitAndCompositeRemovalsAsync(IReadOnlyList<PatchTocEntry> entries, CancellationToken cancellationToken)
	{
		var units = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).ToArray();
		var compositeIds = new HashSet<ulong>();
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

	private async ValueTask VerifyAsync(string tocPath, IReadOnlySet<AssetKey> expectedUnits, CancellationToken cancellationToken)
	{
		var entries = await scanner.ScanEntriesAsync(tocPath, cancellationToken).ConfigureAwait(false);
		var actualUnits = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).Select(entry => entry.AssetKey).ToHashSet();
		if (!actualUnits.SetEquals(expectedUnits)) throw new InvalidDataException("输出 Unit 集合与批准的物理目标集合不一致。");
		if (entries.GroupBy(entry => entry.AssetKey).Any(group => group.Count() != 1)) throw new InvalidDataException("输出包含重复 AssetKey。");
	}
}

// Purpose: Keeps the staged write/validation/commit boundaries injectable for file-level CrossArmor tests.
public interface ICrossArmorStagedWriter
{
	ValueTask<PatchArchiveFileWriteResult> WriteAsync(string sourcePatchTocPath, string stagingDirectory, IReadOnlyCollection<PatchUnitMeshEditResult> edits, IReadOnlyCollection<PatchArchiveAdditionalEntry> additionalEntries, IReadOnlyCollection<PatchTocEntry> removals, bool preserveOriginalStream, byte[] headerTemplateTocData, bool overwriteExisting, CancellationToken cancellationToken);
}

public interface ICrossArmorStagedVerifier
{
	ValueTask VerifyAsync(string tocPath, IReadOnlySet<AssetKey> expectedUnits, CancellationToken cancellationToken);
}

public interface ICrossArmorStagedCommitter
{
	PatchArchiveFileWriteResult Commit(CrossArmorOutputOwnership ownership, PatchArchiveFileWriteResult staged);
}

internal sealed class DefaultCrossArmorStagedWriter(PatchArchiveWriter writer) : ICrossArmorStagedWriter
{
	public ValueTask<PatchArchiveFileWriteResult> WriteAsync(string sourcePatchTocPath, string stagingDirectory, IReadOnlyCollection<PatchUnitMeshEditResult> edits, IReadOnlyCollection<PatchArchiveAdditionalEntry> additionalEntries, IReadOnlyCollection<PatchTocEntry> removals, bool preserveOriginalStream, byte[] headerTemplateTocData, bool overwriteExisting, CancellationToken cancellationToken)
		=> writer.WriteAsync(sourcePatchTocPath, stagingDirectory, edits, additionalEntries, removals, overwriteExisting, preserveOriginalStream, headerTemplateTocData, cancellationToken);
}

internal sealed class DefaultCrossArmorStagedVerifier(PatchTocScanner scanner) : ICrossArmorStagedVerifier
{
	public async ValueTask VerifyAsync(string tocPath, IReadOnlySet<AssetKey> expectedUnits, CancellationToken cancellationToken)
	{
		var entries = await scanner.ScanEntriesAsync(tocPath, cancellationToken).ConfigureAwait(false);
		var actualUnits = entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId).Select(entry => entry.AssetKey).ToHashSet();
		if (!actualUnits.SetEquals(expectedUnits)) throw new InvalidDataException("输出 Unit 集合与批准的物理目标集合不一致。");
		if (entries.GroupBy(entry => entry.AssetKey).Any(group => group.Count() != 1)) throw new InvalidDataException("输出包含重复 AssetKey。");
	}
}

internal sealed class DefaultCrossArmorStagedCommitter : ICrossArmorStagedCommitter
{
	public PatchArchiveFileWriteResult Commit(CrossArmorOutputOwnership ownership, PatchArchiveFileWriteResult staged) => ownership.Commit(staged);
}

public sealed record CrossArmorTargetShellPatchOperationRequest(
	string SourcePatchTocPath,
	string OutputDirectory,
	byte[] HeaderTemplateTocData,
	IReadOnlyList<SdkStyleTargetShellPatchWorkItem> WorkItems,
	IReadOnlyCollection<PatchArchiveAdditionalEntry> MaterialDependencies,
	bool IncludeResolvedMaterialDependencies,
	IReadOnlySet<ulong>? AllowedSourceMaterialIds,
	IReadOnlyList<PatchTocEntry>? PreparedSourceEntries = null,
	IReadOnlyList<uint>? CanonicalBoneHashOrder = null,
	ICurrentGameStreamLayoutRegistry? StreamLayoutRegistry = null)
{
	public bool OverwriteExisting { get; init; }
	// All output-level validation must complete against staging before formal publication.
	public Func<string, CancellationToken, ValueTask>? PreCommitValidation { get; init; }
	public void Validate(bool requireWorkItems = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(SourcePatchTocPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(OutputDirectory);
		ArgumentNullException.ThrowIfNull(HeaderTemplateTocData);
		ArgumentNullException.ThrowIfNull(WorkItems);
		ArgumentNullException.ThrowIfNull(MaterialDependencies);
		if (requireWorkItems && WorkItems.Count == 0) throw new InvalidDataException("At least one approved target Unit work item is required.");
		if (OverwriteExisting) throw new CrossArmorOverwriteNotAllowedException();
	}
}

// Purpose: Prevents cross-armor candidate generation from destroying pre-existing Patch output.
public sealed class CrossArmorOverwriteNotAllowedException : InvalidOperationException
{
	public CrossArmorOverwriteNotAllowedException()
		: base("Cross-armor candidate output must not overwrite an existing Patch.")
	{
	}
}

public sealed record CrossArmorTargetShellPatchOperationResult(
	PatchArchiveFileWriteResult WriteResult,
	SdkStyleTargetShellPatchOutput Output,
	IReadOnlyList<PatchTocEntry> SourceEntries,
	IReadOnlyList<PatchTocEntry> RemovedEntries,
	CrossArmorOutputOwnership Ownership);

public sealed class CrossArmorOutputOwnership
{
	private readonly string[] formalPaths;
	private readonly HashSet<string> publishedPaths = new(StringComparer.OrdinalIgnoreCase);
	public string OutputDirectory { get; }
	public string StagingDirectory { get; }
	public bool IsCommitted { get; private set; }

	private CrossArmorOutputOwnership(string outputDirectory, string stagingDirectory, string[] formalPaths)
	{
		OutputDirectory = outputDirectory;
		StagingDirectory = stagingDirectory;
		this.formalPaths = formalPaths;
	}

	public static CrossArmorOutputOwnership Create(string outputDirectory, string sourcePatchTocPath)
	{
		var directory = Path.GetFullPath(outputDirectory);
		var tocPath = Path.Combine(directory, Path.GetFileName(sourcePatchTocPath));
		var paths = new[] { tocPath, tocPath + ".stream", tocPath + ".gpu_resources" };
		if (paths.Any(File.Exists)) throw new CrossArmorOutputAlreadyExistsException(tocPath);
		var staging = Path.Combine(directory, ".cross-armor-staging", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(staging);
		return new CrossArmorOutputOwnership(directory, staging, paths);
	}

	public PatchArchiveFileWriteResult Commit(PatchArchiveFileWriteResult staged)
	{
		// Commit is deliberately non-cancellable: sidecars precede the TOC, and rollback
		// only removes paths this operation successfully published itself.
		try
		{
			foreach (var path in formalPaths.Skip(1)) Publish(Path.Combine(StagingDirectory, Path.GetFileName(path)), path);
			Publish(staged.TocFilePath, formalPaths[0]);
			IsCommitted = true;
			CleanupDirectoryBestEffort(StagingDirectory);
			return staged with { OutputDirectoryPath = OutputDirectory, TocFilePath = formalPaths[0], StreamFilePath = formalPaths[1], GpuResourceFilePath = formalPaths[2] };
		}
		catch
		{
			CleanupPublishedBestEffort();
			throw;
		}
	}

	private void Publish(string source, string destination)
	{
		if (!File.Exists(source)) return;
		if (File.Exists(destination)) throw new IOException($"Output file already exists: {destination}");
		File.Move(source, destination);
		publishedPaths.Add(destination);
	}

	public void Cleanup()
	{
		if (!IsCommitted) CleanupDirectoryBestEffort(StagingDirectory);
	}

	private void CleanupPublishedBestEffort()
	{
		foreach (var path in publishedPaths.ToArray()) TryDelete(path);
	}

	private static void CleanupDirectoryBestEffort(string path)
	{
		if (!Directory.Exists(path)) return;
		foreach (var file in Directory.EnumerateFiles(path)) TryDelete(file);
		try { Directory.Delete(path, recursive: true); } catch (Exception) { }
	}

	private static void TryDelete(string path)
	{
		try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
	}
}

public sealed class CrossArmorOutputAlreadyExistsException : IOException
{
	public CrossArmorOutputAlreadyExistsException(string path) : base($"Cross-armor candidate output already exists: {path}") { }
}

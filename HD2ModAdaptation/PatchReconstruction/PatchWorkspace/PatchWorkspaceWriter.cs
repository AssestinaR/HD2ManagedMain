using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

namespace HD2ModAdaptation.PatchReconstruction.PatchWorkspace;

// Purpose: Provides the shared Patch packaging boundary for payload-owned reconstruction sessions.
public interface IPatchWorkspaceWriter
{
	ValueTask<PatchArchiveFileWriteResult> WriteAsync(
		PatchWorkspaceIndex index,
		IEnumerable<PatchWorkspaceJobResult> jobs,
		IReadOnlySet<AssetKey> removedKeys,
		string outputDirectoryPath,
		string patchFileName,
		byte[]? headerTemplateTocData = null,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default);

	ValueTask<PatchArchiveFileWriteResult> WriteAsync(
		PatchWorkspace workspace,
		PatchWorkspaceChangeSet changes,
		string outputDirectoryPath,
		string patchFileName,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default);

	ValueTask<PatchArchiveFileWriteResult> WriteAsync(
		CanonicalPatchSession session,
		string outputDirectoryPath,
		string patchFileName,
		byte[]? headerTemplateTocData = null,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default);
}

public sealed class PatchWorkspaceWriter : IPatchWorkspaceWriter
{
	private readonly ICanonicalPatchWriter canonicalWriter;
	private readonly IPatchWorkspaceSessionComposer sessionComposer;

	public PatchWorkspaceWriter(ICanonicalPatchWriter? canonicalWriter = null, IPatchWorkspaceSessionComposer? sessionComposer = null)
	{
		this.canonicalWriter = canonicalWriter ?? new CanonicalPatchWriter();
		this.sessionComposer = sessionComposer ?? new PatchWorkspaceSessionComposer();
	}

	public async ValueTask<PatchArchiveFileWriteResult> WriteAsync(
		PatchWorkspaceIndex index,
		IEnumerable<PatchWorkspaceJobResult> jobs,
		IReadOnlySet<AssetKey> removedKeys,
		string outputDirectoryPath,
		string patchFileName,
		byte[]? headerTemplateTocData = null,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(index);
		ArgumentNullException.ThrowIfNull(jobs);
		ArgumentNullException.ThrowIfNull(removedKeys);
		var jobList = jobs.ToArray();
		var diagnostics = jobList.SelectMany(job => job.Diagnostics).ToArray();
		if (diagnostics.Length != 0)
			throw new InvalidDataException(string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message)));

		var outputByKey = jobList.SelectMany(job => job.Outputs).ToArray();
		var outputKeys = outputByKey.Select(entry => entry.Key).ToHashSet();
		var duplicateOutputs = outputByKey.GroupBy(entry => entry.Key).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
		if (duplicateOutputs.Length != 0)
			throw new InvalidDataException($"Patch jobs produced duplicate AssetKeys: {string.Join(", ", duplicateOutputs)}.");

		var sourceEntries = index.Entries
			.Where(entry => !removedKeys.Contains(entry.AssetKey) && !outputKeys.Contains(entry.AssetKey))
			.Select(entry => CreateSourceEntry(index.SourcePatchTocPath, entry))
			.ToArray();
		var session = new CanonicalPatchSession();
		var finalized = sessionComposer.ComposeJobs(session, jobList, sourceEntries, CanonicalDependencyClosureValidation.Valid);
		if (!finalized.IsValid)
			throw new InvalidDataException(string.Join(Environment.NewLine, finalized.Diagnostics.Select(diagnostic => diagnostic.Message)));
		return await WriteAsync(session, outputDirectoryPath, patchFileName, headerTemplateTocData ?? index.HeaderTemplateTocData, overwriteExisting, cancellationToken).ConfigureAwait(false);
	}

	public ValueTask<PatchArchiveFileWriteResult> WriteAsync(
		PatchWorkspace workspace,
		PatchWorkspaceChangeSet changes,
		string outputDirectoryPath,
		string patchFileName,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(changes);
		var session = workspace.ToCanonicalSession(changes.Changes);
		var finalized = sessionComposer.Compose(session, [], CanonicalDependencyClosureValidation.Valid);
		if (!finalized.IsValid) throw new InvalidDataException(string.Join(Environment.NewLine, finalized.Diagnostics.Select(diagnostic => diagnostic.Message)));
		return WriteAsync(session, outputDirectoryPath, patchFileName, workspace.HeaderTemplateTocData, overwriteExisting, cancellationToken);
	}

	public ValueTask<PatchArchiveFileWriteResult> WriteAsync(
		CanonicalPatchSession session,
		string outputDirectoryPath,
		string patchFileName,
		byte[]? headerTemplateTocData = null,
		bool overwriteExisting = false,
		CancellationToken cancellationToken = default)
		=> canonicalWriter.WriteAsync(session, outputDirectoryPath, patchFileName, headerTemplateTocData, overwriteExisting, cancellationToken);

	private static CanonicalPatchSessionEntry CreateSourceEntry(string tocPath, PatchTocEntry entry)
	{
		var gpuPath = tocPath + ".gpu_resources";
		var streamPath = tocPath + ".stream";
		EnsureRangeExists(tocPath, entry.TocDataOffset, entry.TocDataSize, entry.AssetKey);
		EnsureRangeExists(gpuPath, entry.GpuResourceOffset, entry.GpuResourceSize, entry.AssetKey);
		EnsureRangeExists(streamPath, entry.StreamOffset, entry.StreamSize, entry.AssetKey);
		return new CanonicalPatchSessionEntry(entry.AssetKey, CanonicalPatchEntryOwnership.RequiredDependency,
			entry.TocDataSize == 0 ? Array.Empty<byte>() : null,
			entry.GpuResourceSize == 0 ? Array.Empty<byte>() : null,
			entry.StreamSize == 0 ? Array.Empty<byte>() : null,
			entry.Unknown1, entry.Unknown2, entry.Unknown3, entry.Unknown4)
		{
			TocDataSource = entry.TocDataSize == 0 ? null : new CanonicalPayloadSourceRange(tocPath, entry.TocDataOffset, entry.TocDataSize),
			GpuDataSource = entry.GpuResourceSize == 0 ? null : new CanonicalPayloadSourceRange(gpuPath, entry.GpuResourceOffset, entry.GpuResourceSize),
			StreamDataSource = entry.StreamSize == 0 ? null : new CanonicalPayloadSourceRange(streamPath, entry.StreamOffset, entry.StreamSize)
		};
	}

	private static void EnsureRangeExists(string path, ulong offset, uint size, AssetKey key)
	{
		if (size == 0) return;
		if (!File.Exists(path)) throw new FileNotFoundException($"Patch payload sidecar is missing for {key}.", path);
		var length = new FileInfo(path).Length;
		if (offset > (ulong)length || offset + size > (ulong)length)
			throw new InvalidDataException($"Patch payload range is outside its source file for {key}.");
	}
}

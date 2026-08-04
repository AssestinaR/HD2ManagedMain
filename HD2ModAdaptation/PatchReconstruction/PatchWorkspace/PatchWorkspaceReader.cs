namespace HD2ModAdaptation.PatchReconstruction.PatchWorkspace;

// Purpose: Extracts one Patch TOC and its sidecar payloads into a self-contained format workspace.
public interface IPatchWorkspaceReader
{
	ValueTask<IReadOnlyList<PatchTocEntry>> ReadEntriesAsync(string sourcePatchTocPath, CancellationToken cancellationToken = default);
	ValueTask<PatchWorkspaceIndex> ReadIndexAsync(string sourcePatchTocPath, CancellationToken cancellationToken = default);
	ValueTask<PatchWorkspace> ReadAsync(string sourcePatchTocPath, CancellationToken cancellationToken = default);
}

public sealed class PatchWorkspaceReader : IPatchWorkspaceReader
{
	private readonly PatchTocScanner scanner;
	private readonly PatchEntryPayloadReader payloadReader;

	public PatchWorkspaceReader(PatchTocScanner? scanner = null, PatchEntryPayloadReader? payloadReader = null)
	{
		this.scanner = scanner ?? new PatchTocScanner();
		this.payloadReader = payloadReader ?? new PatchEntryPayloadReader();
	}

	public ValueTask<IReadOnlyList<PatchTocEntry>> ReadEntriesAsync(string sourcePatchTocPath, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePatchTocPath);
		return scanner.ScanEntriesAsync(Path.GetFullPath(sourcePatchTocPath), cancellationToken);
	}

	public async ValueTask<PatchWorkspaceIndex> ReadIndexAsync(string sourcePatchTocPath, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePatchTocPath);
		var source = Path.GetFullPath(sourcePatchTocPath);
		var entries = await ReadEntriesAsync(source, cancellationToken).ConfigureAwait(false);
		return new PatchWorkspaceIndex(source, entries, await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false));
	}

	public async ValueTask<PatchWorkspace> ReadAsync(string sourcePatchTocPath, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePatchTocPath);
		var source = Path.GetFullPath(sourcePatchTocPath);
		if (!File.Exists(source)) throw new FileNotFoundException("Patch TOC does not exist.", source);
		var index = await ReadIndexAsync(source, cancellationToken).ConfigureAwait(false);
		var entries = index.Entries;
		var payloads = new List<PatchWorkspaceEntry>(entries.Count);
		foreach (var entry in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var payload = await payloadReader.ReadPayloadAsync(entry, cancellationToken).ConfigureAwait(false);
			payloads.Add(new PatchWorkspaceEntry(entry, payload.TocData, payload.StreamData, payload.GpuResourceData));
		}
		return new PatchWorkspace(source, payloads, index.HeaderTemplateTocData);
	}
}
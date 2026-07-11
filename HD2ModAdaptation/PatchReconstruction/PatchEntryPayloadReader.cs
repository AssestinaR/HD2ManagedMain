namespace HD2ModAdaptation.PatchReconstruction;

// Purpose: Reads TOC, stream, and GPU payload ranges referenced by patch entries.
public sealed class PatchEntryPayloadReader : IPatchEntryPayloadReader
{
	private const int SidecarAlignment = 64;

	public async ValueTask<PatchEntryPayload> ReadPayloadAsync(PatchTocEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return new PatchEntryPayload(entry,
			await ReadRangeAsync(entry.SourceFilePath, entry.TocDataOffset, entry.TocDataSize, false, cancellationToken).ConfigureAwait(false),
			await ReadRangeAsync(entry.SourceFilePath + ".stream", entry.StreamOffset, entry.StreamSize, true, cancellationToken).ConfigureAwait(false),
			await ReadRangeAsync(entry.SourceFilePath + ".gpu_resources", entry.GpuResourceOffset, entry.GpuResourceSize, true, cancellationToken).ConfigureAwait(false));
	}

	private static async ValueTask<byte[]> ReadRangeAsync(string path, ulong offset, uint size, bool sidecar, CancellationToken cancellationToken)
	{
		if (size == 0) return Array.Empty<byte>();
		if (!File.Exists(path)) throw new FileNotFoundException($"Required patch payload file was not found: {path}", path);
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
		var readableLength = sidecar ? AlignUp((ulong)stream.Length, SidecarAlignment) : (ulong)stream.Length;
		if (offset > readableLength || offset + size > readableLength) throw new InvalidDataException($"Patch payload range is outside '{path}'.");
		var result = new byte[size];
		if (offset >= (ulong)stream.Length) return result;
		stream.Position = checked((long)offset);
		await stream.ReadExactlyAsync(result.AsMemory(0, (int)Math.Min(size, (ulong)stream.Length - offset)), cancellationToken).ConfigureAwait(false);
		return result;
	}

	private static ulong AlignUp(ulong value, int alignment) => (value + (ulong)alignment - 1) & ~((ulong)alignment - 1);
}
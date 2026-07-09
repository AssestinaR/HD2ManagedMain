using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：根据 PatchTocEntry 的 offset/size 安全提取 patch TOC、stream 与 gpu_resources payload。
// Purpose: Safely extracts patch TOC, stream, and gpu_resources payloads using PatchTocEntry offset/size metadata.
public sealed class PatchEntryPayloadReader : IPatchEntryPayloadReader
{
	private const int SidecarAlignment = 64;

	public async ValueTask<PatchEntryPayload> ReadPayloadAsync(PatchTocEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);

		var tocData = await ReadRangeAsync(entry.SourceFilePath, entry.TocDataOffset, entry.TocDataSize, isSidecar: false, cancellationToken).ConfigureAwait(false);
		var streamData = await ReadRangeAsync(entry.SourceFilePath + ".stream", entry.StreamOffset, entry.StreamSize, isSidecar: true, cancellationToken).ConfigureAwait(false);
		var gpuResourceData = await ReadRangeAsync(entry.SourceFilePath + ".gpu_resources", entry.GpuResourceOffset, entry.GpuResourceSize, isSidecar: true, cancellationToken).ConfigureAwait(false);

		return new PatchEntryPayload(entry, tocData, streamData, gpuResourceData);
	}

	private static async ValueTask<byte[]> ReadRangeAsync(string path, ulong offset, uint size, bool isSidecar, CancellationToken cancellationToken)
	{
		if (size == 0)
		{
			return Array.Empty<byte>();
		}

		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Required patch payload file was not found: {path}", path);
		}

		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
		var readableLength = isSidecar ? AlignUp((ulong)stream.Length, SidecarAlignment) : (ulong)stream.Length;
		if (offset > readableLength || offset + size > readableLength)
		{
			throw new InvalidDataException($"Patch payload range offset {offset} size {size} is outside '{path}' length {stream.Length}.");
		}

		if (offset >= (ulong)stream.Length)
		{
			return new byte[size];
		}

		stream.Seek(checked((long)offset), SeekOrigin.Begin);
		var available = (int)Math.Min(size, (ulong)stream.Length - offset);
		var result = new byte[size];
		await stream.ReadExactlyAsync(result.AsMemory(0, available), cancellationToken).ConfigureAwait(false);
		return result;
	}

	private static ulong AlignUp(ulong value, int alignment)
	{
		var mask = checked((ulong)alignment - 1UL);
		return (value + mask) & ~mask;
	}
}

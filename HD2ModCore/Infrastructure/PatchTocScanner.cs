using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure.Binary;

namespace HD2ModCore.Infrastructure;

// 作用：快速扫描 HD2 的 .patch_n（TOC）并提取 (TypeID, FileID) 资产键，不解析资源体。
// Purpose: Fast scanner that reads an HD2 .patch_n TOC and extracts (TypeID, FileID) asset keys without decoding resource data.
public sealed class PatchTocScanner : IPatchTocScanner
{
	private const uint ExpectedMagic = 4026531857;
	private const int TocHeaderSizeToTypesEnd = 60;
	private const int TypeRecordSize = 32;
	private const int EntryRecordSize = 80;

	public async ValueTask<IReadOnlySet<AssetKey>> ScanAssetKeysAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(patchTocFilePath))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(patchTocFilePath));
		}

		await using var fs = new FileStream(
			patchTocFilePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			bufferSize: 64 * 1024,
			options: FileOptions.Asynchronous | FileOptions.SequentialScan);

		var header = new byte[12];
		await ReadExactAsync(fs, header, cancellationToken).ConfigureAwait(false);

		var magic = BinaryPrimitivesLE.ReadUInt32(header.AsSpan(0, 4));
		if (magic != ExpectedMagic)
		{
			throw new InvalidDataException($"Invalid TOC magic in '{patchTocFilePath}': {magic}");
		}

		var numTypes = BinaryPrimitivesLE.ReadUInt32(header.AsSpan(4, 4));
		var numFiles = BinaryPrimitivesLE.ReadUInt32(header.AsSpan(8, 4));

		var entriesOffset = TocHeaderSizeToTypesEnd + checked((int)numTypes * TypeRecordSize);
		var bytesToRead = checked(entriesOffset + checked((int)numFiles * EntryRecordSize));

		fs.Position = 0;
		var buffer = new byte[bytesToRead];
		await ReadExactAsync(fs, buffer, cancellationToken).ConfigureAwait(false);

		var result = new HashSet<AssetKey>((int)Math.Min(numFiles, 1024u));
		var offset = entriesOffset;
		for (var i = 0; i < numFiles; i++)
		{
			var fileId = BinaryPrimitivesLE.ReadUInt64(buffer.AsSpan(offset, 8));
			var typeId = BinaryPrimitivesLE.ReadUInt64(buffer.AsSpan(offset + 8, 8));
			result.Add(new AssetKey(typeId, fileId));
			offset += EntryRecordSize;
		}

		return result;
	}

	private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
	{
		var totalRead = 0;
		while (totalRead < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				throw new EndOfStreamException();
			}
			totalRead += read;
		}
	}
}

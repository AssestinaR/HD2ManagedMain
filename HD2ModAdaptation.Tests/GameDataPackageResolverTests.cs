using System.Buffers.Binary;
using System.Text;
using HD2ModAdaptation.PatchReconstruction;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies legacy and bundled DSAR package reads used by dependency fallback resolution.
public sealed class GameDataPackageResolverTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task GetPackageResourceAsync_ReadsLegacyArchiveSlice()
	{
		Directory.CreateDirectory(root);
		var path = Path.Combine(root, "legacy");
		await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4, 5 });

		var data = await new GameDataPackageResolver(root).GetPackageResourceAsync("legacy", 1, 3);

		Assert.Equal(new byte[] { 2, 3, 4 }, data);
	}

	[Fact]
	public async Task GetPackageResourceAsync_ReadsBundledPackageSlice()
	{
		Directory.CreateDirectory(root);
		const string packageName = "aaaaaaaaaaaaaaaa";
		var payload = Enumerable.Range(1, 128).Select(value => (byte)value).ToArray();
		await File.WriteAllBytesAsync(Path.Combine(root, "bundles.nxa"), BuildDsar(BuildBundleDatabase(packageName, payload.Length)));
		await File.WriteAllBytesAsync(Path.Combine(root, "bundles.00.nxa"), BuildDsar(payload));

		var data = await new GameDataPackageResolver(root).GetPackageResourceAsync(packageName, 80, 16);

		Assert.Equal(payload.AsSpan(80, 16).ToArray(), data);
	}

	public void Dispose()
	{
		if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
	}

	private static byte[] BuildBundleDatabase(string archiveId, int packageSize)
	{
		var data = new byte[0x90];
		Write32(data, 0x0c, 1); Write32(data, 0x10, 1); Write64(data, 0x18, (ulong)packageSize); Write32(data, 0x20, 0x40); Write32(data, 0x24, 1); Write32(data, 0x28, 0x60);
		Encoding.ASCII.GetBytes(archiveId).CopyTo(data, 0x40);
		Write64(data, 0x60, 0); Write32(data, 0x68, 0); data[0x6f] = 0;
		return data;
	}

	private static byte[] BuildDsar(byte[] payload)
	{
		const int dataOffset = 0x40;
		var data = new byte[dataOffset + payload.Length];
		Write32(data, 0, 0x52415344); Write32(data, 8, 1); Write64(data, 0x20, 0); Write64(data, 0x28, dataOffset); Write32(data, 0x30, (uint)payload.Length); Write32(data, 0x34, (uint)payload.Length); data[0x38] = 0; data[0x39] = 2;
		payload.CopyTo(data, dataOffset); return data;
	}

	private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void Write64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
}
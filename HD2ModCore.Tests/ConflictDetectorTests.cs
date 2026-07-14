using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证冲突检测能找出两个节点的资产键交集。
// Purpose: Verifies conflict detection finds shared AssetKeys between two nodes.
public sealed class ConflictDetectorTests
{
	[Fact]
	public async Task DetectNodeConflictsAsync_FindsIntersection()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var modsRoot = Path.Combine(root, "mods");
		Directory.CreateDirectory(modsRoot);

		try
		{
			// prepare node A
			var dirA = Path.Combine(modsRoot, "a");
			Directory.CreateDirectory(dirA);
			await File.WriteAllBytesAsync(Path.Combine(dirA, "9ba626afa44a3aa3.patch_0"), BuildToc(new[]
			{
				new AssetKey(1, 1),
				new AssetKey(2, 2),
			}));

			// prepare node B
			var dirB = Path.Combine(modsRoot, "b");
			Directory.CreateDirectory(dirB);
			await File.WriteAllBytesAsync(Path.Combine(dirB, "9ba626afa44a3aa3.patch_1"), BuildToc(new[]
			{
				new AssetKey(2, 2),
				new AssetKey(3, 3),
			}));

			var idA = ModNodeId.New();
			var idB = ModNodeId.New();
			var nodeA = new ModNode(idA, "a", new ModNodeMetadata("a", null, DateTimeOffset.UtcNow, null), Array.Empty<PatchGroupKey>(), Array.Empty<ModNodeId>());
			var nodeB = new ModNode(idB, "b", new ModNodeMetadata("b", null, DateTimeOffset.UtcNow, null), Array.Empty<PatchGroupKey>(), Array.Empty<ModNodeId>());
			var snapshot = new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode> { [idA] = nodeA, [idB] = nodeB }, Array.Empty<Profile>());

			var keyProvider = new AssetKeySetProvider(new PatchFileNameParser(), new PatchTocScanner());
			var detector = new ConflictDetector(keyProvider);

			var conflicts = await detector.DetectNodeConflictsAsync(new[] { idA, idB }, snapshot, modsRoot);
			Assert.Single(conflicts);
			Assert.Contains(new AssetKey(2, 2), conflicts[0].SharedKeys);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}

	private static byte[] BuildToc(AssetKey[] entries)
	{
		const uint magic = 4026531857;
		var numTypes = 0;
		var numFiles = entries.Length;
		var entriesOffset = 60 + numTypes * 32;
		var totalSize = entriesOffset + numFiles * 80;
		var buffer = new byte[totalSize];

		WriteUInt32(buffer, 0, magic);
		WriteUInt32(buffer, 4, (uint)numTypes);
		WriteUInt32(buffer, 8, (uint)numFiles);

		var offset = entriesOffset;
		foreach (var e in entries)
		{
			WriteUInt64(buffer, offset, e.FileId);
			WriteUInt64(buffer, offset + 8, e.TypeId);
			offset += 80;
		}

		return buffer;
	}

	private static void WriteUInt32(byte[] buffer, int offset, uint value)
	{
		buffer[offset + 0] = (byte)(value & 0xFF);
		buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
		buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
		buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
	}

	private static void WriteUInt64(byte[] buffer, int offset, ulong value)
	{
		buffer[offset + 0] = (byte)(value & 0xFF);
		buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
		buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
		buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
		buffer[offset + 4] = (byte)((value >> 32) & 0xFF);
		buffer[offset + 5] = (byte)((value >> 40) & 0xFF);
		buffer[offset + 6] = (byte)((value >> 48) & 0xFF);
		buffer[offset + 7] = (byte)((value >> 56) & 0xFF);
	}
}

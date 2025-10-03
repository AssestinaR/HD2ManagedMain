using System.Text.Json;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证替换目标推导器能按票数排序并拆分 TopN 与 Others。
// Purpose: Verifies replacement target deriver orders by votes and splits TopN vs Others.
public sealed class ReplacementTargetDeriverTests
{
	[Fact]
	public async Task DeriveAsync_ReturnsTopAndOthers()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var appRoot = Path.Combine(root, "app");
		var gameData = Path.Combine(root, "game", "data");
		Directory.CreateDirectory(appRoot);
		Directory.CreateDirectory(gameData);

		try
		{
			var a = "aaaaaaaaaaaaaaaa";
			var b = "bbbbbbbbbbbbbbbb";
			var c = "cccccccccccccccc";

			File.WriteAllBytes(Path.Combine(gameData, a), BuildToc(new[] { new AssetKey(1, 1), new AssetKey(1, 2), new AssetKey(1, 3) }));
			File.WriteAllBytes(Path.Combine(gameData, b), BuildToc(new[] { new AssetKey(1, 1) }));
			File.WriteAllBytes(Path.Combine(gameData, c), BuildToc(new[] { new AssetKey(1, 1), new AssetKey(1, 2) }));

			var archiveHashes = new Dictionary<string, Dictionary<string, string>>
			{
				["Armor"] = new Dictionary<string, string>
				{
					[a] = "A",
					[b] = "B",
					[c] = "C",
				}
			};
			var json = JsonSerializer.Serialize(archiveHashes);

			var paths = new StoragePaths(appRoot);
			var indexService = new AssetArchiveIndexService(paths, new PatchTocScanner());
			await indexService.BuildOrRebuildAsync(gameData, json);

			var deriver = new ReplacementTargetDeriver(paths, indexService);
           var filter = new IndexFilterSettings(IndexFilterMode.Percentage, PercentageThreshold: 1.0, AbsoluteThreshold: null);

			// assetKeys include: (1,1) votes a,b,c ; (1,2) votes a,c ; (1,3) votes a
			var result = await deriver.DeriveAsync(new HashSet<AssetKey>
			{
				new AssetKey(1, 1),
				new AssetKey(1, 2),
				new AssetKey(1, 3),
			}, filter, topN: 2);

			Assert.Equal(2, result.Top.Count);
			Assert.Single(result.Others);

			// A should be first with 3 votes
			Assert.Equal(a, result.Top[0].ArchiveId);
			Assert.Equal(3, result.Top[0].Votes);

			// C should be second with 2 votes
			Assert.Equal(c, result.Top[1].ArchiveId);
			Assert.Equal(2, result.Top[1].Votes);

			// B should be in others with 1 vote
			Assert.Equal(b, result.Others[0].ArchiveId);
			Assert.Equal(1, result.Others[0].Votes);
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

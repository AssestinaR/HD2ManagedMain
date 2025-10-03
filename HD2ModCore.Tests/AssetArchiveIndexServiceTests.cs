using System.Text.Json;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 SQLite 索引构建、投票与过滤（百分比/固定值）在最小数据集上的行为。
// Purpose: Verifies SQLite index build, voting and filtering (percentage/absolute) on a minimal dataset.
public sealed class AssetArchiveIndexServiceTests
{
	[Fact]
	public async Task BuildAndVote_PercentageFilter_FiltersHighDfKeys()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var appRoot = Path.Combine(root, "app");
		var gameData = Path.Combine(root, "game", "data");
		Directory.CreateDirectory(appRoot);
		Directory.CreateDirectory(gameData);

		try
		{
			var archiveA = "aaaaaaaaaaaaaaaa";
			var archiveB = "bbbbbbbbbbbbbbbb";
			File.WriteAllBytes(Path.Combine(gameData, archiveA), BuildToc(new[]
			{
				new AssetKey(10, 100), // shared
				new AssetKey(20, 200), // only in A
			}));
			File.WriteAllBytes(Path.Combine(gameData, archiveB), BuildToc(new[]
			{
				new AssetKey(10, 100), // shared
				new AssetKey(30, 300), // only in B
			}));

			var archiveHashes = new Dictionary<string, Dictionary<string, string>>
			{
				["Armor"] = new Dictionary<string, string>
				{
					[archiveA] = "Armor A",
					[archiveB] = "Armor B",
				}
			};
			var json = JsonSerializer.Serialize(archiveHashes);

			var paths = new StoragePaths(appRoot);
			var indexService = new AssetArchiveIndexService(paths, new PatchTocScanner());
			await indexService.BuildOrRebuildAsync(gameData, json);

            var filter = new IndexFilterSettings(IndexFilterMode.Percentage, PercentageThreshold: 0.8, AbsoluteThreshold: null);

			// Vote using a set including a shared key and unique key.
			// shared df=2/2=1.0 => filtered, unique df=1/2=0.5 => not filtered.
			var votes = await indexService.VoteArchivesAsync(new HashSet<AssetKey>
			{
				new AssetKey(10, 100),
				new AssetKey(20, 200),
			}, filter);

			Assert.Single(votes);
			Assert.True(votes.ContainsKey(archiveA));
			Assert.Equal(1, votes[archiveA]);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task BuildAndVote_AbsoluteFilter_FiltersHighDfKeys()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var appRoot = Path.Combine(root, "app");
		var gameData = Path.Combine(root, "game", "data");
		Directory.CreateDirectory(appRoot);
		Directory.CreateDirectory(gameData);

		try
		{
			var archiveA = "aaaaaaaaaaaaaaaa";
			var archiveB = "bbbbbbbbbbbbbbbb";
			File.WriteAllBytes(Path.Combine(gameData, archiveA), BuildToc(new[]
			{
				new AssetKey(10, 100), // shared df=2
				new AssetKey(20, 200), // only in A df=1
			}));
			File.WriteAllBytes(Path.Combine(gameData, archiveB), BuildToc(new[]
			{
				new AssetKey(10, 100), // shared df=2
			}));

			var archiveHashes = new Dictionary<string, Dictionary<string, string>>
			{
				["Armor"] = new Dictionary<string, string>
				{
					[archiveA] = "Armor A",
					[archiveB] = "Armor B",
				}
			};
			var json = JsonSerializer.Serialize(archiveHashes);

			var paths = new StoragePaths(appRoot);
			var indexService = new AssetArchiveIndexService(paths, new PatchTocScanner());
			await indexService.BuildOrRebuildAsync(gameData, json);

			var filter = new IndexFilterSettings(IndexFilterMode.AbsoluteCount, PercentageThreshold: null, AbsoluteThreshold: 1);

			var votes = await indexService.VoteArchivesAsync(new HashSet<AssetKey>
			{
				new AssetKey(10, 100),
				new AssetKey(20, 200),
			}, filter);

			Assert.Single(votes);
			Assert.True(votes.ContainsKey(archiveA));
			Assert.Equal(1, votes[archiveA]);
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

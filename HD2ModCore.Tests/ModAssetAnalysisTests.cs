using System.Globalization;
using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies asset metadata loading, readable mod summaries and ordered override analysis.
public sealed class ModAssetAnalysisTests
{
	[Fact]
	public async Task CatalogProvider_LoadsArchiveFileAndTypeMetadata()
	{
		var root = CreateTempRoot();
		try
		{
			var paths = new StoragePaths(root);
			WriteMetadata(paths, "aaaaaaaaaaaaaaaa", "Armor A", fileId: 100, fileName: "content/armor/a_body", typeId: 0xe0a48d0be9a7453f, typeName: "unit");

			var provider = new FileSystemAssetMetadataCatalogProvider(paths);
			var catalog = await provider.LoadAsync();

			Assert.Equal("Armor A", catalog.FindArchive("AAAAAAAAAAAAAAAA")!.DisplayName);
			Assert.Equal("content/armor/a_body", catalog.FindFile(100)!.FriendlyName);
			Assert.Equal(AssetTypeCategory.Model, catalog.FindType(0xe0a48d0be9a7453f)!.Category);
		}
		finally
		{
			DeleteQuietly(root);
		}
	}

	[Fact]
	public async Task AnalyzeNodeAsync_BuildsReadableAssetsAndDerivedTags()
	{
		var root = CreateTempRoot();
		try
		{
			var paths = new StoragePaths(root);
			var modsRoot = Path.Combine(root, "mods");
			var modDir = Path.Combine(modsRoot, "mod-a");
			Directory.CreateDirectory(modDir);

			WriteMetadata(paths, "aaaaaaaaaaaaaaaa", "Armor A", fileId: 100, fileName: "content/armor/a_body", typeId: 0xe0a48d0be9a7453f, typeName: "unit");
			await File.WriteAllBytesAsync(Path.Combine(modDir, "aaaaaaaaaaaaaaaa.patch_0"), BuildToc(new[] { new AssetKey(0xe0a48d0be9a7453f, 100) }));

			var analyzer = new ModAssetAnalyzer(new PatchFileNameParser(), new PatchTocScanner(), new FileSystemAssetMetadataCatalogProvider(paths));
			var node = CreateNode("mod-a", "Mod A");

			var summary = await analyzer.AnalyzeNodeAsync(node, modsRoot);

			var asset = Assert.Single(summary.Assets);
			Assert.Equal("Armor A", asset.ArchiveDisplayName);
			Assert.Equal("content/armor/a_body", asset.FileDisplayName);
			Assert.Equal("unit", asset.TypeDisplayName);
			Assert.Contains("armor", summary.DerivedTags);
			Assert.Contains("model", summary.DerivedTags);
		}
		finally
		{
			DeleteQuietly(root);
		}
	}

	[Fact]
	public async Task AnalyzeNodeAsync_DoesNotExposeInternalIdsAsDerivedTags()
	{
		var root = CreateTempRoot();
		try
		{
			var paths = new StoragePaths(root);
			var modsRoot = Path.Combine(root, "mods");
			var modDir = Path.Combine(modsRoot, "mod-a");
			Directory.CreateDirectory(modDir);

			WriteMetadata(paths, "dfa7c28c3b490a8c", "B-08 Light Gunner", fileId: 100, fileName: "12345678901234567890", typeId: 0xe0a48d0be9a7453f, typeName: "unit");
			File.WriteAllText(paths.TypeHashesPath, string.Empty);
			await File.WriteAllBytesAsync(Path.Combine(modDir, "dfa7c28c3b490a8c.patch_0"), BuildToc(new[] { new AssetKey(0xe0a48d0be9a7453f, 100) }));

			var analyzer = new ModAssetAnalyzer(new PatchFileNameParser(), new PatchTocScanner(), new FileSystemAssetMetadataCatalogProvider(paths));
			var node = CreateNode("mod-a", "Mod A");

			var summary = await analyzer.AnalyzeNodeAsync(node, modsRoot);

			Assert.Contains("armor", summary.DerivedTags);
			Assert.Contains("B-08 Light Gunner", summary.DerivedTags);
			Assert.DoesNotContain("b-08", summary.DerivedTags);
			Assert.DoesNotContain("light", summary.DerivedTags);
			Assert.DoesNotContain("gunner", summary.DerivedTags);
			Assert.DoesNotContain(summary.DerivedTags, tag => tag.StartsWith("0x", StringComparison.OrdinalIgnoreCase));
			Assert.DoesNotContain(summary.DerivedTags, tag => tag.All(char.IsDigit));
			Assert.DoesNotContain("dfa7c28c3b490a8c", summary.DerivedTags);
		}
		finally
		{
			DeleteQuietly(root);
		}
	}

	[Fact]
	public async Task AnalyzeNodeAsync_UsesGameDataIndexMatchesForSemanticTags()
	{
		var root = CreateTempRoot();
		try
		{
			var paths = new StoragePaths(root);
			var modsRoot = Path.Combine(root, "mods");
			var modDir = Path.Combine(modsRoot, "mod-a");
			Directory.CreateDirectory(modDir);

			const string baseArchive = "9ba626afa44a3aa3";
			WriteMetadata(paths, baseArchive, "Base Archive", fileId: 100, fileName: "content/unknown/asset", typeId: 0xe0a48d0be9a7453f, typeName: "unit");
			await File.WriteAllBytesAsync(Path.Combine(modDir, $"{baseArchive}.patch_0"), BuildToc(new[] { new AssetKey(0xe0a48d0be9a7453f, 100) }));

			var target = new ArchiveMetadata("dfa7c28c3b490a8c", "Armor", "B-08 Light Gunner");
			var index = new StubAssetArchiveIndexService(new Dictionary<AssetKey, IReadOnlyList<ArchiveMetadata>>
			{
				[new AssetKey(0xe0a48d0be9a7453f, 100)] = new[] { target },
			});
			var analyzer = new ModAssetAnalyzer(new PatchFileNameParser(), new PatchTocScanner(), new FileSystemAssetMetadataCatalogProvider(paths), index);
			var node = CreateNode("mod-a", "Mod A");

			var summary = await analyzer.AnalyzeNodeAsync(node, modsRoot);

			var asset = Assert.Single(summary.Assets);
			Assert.Equal("B-08 Light Gunner", asset.ArchiveDisplayName);
			Assert.Equal("Armor", asset.ArchiveCategory);
			Assert.Contains("B-08 Light Gunner", summary.DerivedTags);
			Assert.DoesNotContain("b-08", summary.DerivedTags);
			Assert.DoesNotContain("light", summary.DerivedTags);
			Assert.DoesNotContain("gunner", summary.DerivedTags);
			Assert.Contains("armor", summary.DerivedTags);
		}
		finally
		{
			DeleteQuietly(root);
		}
	}

	[Fact]
	public async Task AnalyzeNodeAsync_BuildsOrderedAssetTargetGroups()
	{
		var root = CreateTempRoot();
		try
		{
			var paths = new StoragePaths(root);
			var modsRoot = Path.Combine(root, "mods");
			var modDir = Path.Combine(modsRoot, "mod-a");
			Directory.CreateDirectory(modDir);

			WriteMetadata(paths, new[]
			{
				("Armor", "aaaaaaaaaaaaaaaa", "B-08 Light Gunner"),
				("Weapons", "bbbbbbbbbbbbbbbb", "AR-23 Liberator"),
			}, fileId: 100, fileName: "content/shared/asset", typeId: 0xe0a48d0be9a7453f, typeName: "unit");
			await File.WriteAllBytesAsync(Path.Combine(modDir, "aaaaaaaaaaaaaaaa.patch_0"), BuildToc(new[] { new AssetKey(0xe0a48d0be9a7453f, 100) }));
			await File.WriteAllBytesAsync(Path.Combine(modDir, "bbbbbbbbbbbbbbbb.patch_0"), BuildToc(new[] { new AssetKey(0xe0a48d0be9a7453f, 100) }));

			var analyzer = new ModAssetAnalyzer(new PatchFileNameParser(), new PatchTocScanner(), new FileSystemAssetMetadataCatalogProvider(paths));
			var node = CreateNode("mod-a", "Mod A");

			var summary = await analyzer.AnalyzeNodeAsync(node, modsRoot);

			Assert.Collection(summary.TargetGroups,
				group =>
				{
					Assert.Equal("Armor", group.Category);
					var item = Assert.Single(group.Items);
					Assert.Equal("B-08 Light Gunner", item.DisplayName);
					Assert.Equal(1, item.AssetCount);
					Assert.Contains("unit", item.TypeNames);
				},
				group =>
				{
					Assert.Equal("Weapons", group.Category);
					var item = Assert.Single(group.Items);
					Assert.Equal("AR-23 Liberator", item.DisplayName);
				});
		}
		finally
		{
			DeleteQuietly(root);
		}
	}

	[Fact]
	public async Task OverrideAnalyzer_ReportsWinnerAndFullyOverriddenMods()
	{
		var root = CreateTempRoot();
		try
		{
			var paths = new StoragePaths(root);
			var modsRoot = Path.Combine(root, "mods");
			Directory.CreateDirectory(modsRoot);

			WriteMetadata(paths, "aaaaaaaaaaaaaaaa", "Armor A", fileId: 100, fileName: "content/armor/a_body", typeId: 0xe0a48d0be9a7453f, typeName: "unit");

			var nodeA = CreateNode("mod-a", "Mod A");
			var nodeB = CreateNode("mod-b", "Mod B");
			Directory.CreateDirectory(Path.Combine(modsRoot, nodeA.RelativePath));
			Directory.CreateDirectory(Path.Combine(modsRoot, nodeB.RelativePath));
			await File.WriteAllBytesAsync(Path.Combine(modsRoot, nodeA.RelativePath, "aaaaaaaaaaaaaaaa.patch_0"), BuildToc(new[] { new AssetKey(0xe0a48d0be9a7453f, 100) }));
			await File.WriteAllBytesAsync(Path.Combine(modsRoot, nodeB.RelativePath, "aaaaaaaaaaaaaaaa.patch_0"), BuildToc(new[] { new AssetKey(0xe0a48d0be9a7453f, 100) }));

			var snapshot = new LibrarySnapshot(
				1,
				DateTimeOffset.UtcNow,
				new Dictionary<ModNodeId, ModNode> { [nodeA.Id] = nodeA, [nodeB.Id] = nodeB },
				Array.Empty<Profile>());
			var entries = new[]
			{
				new ProfileEntry(nodeA.Id, 0, true),
				new ProfileEntry(nodeB.Id, 1, true),
			};
			var assetAnalyzer = new ModAssetAnalyzer(new PatchFileNameParser(), new PatchTocScanner(), new FileSystemAssetMetadataCatalogProvider(paths));
			var overrideAnalyzer = new ModAssetOverrideAnalyzer(assetAnalyzer);

			var analysis = await overrideAnalyzer.AnalyzeAsync(entries, snapshot, modsRoot);

			var chain = Assert.Single(analysis.OverrideChains);
			Assert.Equal(nodeB.Id, chain.Winner.NodeId);
			Assert.Equal("Mod B", chain.Winner.ModName);
			var coverageA = Assert.Single(analysis.Coverages, c => c.NodeId == nodeA.Id);
			Assert.True(coverageA.FullyOverridden);
			var coverageB = Assert.Single(analysis.Coverages, c => c.NodeId == nodeB.Id);
			Assert.False(coverageB.FullyOverridden);
		}
		finally
		{
			DeleteQuietly(root);
		}
	}

	[Fact]
	public async Task CachedAnalyzer_ReusesSummaryUntilPatchFileChanges()
	{
		var root = CreateTempRoot();
		try
		{
			var paths = new StoragePaths(root);
			var modsRoot = Path.Combine(root, "mods");
			var node = CreateNode("mod-a", "Mod A");
			var modDir = Path.Combine(modsRoot, node.RelativePath);
			Directory.CreateDirectory(modDir);

			WriteMetadata(paths, "aaaaaaaaaaaaaaaa", "Armor A", fileId: 100, fileName: "content/armor/a_body", typeId: 0xe0a48d0be9a7453f, typeName: "unit");
			var patchPath = Path.Combine(modDir, "aaaaaaaaaaaaaaaa.patch_0");
			await File.WriteAllBytesAsync(patchPath, BuildToc(new[] { new AssetKey(0xe0a48d0be9a7453f, 100) }));

			var inner = new CountingModAssetAnalyzer(new ModAssetAnalyzer(new PatchFileNameParser(), new PatchTocScanner(), new FileSystemAssetMetadataCatalogProvider(paths)));
			var analyzer = new CachedModAssetAnalyzer(inner, new FileSystemModAssetAnalysisCacheStore(paths), new PatchFileNameParser(), paths);

			var first = await analyzer.AnalyzeNodeAsync(node, modsRoot);
			var second = await analyzer.AnalyzeNodeAsync(node, modsRoot);

			Assert.Equal(1, inner.CallCount);
			Assert.Single(first.Assets);
			Assert.Single(second.Assets);

			await Task.Delay(20);
			await File.WriteAllBytesAsync(patchPath, BuildToc(new[]
			{
				new AssetKey(0xe0a48d0be9a7453f, 100),
				new AssetKey(0xe0a48d0be9a7453f, 101),
			}));

			var third = await analyzer.AnalyzeNodeAsync(node, modsRoot);

			Assert.Equal(2, inner.CallCount);
			Assert.Equal(2, third.Assets.Count);
		}
		finally
		{
			DeleteQuietly(root);
		}
	}

	private static string CreateTempRoot()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		return root;
	}

	private static ModNode CreateNode(string relativePath, string name)
		=> new(
			ModNodeId.New(),
			relativePath,
			new ModNodeMetadata(name, null, Array.Empty<string>(), DateTimeOffset.UtcNow, null),
			Array.Empty<PatchGroupKey>(),
			Array.Empty<ModNodeId>());

	private static void WriteMetadata(StoragePaths paths, string archiveId, string archiveName, ulong fileId, string fileName, ulong typeId, string typeName)
		=> WriteMetadata(paths, new[] { ("Armor", archiveId, archiveName) }, fileId, fileName, typeId, typeName);

	private static void WriteMetadata(StoragePaths paths, IReadOnlyList<(string Category, string ArchiveId, string ArchiveName)> archives, ulong fileId, string fileName, ulong typeId, string typeName)
	{
		Directory.CreateDirectory(paths.ResourcesDirectory);
		var archiveMap = new Dictionary<string, Dictionary<string, string>>();
		foreach (var archive in archives)
		{
			if (!archiveMap.TryGetValue(archive.Category, out var category))
			{
				category = new Dictionary<string, string>();
				archiveMap[archive.Category] = category;
			}

			category[archive.ArchiveId] = archive.ArchiveName;
		}

		File.WriteAllText(paths.ArchiveHashesPath, JsonSerializer.Serialize(archiveMap));
		File.WriteAllText(paths.FriendlyNamesPath, $"{fileId.ToString(CultureInfo.InvariantCulture)} {fileName}{Environment.NewLine}");
		File.WriteAllText(paths.TypeHashesPath, $"{typeId:x16} {typeName}{Environment.NewLine}");
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

	private static void DeleteQuietly(string path)
	{
		try { Directory.Delete(path, recursive: true); } catch { }
	}

	private sealed class CountingModAssetAnalyzer : HD2ModCore.Application.IModAssetAnalyzer
	{
		private readonly IModAssetAnalyzer _inner;

		public int CallCount { get; private set; }

		public CountingModAssetAnalyzer(IModAssetAnalyzer inner)
		{
			_inner = inner;
		}

		public async ValueTask<ModAssetSummary> AnalyzeNodeAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
		{
			CallCount++;
			return await _inner.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken);
		}
	}

	private sealed class StubAssetArchiveIndexService : IAssetArchiveIndexService
	{
		private readonly IReadOnlyDictionary<AssetKey, IReadOnlyList<ArchiveMetadata>> _matches;

		public StubAssetArchiveIndexService(IReadOnlyDictionary<AssetKey, IReadOnlyList<ArchiveMetadata>> matches)
		{
			_matches = matches;
		}

		public ValueTask<bool> IndexExistsAsync(CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(true);

		public ValueTask<GameDataIndexFingerprint?> GetFingerprintAsync(CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<GameDataIndexFingerprint?>(null);

		public ValueTask<GameDataIndexStatus> GetIndexStatusAsync(string gameDataDirectory, string archiveHashesJson, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(new GameDataIndexStatus(GameDataIndexState.Current, null, gameDataDirectory, "stub"));

		public ValueTask BuildOrRebuildAsync(string gameDataDirectory, string archiveHashesJson, IProgress<IndexBuildProgress>? progress = null, CancellationToken cancellationToken = default)
			=> ValueTask.CompletedTask;

		public ValueTask<IReadOnlyList<AssetArchiveMatch>> FindAssetArchivesAsync(IReadOnlySet<AssetKey> assetKeys, CancellationToken cancellationToken = default)
		{
			var result = assetKeys
				.Select(key => new AssetArchiveMatch(key, _matches.TryGetValue(key, out var archives) ? archives : Array.Empty<ArchiveMetadata>()))
				.ToList();
			return ValueTask.FromResult<IReadOnlyList<AssetArchiveMatch>>(result);
		}

		public ValueTask<IReadOnlyDictionary<string, int>> VoteArchivesAsync(IReadOnlySet<AssetKey> assetKeys, IndexFilterSettings filterSettings, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
	}
}
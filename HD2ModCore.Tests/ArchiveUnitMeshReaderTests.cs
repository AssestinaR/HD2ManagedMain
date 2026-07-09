using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 ArchiveUnitMeshReader 能从原版游戏 archive 中定位并解析目标 Unit 模板。
// Purpose: Verifies ArchiveUnitMeshReader can locate and parse target Unit templates from vanilla game archives.
public sealed class ArchiveUnitMeshReaderTests
{
	private const ulong UnitTypeId = PatchUnitMeshReader.UnitTypeId;
	private const ulong CompositeUnitTypeId = 0xc4f0f4be7fb0c8d6;

	[Fact]
	public async Task ReadUnitMeshAsync_ArchiveUnitEntry_ReadsPayloadAndModel()
	{
		var key = new AssetKey(UnitTypeId, 0x123456789abcdef0);
		var entry = new PatchTocEntry(
			key,
			"archive",
			"archive",
			TocDataOffset: 10,
			StreamOffset: 20,
			GpuResourceOffset: 30,
			TocDataSize: 3,
			StreamSize: 2,
			GpuResourceSize: 4,
			EntryIndex: 7);
		var tocData = new byte[] { 1, 2, 3 };
		var streamData = new byte[] { 4, 5 };
		var gpuData = new byte[] { 6, 7, 8, 9 };
		var model = CreateEmptyModel(version: 42);
		var resolver = new FakeGameDataPackageResolver(
			new GameDataPackageToc(new byte[] { 99 }, UsesSlimEntryOffset: true),
			new Dictionary<(string PackageName, ulong Offset, uint Size), byte[]>
			{
				[("archive", entry.TocDataOffset, entry.TocDataSize)] = tocData,
				[("archive.stream", entry.StreamOffset, entry.StreamSize)] = streamData,
				[("archive.gpu_resources", entry.GpuResourceOffset, entry.GpuResourceSize)] = gpuData,
			});
		var scanner = new FakePatchTocScanner(new[] { entry });
		var unitReader = new FakeUnitMeshReader(model);
		var reader = new ArchiveUnitMeshReader(_ => resolver, scanner, unitReader);

		var result = await reader.ReadUnitMeshAsync("game", "archive", key);

		Assert.Equal(key, result.Entry.AssetKey);
		Assert.Equal("archive", result.Entry.ArchiveName);
		Assert.Equal(entry.TocDataOffset, result.Entry.TocDataOffset);
		Assert.Equal(entry.StreamOffset, result.Entry.StreamOffset);
		Assert.Equal(entry.GpuResourceOffset, result.Entry.GpuResourceOffset);
		Assert.Equal(entry.TocDataSize, result.Entry.TocDataSize);
		Assert.Equal(entry.StreamSize, result.Entry.StreamSize);
		Assert.Equal(entry.GpuResourceSize, result.Entry.GpuResourceSize);
		Assert.Equal(entry.EntryIndex, result.Entry.EntryIndex);
		Assert.Equal(tocData, result.Payload.TocData);
		Assert.Equal(streamData, result.Payload.StreamData);
		Assert.Equal(gpuData, result.Payload.GpuResourceData);
		Assert.Same(model, result.Model);
		Assert.True(scanner.UsesSlimEntryOffset);
		Assert.Equal("archive", scanner.SourceFilePath);
		Assert.Equal(tocData, unitReader.LastTocData);
		Assert.Equal(gpuData, unitReader.LastGpuData);
	}

	[Fact]
	public async Task ReadUnitMeshAsync_CompositeBackedUnit_ReadsCompositePayloadFromSameArchive()
	{
		var unitKey = new AssetKey(UnitTypeId, 0x123456789abcdef0);
		var compositeKey = new AssetKey(CompositeUnitTypeId, 0xfedcba9876543210);
		var unitEntry = new PatchTocEntry(
			unitKey,
			"archive",
			"archive",
			TocDataOffset: 10,
			GpuResourceOffset: 30,
			TocDataSize: 24,
			GpuResourceSize: 4,
			EntryIndex: 7);
		var compositeEntry = new PatchTocEntry(
			compositeKey,
			"archive",
			"archive",
			TocDataOffset: 40,
			GpuResourceOffset: 70,
			TocDataSize: 5,
			GpuResourceSize: 6,
			EntryIndex: 8);
		var unitTocData = new byte[24];
		WriteUInt64(unitTocData, 16, compositeKey.FileId);
		var unitGpuData = new byte[] { 1, 2, 3, 4 };
		var compositeTocData = new byte[] { 5, 6, 7, 8, 9 };
		var compositeGpuData = new byte[] { 10, 11, 12, 13, 14, 15 };
		var model = CreateEmptyModel(version: 42);
		var resolver = new FakeGameDataPackageResolver(
			new GameDataPackageToc(new byte[] { 99 }, UsesSlimEntryOffset: true),
			new Dictionary<(string PackageName, ulong Offset, uint Size), byte[]>
			{
				[("archive", unitEntry.TocDataOffset, unitEntry.TocDataSize)] = unitTocData,
				[("archive.gpu_resources", unitEntry.GpuResourceOffset, unitEntry.GpuResourceSize)] = unitGpuData,
				[("archive", compositeEntry.TocDataOffset, compositeEntry.TocDataSize)] = compositeTocData,
				[("archive.gpu_resources", compositeEntry.GpuResourceOffset, compositeEntry.GpuResourceSize)] = compositeGpuData,
			});
		var scanner = new FakePatchTocScanner(new[] { unitEntry, compositeEntry });
		var unitReader = new FakeUnitMeshReader(model);
		var reader = new ArchiveUnitMeshReader(_ => resolver, scanner, unitReader);

		var result = await reader.ReadUnitMeshAsync("game", "archive", unitKey);

		Assert.Equal(unitKey, result.Entry.AssetKey);
		Assert.Equal(unitTocData, unitReader.LastTocData);
		Assert.Equal(unitGpuData, unitReader.LastGpuData);
		Assert.Equal(compositeTocData, unitReader.LastCompositeTocData);
		Assert.Equal(compositeGpuData, unitReader.LastCompositeGpuData);
	}

	[Fact]
	public async Task ReadUnitMeshAsync_NonUnitAssetKey_Throws()
	{
		var reader = CreateReader();
		var key = new AssetKey(0x1111, 0x2222);

		await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadUnitMeshAsync("game", "archive", key).AsTask());
	}

	[Fact]
	public async Task ReadUnitMeshAsync_MissingArchiveToc_Throws()
	{
		var key = new AssetKey(UnitTypeId, 0x2222);
		var reader = CreateReader(new FakeGameDataPackageResolver(null, new Dictionary<(ulong Offset, uint Size), byte[]>()));

		await Assert.ThrowsAsync<FileNotFoundException>(() => reader.ReadUnitMeshAsync("game", "archive", key).AsTask());
	}

	[Fact]
	public async Task ReadUnitMeshAsync_MissingAsset_Throws()
	{
		var key = new AssetKey(UnitTypeId, 0x2222);
		var resolver = new FakeGameDataPackageResolver(new GameDataPackageToc(Array.Empty<byte>(), UsesSlimEntryOffset: false), new Dictionary<(ulong Offset, uint Size), byte[]>());
		var reader = CreateReader(resolver, new FakePatchTocScanner(Array.Empty<PatchTocEntry>()));

		await Assert.ThrowsAsync<KeyNotFoundException>(() => reader.ReadUnitMeshAsync("game", "archive", key).AsTask());
	}

	[Fact]
	public async Task ReadUnitMeshAsync_MissingRequiredResource_Throws()
	{
		var key = new AssetKey(UnitTypeId, 0x2222);
		var entry = new PatchTocEntry(key, "archive", "archive", TocDataOffset: 10, TocDataSize: 3);
		var resolver = new FakeGameDataPackageResolver(new GameDataPackageToc(Array.Empty<byte>(), UsesSlimEntryOffset: false), new Dictionary<(ulong Offset, uint Size), byte[]>());
		var reader = CreateReader(resolver, new FakePatchTocScanner(new[] { entry }));

		await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadUnitMeshAsync("game", "archive", key).AsTask());
	}

	private static ArchiveUnitMeshReader CreateReader(
		IGameDataPackageResolver? resolver = null,
		IPatchTocScanner? scanner = null,
		IUnitMeshReader? unitReader = null)
		=> new(
			_ => resolver ?? new FakeGameDataPackageResolver(new GameDataPackageToc(Array.Empty<byte>(), UsesSlimEntryOffset: false), new Dictionary<(ulong Offset, uint Size), byte[]>()),
			scanner ?? new FakePatchTocScanner(Array.Empty<PatchTocEntry>()),
			unitReader ?? new FakeUnitMeshReader(CreateEmptyModel()));

	private static UnitMeshModel CreateEmptyModel(uint version = 0)
		=> new(
			0,
			0,
			0,
			version,
			0,
			0,
			0,
			0,
			0,
			0,
			UnitCustomizationInfo.Empty,
			Array.Empty<UnitBoneInfo>(),
			Array.Empty<UnitStreamInfo>(),
			Array.Empty<UnitMeshInfo>(),
			Array.Empty<UnitMaterialBinding>(),
			Array.Empty<UnitRawMeshSummary>(),
			Array.Empty<UnitRawMeshData>());

	private static void WriteUInt64(byte[] data, int offset, ulong value)
	{
		for (var i = 0; i < 8; i++)
		{
			data[offset + i] = (byte)(value >> (i * 8));
		}
	}

	private sealed class FakeGameDataPackageResolver : IGameDataPackageResolver
	{
		private readonly GameDataPackageToc? toc;
		private readonly IReadOnlyDictionary<(string PackageName, ulong Offset, uint Size), byte[]> resources;

		public FakeGameDataPackageResolver(GameDataPackageToc? toc, IReadOnlyDictionary<(ulong Offset, uint Size), byte[]> resources)
			: this(toc, resources.ToDictionary(pair => ("archive", pair.Key.Offset, pair.Key.Size), pair => pair.Value))
		{
		}

		public FakeGameDataPackageResolver(GameDataPackageToc? toc, IReadOnlyDictionary<(string PackageName, ulong Offset, uint Size), byte[]> resources)
		{
			this.toc = toc;
			this.resources = resources;
		}

		public ValueTask<GameDataPackageToc?> GetPackageTocAsync(string packageName, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(toc);

		public ValueTask<byte[]?> GetPackageResourceAsync(string packageName, ulong resourceOffset, uint resourceSize, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(resources.TryGetValue((Path.GetFileName(packageName), resourceOffset, resourceSize), out var data) ? data : null);
	}

	private sealed class FakePatchTocScanner : IPatchTocScanner
	{
		private readonly IReadOnlyList<PatchTocEntry> entries;

		public FakePatchTocScanner(IReadOnlyList<PatchTocEntry> entries)
		{
			this.entries = entries;
		}

		public bool UsesSlimEntryOffset { get; private set; }
		public string? SourceFilePath { get; private set; }

		public ValueTask<IReadOnlySet<AssetKey>> ScanAssetKeysAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<IReadOnlySet<AssetKey>>(entries.Select(e => e.AssetKey).ToHashSet());

		public IReadOnlySet<AssetKey> ScanAssetKeys(ReadOnlySpan<byte> tocData, bool usesSlimEntryOffset = false)
			=> entries.Select(e => e.AssetKey).ToHashSet();

		public IReadOnlyList<PatchTocEntry> ScanEntries(ReadOnlySpan<byte> tocData, string sourceFilePath, bool usesSlimEntryOffset = false)
		{
			UsesSlimEntryOffset = usesSlimEntryOffset;
			SourceFilePath = sourceFilePath;
			return entries;
		}

		public ValueTask<IReadOnlyList<PatchTocEntry>> ScanEntriesAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(entries);
	}

	private sealed class FakeUnitMeshReader : IUnitMeshReader
	{
		private readonly UnitMeshModel model;

		public FakeUnitMeshReader(UnitMeshModel model)
		{
			this.model = model;
		}

		public byte[]? LastTocData { get; private set; }
		public byte[]? LastGpuData { get; private set; }
		public byte[]? LastCompositeTocData { get; private set; }
		public byte[]? LastCompositeGpuData { get; private set; }

		public UnitMeshModel Read(ReadOnlySpan<byte> tocData, ReadOnlySpan<byte> gpuData, ReadOnlySpan<byte> compositeTocData = default, ReadOnlySpan<byte> compositeGpuData = default)
		{
			LastTocData = tocData.ToArray();
			LastGpuData = gpuData.ToArray();
			LastCompositeTocData = compositeTocData.ToArray();
			LastCompositeGpuData = compositeGpuData.ToArray();
			return model;
		}
	}
}

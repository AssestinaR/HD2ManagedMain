using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies explicit vanilla archive Unit loading without global archive guessing.
public sealed class GameDataUnitMeshReaderTests
{
	[Fact]
	public async Task ReadAsync_InlineUnit_ReadsOnlyTheSelectedArchive()
	{
		var unitKey = new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1234);
		var unitToc = CreateMinimalUnitToc();
		var resolver = new FakePackageResolver();
		resolver.AddPackage("units", new Dictionary<AssetKey, byte[]> { [unitKey] = unitToc });

		var result = await new GameDataUnitMeshReader(resolver).ReadAsync("units", unitKey);

		Assert.Equal(unitKey, result.AssetKey);
		Assert.Equal("units", result.ArchiveName);
		Assert.Equal(unitToc, result.Payload.TocData);
		Assert.Null(result.CompositePayload);
		Assert.Equal(new[] { "units" }, resolver.TocRequests);
	}

	[Fact]
	public async Task ReadAsync_CompositeInExplicitDependencyArchive_ReadsCompositePayload()
	{
		var unitKey = new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1234);
		var compositeKey = new AssetKey(PatchUnitMeshReader.CompositeUnitTypeId, 0x5678);
		var unitToc = CreateMinimalUnitToc(compositeKey.FileId);
		var compositeToc = CreateMinimalCompositeToc();
		var resolver = new FakePackageResolver();
		resolver.AddPackage("units", new Dictionary<AssetKey, byte[]> { [unitKey] = unitToc });
		resolver.AddPackage("composites", new Dictionary<AssetKey, byte[]> { [compositeKey] = compositeToc });

		var result = await new GameDataUnitMeshReader(resolver).ReadAsync("units", unitKey, new[] { "composites" });

		Assert.Equal(compositeToc, result.CompositePayload!.TocData);
		Assert.Equal(new[] { "units", "composites" }, resolver.TocRequests);
	}

	[Fact]
	public async Task ReadAsync_CompositeOutsideExplicitArchiveScope_Throws()
	{
		var unitKey = new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1234);
		var compositeKey = new AssetKey(PatchUnitMeshReader.CompositeUnitTypeId, 0x5678);
		var resolver = new FakePackageResolver();
		resolver.AddPackage("units", new Dictionary<AssetKey, byte[]> { [unitKey] = CreateMinimalUnitToc(compositeKey.FileId) });
		resolver.AddPackage("composites", new Dictionary<AssetKey, byte[]> { [compositeKey] = CreateMinimalCompositeToc() });

		var exception = await Assert.ThrowsAsync<InvalidDataException>(() => new GameDataUnitMeshReader(resolver).ReadAsync("units", unitKey).AsTask());

		Assert.Contains("explicit archive scope", exception.Message);
		Assert.Equal(new[] { "units" }, resolver.TocRequests);
	}

	private static byte[] CreateMinimalUnitToc(ulong compositeFileId = 0)
	{
		var data = new byte[136];
		WriteUInt64(data, 16, compositeFileId);
		WriteUInt32(data, 0x2c, 1);
		WriteUInt32(data, 0x5c, compositeFileId == 0 ? 96u : 0u);
		WriteUInt32(data, 0x64, 112);
		WriteUInt32(data, 0x70, 0);
		WriteUInt32(data, 96, 0);
		WriteUInt32(data, 112, 0);
		return data;
	}

	private static byte[] CreateMinimalCompositeToc()
	{
		var data = new byte[40];
		WriteUInt32(data, 8, 1);
		WriteUInt32(data, 12, 0);
		WriteUInt64(data, 16, 0);
		WriteUInt64(data, 24, 0);
		WriteUInt32(data, 32, 36);
		WriteUInt32(data, 36, 0);
		return data;
	}

	private static byte[] CreateToc(IReadOnlyDictionary<AssetKey, byte[]> payloads, out Dictionary<(ulong Offset, uint Size), byte[]> resources)
	{
		const int entryOffset = 60;
		var data = new byte[entryOffset + payloads.Count * 80];
		resources = new Dictionary<(ulong Offset, uint Size), byte[]>();
		WriteUInt32(data, 0, 0xf0000011);
		WriteUInt32(data, 8, (uint)payloads.Count);
		var offset = 0UL;
		var index = 0;
		foreach (var (key, payload) in payloads)
		{
			var entryOffsetInData = entryOffset + index * 80;
			WriteUInt64(data, entryOffsetInData, key.FileId);
			WriteUInt64(data, entryOffsetInData + 8, key.TypeId);
			WriteUInt64(data, entryOffsetInData + 16, offset);
			WriteUInt32(data, entryOffsetInData + 56, (uint)payload.Length);
			WriteUInt32(data, entryOffsetInData + 76, (uint)(index + 1));
			resources[(offset, (uint)payload.Length)] = payload;
			offset += (uint)payload.Length;
			index++;
		}

		return data;
	}

	private static void WriteUInt32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void WriteUInt64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);

	private sealed class FakePackageResolver : IGameDataPackageResolver
	{
		private readonly Dictionary<string, GameDataPackageToc> tocs = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Dictionary<(ulong Offset, uint Size), byte[]>> resources = new(StringComparer.OrdinalIgnoreCase);

		public List<string> TocRequests { get; } = new();

		public void AddPackage(string packageName, IReadOnlyDictionary<AssetKey, byte[]> payloads)
		{
			tocs[packageName] = new GameDataPackageToc(CreateToc(payloads, out var packageResources), false);
			resources[packageName] = packageResources;
		}

		public ValueTask<GameDataPackageToc?> GetPackageTocAsync(string packageName, CancellationToken cancellationToken = default)
		{
			TocRequests.Add(packageName);
			return ValueTask.FromResult(tocs.TryGetValue(packageName, out var toc) ? toc : null);
		}

		public ValueTask<byte[]?> GetPackageResourceAsync(string packageName, ulong resourceOffset, uint resourceSize, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(resources.TryGetValue(packageName, out var packageResources)
				&& packageResources.TryGetValue((resourceOffset, resourceSize), out var payload)
				? payload
				: null);

		public ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<IReadOnlyList<string>>(tocs.Keys.ToArray());
	}
}
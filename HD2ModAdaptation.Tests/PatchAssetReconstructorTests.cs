using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies direct Unit edit reconstruction includes a complete material payload closure.
public sealed class PatchAssetReconstructorTests : IDisposable
{
	private const ulong UnitType = 0xe0a48d0be9a7453f;
	private const ulong UnitId = 0x0102030405060708;
	private const ulong MaterialId = 0x1111111111111111;
	private const ulong TextureId = 0x2222222222222222;
	private readonly string root = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task ReconstructAsync_WritesUnitAndGameMaterialClosure()
	{
		var sourceDirectory = Path.Combine(root, "source");
		var outputDirectory = Path.Combine(root, "output");
		Directory.CreateDirectory(sourceDirectory);
		var sourcePath = Path.Combine(sourceDirectory, "unit.patch");
		await File.WriteAllBytesAsync(sourcePath, CreatePatch(new Dictionary<AssetKey, byte[]>
		{
			[new AssetKey(UnitType, UnitId)] = new byte[] { 1, 2, 3 },
		}));
		var scanner = new PatchTocScanner();
		var unitEntry = Assert.Single(await scanner.ScanEntriesAsync(sourcePath));
		var original = await new PatchEntryPayloadReader().ReadPayloadAsync(unitEntry);
		var edit = new PatchUnitMeshEditResult(unitEntry, original, new byte[] { 9, 8 }, new byte[] { 7 }, ReplacementMaterialIds: new[] { MaterialId });
		var packages = new FakePackageResolver();
		packages.AddPackage("materials", new Dictionary<AssetKey, byte[]>
		{
			[MaterialKey] = CreateMaterialPayload(TextureId),
			[TextureKey] = new byte[] { 4, 5, 6 },
		});
		var dependencies = new MaterialDependencyResolver(gameResolverFactory: _ => packages);
		var reconstructor = new PatchAssetReconstructor(materialResolver: dependencies);

		var result = await reconstructor.ReconstructAsync(
			sourcePath,
			outputDirectory,
			new[] { edit },
			new Dictionary<AssetKey, IReadOnlyList<string>>(),
			"unused");

		Assert.Equal(new[] { MaterialId }, result.MaterialIds);
		Assert.Empty(result.MaterialDependencies.RejectedMaterialReasons);
		var rebuiltEntries = await scanner.ScanEntriesAsync(result.WriteResult.TocFilePath);
		Assert.Equal(3, rebuiltEntries.Count);
		Assert.Equal(new byte[] { 9, 8 }, (await new PatchEntryPayloadReader().ReadPayloadAsync(Assert.Single(rebuiltEntries, entry => entry.AssetKey == new AssetKey(UnitType, UnitId)))).TocData);
		Assert.Equal(CreateMaterialPayload(TextureId), (await new PatchEntryPayloadReader().ReadPayloadAsync(Assert.Single(rebuiltEntries, entry => entry.AssetKey == MaterialKey))).TocData);
		Assert.Equal(new byte[] { 4, 5, 6 }, (await new PatchEntryPayloadReader().ReadPayloadAsync(Assert.Single(rebuiltEntries, entry => entry.AssetKey == TextureKey))).TocData);
	}

	[Fact]
	public async Task ReconstructAsync_DoesNotDuplicateSourcePatchDependencies()
	{
		var sourceDirectory = Path.Combine(root, "source");
		var outputDirectory = Path.Combine(root, "output");
		Directory.CreateDirectory(sourceDirectory);
		var sourcePath = Path.Combine(sourceDirectory, "unit.patch");
		await File.WriteAllBytesAsync(sourcePath, CreatePatch(new Dictionary<AssetKey, byte[]>
		{
			[new AssetKey(UnitType, UnitId)] = new byte[] { 1 },
			[MaterialKey] = CreateMaterialPayload(TextureId),
			[TextureKey] = new byte[] { 2 },
		}));
		var scanner = new PatchTocScanner();
		var unitEntry = Assert.Single(await scanner.ScanEntriesAsync(sourcePath), entry => entry.AssetKey.TypeId == UnitType);
		var original = await new PatchEntryPayloadReader().ReadPayloadAsync(unitEntry);
		var edit = new PatchUnitMeshEditResult(unitEntry, original, new byte[] { 3 }, Array.Empty<byte>(), ReplacementMaterialIds: new[] { MaterialId });

		var result = await new PatchAssetReconstructor().ReconstructAsync(sourcePath, outputDirectory, new[] { edit }, new Dictionary<AssetKey, IReadOnlyList<string>>(), root);

		Assert.Equal(3, (await scanner.ScanEntriesAsync(result.WriteResult.TocFilePath)).Count);
	}

	public void Dispose()
	{
		if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
	}

	private static AssetKey MaterialKey => new(MaterialDependencyResolver.MaterialTypeId, MaterialId);
	private static AssetKey TextureKey => new(MaterialDependencyResolver.TextureTypeId, TextureId);

	private static byte[] CreateMaterialPayload(params ulong[] textureIds)
	{
		var data = new byte[136 + textureIds.Length * 4 + textureIds.Length * 8];
		Write32(data, 64, (uint)textureIds.Length);
		for (var index = 0; index < textureIds.Length; index++) Write64(data, 136 + textureIds.Length * 4 + index * 8, textureIds[index]);
		return data;
	}

	private static byte[] CreatePatch(IReadOnlyDictionary<AssetKey, byte[]> entries)
	{
		const int typeOffset = 60;
		var types = entries.Keys.Select(key => key.TypeId).Distinct().ToArray();
		var entryOffset = typeOffset + types.Length * 32;
		var payloadOffset = entryOffset + entries.Count * 80;
		var data = new byte[payloadOffset + entries.Sum(pair => pair.Value.Length)];
		Write32(data, 0, 4026531857); Write32(data, 4, (uint)types.Length); Write32(data, 8, (uint)entries.Count);
		for (var index = 0; index < types.Length; index++) { Write64(data, typeOffset + index * 32 + 8, types[index]); Write64(data, typeOffset + index * 32 + 16, (ulong)entries.Count(pair => pair.Key.TypeId == types[index])); }
		var offset = payloadOffset;
		foreach (var (key, payload, index) in entries.Select((pair, index) => (pair.Key, pair.Value, index)))
		{
			var entry = entryOffset + index * 80;
			Write64(data, entry, key.FileId); Write64(data, entry + 8, key.TypeId); Write64(data, entry + 16, (ulong)offset); Write32(data, entry + 56, (uint)payload.Length); Write32(data, entry + 76, (uint)(index + 1));
			payload.CopyTo(data, offset); offset += payload.Length;
		}
		return data;
	}

	private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void Write64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);

	private sealed class FakePackageResolver : IGameDataPackageResolver
	{
		private readonly Dictionary<string, GameDataPackageToc> tocs = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Dictionary<(ulong Offset, uint Size), byte[]>> resources = new(StringComparer.OrdinalIgnoreCase);

		public void AddPackage(string name, IReadOnlyDictionary<AssetKey, byte[]> entries)
		{
			var data = CreatePatch(entries);
			tocs[name] = new GameDataPackageToc(data.AsSpan(0, 60 + entries.Keys.Select(key => key.TypeId).Distinct().Count() * 32 + entries.Count * 80).ToArray(), false);
			var map = new Dictionary<(ulong Offset, uint Size), byte[]>();
			foreach (var entry in new PatchTocScanner().ScanEntries(data, name)) map[(entry.TocDataOffset, entry.TocDataSize)] = data.AsSpan((int)entry.TocDataOffset, (int)entry.TocDataSize).ToArray();
			resources[name] = map;
		}

		public ValueTask<GameDataPackageToc?> GetPackageTocAsync(string packageName, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(tocs.TryGetValue(packageName, out var toc) ? toc : null);

		public ValueTask<byte[]?> GetPackageResourceAsync(string packageName, ulong resourceOffset, uint resourceSize, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(resources.TryGetValue(packageName, out var data) && data.TryGetValue((resourceOffset, resourceSize), out var payload) ? payload : null);

		public ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<IReadOnlyList<string>>(tocs.Keys.ToArray());
	}
}
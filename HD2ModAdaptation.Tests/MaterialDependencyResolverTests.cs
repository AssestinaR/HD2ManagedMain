using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies self-contained material texture closure resolution.
public sealed class MaterialDependencyResolverTests : IDisposable
{
	private const ulong MaterialId = 0x1111111111111111;
	private const ulong TextureId = 0x2222222222222222;
	private readonly string root = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));

	[Fact]
	public void MaterialReader_ReadsTextureIdsAfterTextureRecordTable()
	{
		var data = CreateMaterialPayload(TextureId, 0x3333333333333333);

		var ids = new StingrayMaterialReferenceReader().ReadTextureIds(data);

		Assert.Equal(new[] { TextureId, 0x3333333333333333ul }, ids);
	}

	[Fact]
	public async Task ResolveAsync_PrefersSourcePatchPayloads()
	{
		var source = CreatePatchEntries(new Dictionary<AssetKey, byte[]>
		{
			[MaterialKey] = CreateMaterialPayload(TextureId),
			[TextureKey] = new byte[] { 1, 2, 3 },
		});
		var game = new FakePackageResolver();

		var result = await CreateResolver(game).ResolveAsync(new[] { MaterialId }, source, "unused", new Dictionary<AssetKey, IReadOnlyList<string>>());

		Assert.Empty(result.RejectedMaterialReasons);
		Assert.Equal(2, result.Entries.Count);
		Assert.All(result.Origins.Values, origin => Assert.Equal(MaterialDependencyOriginKind.SourcePatch, origin.Kind));
		Assert.Empty(game.TocRequests);
	}

	[Fact]
	public async Task ResolveAsync_UsesPreferredArchiveBeforeGlobalFallback()
	{
		var preferred = "preferred";
		var fallback = "fallback";
		var game = new FakePackageResolver();
		game.AddPackage(preferred, new Dictionary<AssetKey, byte[]>
		{
			[MaterialKey] = CreateMaterialPayload(TextureId),
			[TextureKey] = new byte[] { 8, 8 },
		});
		game.AddPackage(fallback, new Dictionary<AssetKey, byte[]>
		{
			[MaterialKey] = CreateMaterialPayload(TextureId),
			[TextureKey] = new byte[] { 9, 9 },
		});
		var preferredArchives = new Dictionary<AssetKey, IReadOnlyList<string>>
		{
			[MaterialKey] = new[] { preferred },
			[TextureKey] = new[] { preferred },
		};

		var result = await CreateResolver(game).ResolveAsync(new[] { MaterialId }, Array.Empty<PatchTocEntry>(), "unused", preferredArchives);

		Assert.Empty(result.RejectedMaterialReasons);
		Assert.Equal(new byte[] { 8, 8 }, Assert.Single(result.Entries, entry => entry.AssetKey == TextureKey).TocData);
		Assert.All(result.Origins.Values, origin => Assert.Equal(preferred, origin.Name));
	}

	[Fact]
	public async Task ResolveAsync_UsesGlobalArchivesAndRejectsMissingTexture()
	{
		var game = new FakePackageResolver();
		game.AddPackage("global", new Dictionary<AssetKey, byte[]> { [MaterialKey] = CreateMaterialPayload(TextureId) });

		var result = await CreateResolver(game).ResolveAsync(new[] { MaterialId }, Array.Empty<PatchTocEntry>(), "unused", new Dictionary<AssetKey, IReadOnlyList<string>>());

		Assert.Equal("Missing texture entries: 0x2222222222222222.", result.RejectedMaterialReasons[MaterialId]);
		Assert.Empty(result.TextureIdsByMaterial);
		Assert.Empty(result.Entries);
		Assert.Contains("global", game.TocRequests);
	}

	private static AssetKey MaterialKey => new(MaterialDependencyResolver.MaterialTypeId, MaterialId);
	private static AssetKey TextureKey => new(MaterialDependencyResolver.TextureTypeId, TextureId);
	private static MaterialDependencyResolver CreateResolver(FakePackageResolver resolver) => new(gameResolverFactory: _ => resolver);

	private IReadOnlyList<PatchTocEntry> CreatePatchEntries(IReadOnlyDictionary<AssetKey, byte[]> payloads)
	{
		Directory.CreateDirectory(root);
		var entries = new List<PatchTocEntry>();
		foreach (var pair in payloads)
		{
			var path = Path.Combine(root, $"{pair.Key.FileId:x16}");
			File.WriteAllBytes(path, pair.Value);
			entries.Add(new PatchTocEntry(pair.Key, path, Path.GetFileName(path), 0, TocDataSize: (uint)pair.Value.Length));
		}
		return entries;
	}

	public void Dispose()
	{
		if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
	}

	private static byte[] CreateMaterialPayload(params ulong[] textureIds)
	{
		var data = new byte[136 + textureIds.Length * 4 + textureIds.Length * 8];
		Write32(data, 64, (uint)textureIds.Length); Write32(data, 104, 0);
		for (var index = 0; index < textureIds.Length; index++) Write64(data, 136 + textureIds.Length * 4 + index * 8, textureIds[index]);
		return data;
	}

	private static byte[] CreateToc(IReadOnlyDictionary<AssetKey, byte[]> entries, out Dictionary<(ulong Offset, uint Size), byte[]> resources)
	{
		const int entryOffset = 60;
		var data = new byte[entryOffset + entries.Count * 80]; resources = new();
		Write32(data, 0, 4026531857); Write32(data, 8, (uint)entries.Count);
		var offset = 0UL;
		foreach (var (key, payload) in entries)
		{
			var entry = entryOffset + resources.Count * 80;
			Write64(data, entry, key.FileId); Write64(data, entry + 8, key.TypeId); Write64(data, entry + 16, offset); Write32(data, entry + 56, (uint)payload.Length); Write32(data, entry + 76, (uint)(resources.Count + 1));
			resources[(offset, (uint)payload.Length)] = payload; offset += (uint)payload.Length;
		}
		return data;
	}

	private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void Write64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);

	private sealed class FakePackageResolver : IGameDataPackageResolver
	{
		private readonly Dictionary<string, GameDataPackageToc> tocs = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Dictionary<(ulong Offset, uint Size), byte[]>> resources = new(StringComparer.OrdinalIgnoreCase);
		public List<string> TocRequests { get; } = new();

		public void AddPackage(string name, IReadOnlyDictionary<AssetKey, byte[]> entries)
		{
			tocs[name] = new GameDataPackageToc(CreateToc(entries, out var data), false);
			resources[name] = data;
		}

		public ValueTask<GameDataPackageToc?> GetPackageTocAsync(string packageName, CancellationToken cancellationToken = default)
		{
			TocRequests.Add(packageName); return ValueTask.FromResult(tocs.TryGetValue(packageName, out var toc) ? toc : null);
		}

		public ValueTask<byte[]?> GetPackageResourceAsync(string packageName, ulong resourceOffset, uint resourceSize, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(resources.TryGetValue(packageName, out var data) && data.TryGetValue((resourceOffset, resourceSize), out var payload) ? payload : null);

		public ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<string>>(tocs.Keys.ToArray());
	}
}
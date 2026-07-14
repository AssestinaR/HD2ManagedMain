using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies product-level payload-preserving material split and explicit material-winner merge workflows.
public sealed class MaterialPackagingServiceTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));
	private static readonly AssetKey Unit = new(PatchUnitMeshReader.UnitTypeId, 1);
	private static readonly AssetKey Material = new(MaterialDependencyResolver.MaterialTypeId, 2);
	private static readonly AssetKey Texture = new(MaterialDependencyResolver.TextureTypeId, 3);

	[Fact]
	public async Task SplitAsync_PreservesPayloadsAndGraph()
	{
		var source = CreatePatch("source.patch_0", new Dictionary<AssetKey, Payload>
		{
			[Unit] = new(CreateUnitPayload(Material.FileId), [], [1]),
			[Material] = new(CreateMaterialPayload(Texture.FileId), [], []),
			[Texture] = new([4, 5], [6], [7, 8]),
		});
		var service = new MaterialPackagingService();

		var plan = await service.PlanSplitAsync(source);
		var result = await service.SplitAsync(source, Path.Combine(root, "model"), Path.Combine(root, "material"));

		Assert.True(plan.IsApproved);
		Assert.Equal([Unit], plan.ModelAssetKeys);
		Assert.Equal(new HashSet<AssetKey> { Material, Texture }, plan.MaterialAssetKeys);
		Assert.True(result.Verification.IsSuccessful);
		Assert.Equal(3, result.Verification.ActualAssetCount);
	}

	[Fact]
	public async Task MergeAsync_UsesCompleteCandidateMaterialWinner()
	{
		var oldTexture = new AssetKey(MaterialDependencyResolver.TextureTypeId, 4);
		var source = CreatePatch("model.patch_0", new Dictionary<AssetKey, Payload>
		{
			[Unit] = new(CreateUnitPayload(Material.FileId), [], [1]),
			[Material] = new(CreateMaterialPayload(oldTexture.FileId), [], []),
			[oldTexture] = new([8], [], [8]),
		});
		var candidate = CreatePatch("candidate.patch_0", new Dictionary<AssetKey, Payload>
		{
			[Material] = new(CreateMaterialPayload(Texture.FileId), [], []),
			[Texture] = new([9], [], [10]),
		});
		var service = new MaterialPackagingService();

		var compatibility = await service.CheckCandidateAsync(source, candidate, requireAllExternalMaterials: true);
		var result = await service.MergeAsync(source, candidate, Path.Combine(root, "merged"), requireAllExternalMaterials: true);

		Assert.True(compatibility.IsCompatible);
		Assert.Equal([Material], compatibility.MatchingMaterialAssetKeys);
		Assert.True(result.Verification.IsSuccessful);
		Assert.Equal(3, result.Verification.ActualAssetCount);
		var outputEntries = await new PatchTocScanner().ScanEntriesAsync(result.Outputs.Single().TocFilePath);
		Assert.DoesNotContain(outputEntries, entry => entry.AssetKey == oldTexture);
	}

	[Fact]
	public async Task MergeAsync_AllowsCandidateWithExternalTextureReminder()
	{
		var missingTexture = new AssetKey(MaterialDependencyResolver.TextureTypeId, 0x99);
		var source = CreatePatch("external-model.patch_0", new Dictionary<AssetKey, Payload>
		{
			[Unit] = new(CreateUnitPayload(Material.FileId), [], []),
		});
		var candidate = CreatePatch("partial-candidate.patch_0", new Dictionary<AssetKey, Payload>
		{
			[Material] = new(CreateMaterialPayload(missingTexture.FileId), [], []),
		});
		var service = new MaterialPackagingService();

		var compatibility = await service.CheckCandidateAsync(source, candidate, requireAllExternalMaterials: true);
		var result = await service.MergeAsync(source, candidate, Path.Combine(root, "external-merged"), requireAllExternalMaterials: true);

		Assert.True(compatibility.IsCompatible);
		Assert.Contains(compatibility.Blockers, notice => notice.Contains("Texture", StringComparison.Ordinal));
		Assert.True(result.Verification.IsSuccessful);
		Assert.Equal(2, result.Verification.ActualAssetCount);
	}

	public void Dispose()
	{
		if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
	}

	private string CreatePatch(string name, IReadOnlyDictionary<AssetKey, Payload> entries)
	{
		Directory.CreateDirectory(root);
		var path = Path.Combine(root, name);
		const int typeOffset = 60;
		var types = entries.Keys.Select(key => key.TypeId).Distinct().ToArray();
		var entryOffset = typeOffset + types.Length * 32;
		var payloadOffset = entryOffset + entries.Count * 80;
		var toc = new byte[payloadOffset + entries.Sum(entry => entry.Value.Toc.Length)];
		Write32(toc, 0, 0xf0000011); Write32(toc, 4, (uint)types.Length); Write32(toc, 8, (uint)entries.Count);
		for (var index = 0; index < types.Length; index++) { Write64(toc, typeOffset + index * 32 + 8, types[index]); Write64(toc, typeOffset + index * 32 + 16, (ulong)entries.Count(entry => entry.Key.TypeId == types[index])); }
		using var stream = new MemoryStream(); using var gpu = new MemoryStream();
		var cursor = payloadOffset;
		foreach (var (pair, index) in entries.Select((pair, index) => (pair, index)))
		{
			var record = entryOffset + index * 80;
			Write64(toc, record, pair.Key.FileId); Write64(toc, record + 8, pair.Key.TypeId); Write64(toc, record + 16, (ulong)cursor); Write64(toc, record + 24, (ulong)stream.Position); Write64(toc, record + 32, (ulong)gpu.Position);
			Write32(toc, record + 56, (uint)pair.Value.Toc.Length); Write32(toc, record + 60, (uint)pair.Value.Stream.Length); Write32(toc, record + 64, (uint)pair.Value.Gpu.Length); Write32(toc, record + 76, (uint)(index + 1));
			pair.Value.Toc.CopyTo(toc, cursor); cursor += pair.Value.Toc.Length; stream.Write(pair.Value.Stream); gpu.Write(pair.Value.Gpu);
		}
		File.WriteAllBytes(path, toc); File.WriteAllBytes(path + ".stream", stream.ToArray()); File.WriteAllBytes(path + ".gpu_resources", gpu.ToArray());
		return path;
	}

	private static byte[] CreateUnitPayload(ulong materialId)
	{
		var data = new byte[0x84]; Write32(data, 0x70, 0x74); Write32(data, 0x74, 1); Write32(data, 0x78, 7); Write64(data, 0x7c, materialId); return data;
	}
	private static byte[] CreateMaterialPayload(ulong textureId)
	{
		var data = new byte[148]; Write32(data, 64, 1); Write64(data, 140, textureId); return data;
	}
	private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void Write64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
	private sealed record Payload(byte[] Toc, byte[] Stream, byte[] Gpu);
}
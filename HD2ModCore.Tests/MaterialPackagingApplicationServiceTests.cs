using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using Xunit;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;

namespace HD2ModCore.Tests;

// Purpose: Verifies Core library-node coordination for safe material candidates and package output.
public sealed class MaterialPackagingApplicationServiceTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "HD2ModCoreTests", Guid.NewGuid().ToString("N"));
	private static readonly AdaptationAssetKey Unit = new(HD2ModAdaptation.PatchReconstruction.UnitMesh.PatchUnitMeshReader.UnitTypeId, 1);
	private static readonly AdaptationAssetKey Material = new(MaterialDependencyResolver.MaterialTypeId, 2);
	private static readonly AdaptationAssetKey Texture = new(MaterialDependencyResolver.TextureTypeId, 3);

	[Fact]
	public async Task FindCandidatesAsync_ReturnsCompleteExactMaterialMatch()
	{
		var source = CreateNode("source", "模型");
		var candidate = CreateNode("candidate", "材质");
		CreatePatch(source.RelativePath, new Dictionary<AdaptationAssetKey, byte[]> { [Unit] = CreateUnitPayload(Material.FileId) });
		CreatePatch(candidate.RelativePath, new Dictionary<AdaptationAssetKey, byte[]> { [Material] = CreateMaterialPayload(Texture.FileId), [Texture] = [1] });
		var service = new MaterialPackagingApplicationService(new PatchFileNameParser());

		var result = await service.FindCandidatesAsync(source, [source, candidate], root, requireAllExternalMaterials: true);

		var match = Assert.Single(result);
		Assert.True(match.IsCompatible);
		Assert.Equal(1, match.MatchingMaterialCount);
	}

	[Fact]
	public async Task SplitAsync_WritesTwoVerifiedFlatOutputs()
	{
		var source = CreateNode("embedded", "内嵌模型");
		CreatePatch(source.RelativePath, new Dictionary<AdaptationAssetKey, byte[]> { [Unit] = CreateUnitPayload(Material.FileId), [Material] = CreateMaterialPayload(Texture.FileId), [Texture] = [1, 2] });
		var service = new MaterialPackagingApplicationService(new PatchFileNameParser());

		var result = await service.SplitAsync(source, root, Path.Combine(root, "out"));

		Assert.True(result.IsSuccessful);
		Assert.Equal(2, result.OutputDirectories.Count);
		Assert.Equal(3, result.AssetCount);
	}

	public void Dispose()
	{
		if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
	}

	private ModNode CreateNode(string relativePath, string name) => new(ModNodeId.New(), relativePath, new ModNodeMetadata(name, null, DateTimeOffset.UtcNow, null), [], []);

	private void CreatePatch(string relativePath, IReadOnlyDictionary<AdaptationAssetKey, byte[]> entries)
	{
		var directory = Path.Combine(root, relativePath); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "9ba626afa44a3aa3.patch_0");
		const int typeOffset = 60; var types = entries.Keys.Select(key => key.TypeId).Distinct().ToArray(); var entryOffset = typeOffset + types.Length * 32; var payloadOffset = entryOffset + entries.Count * 80; var data = new byte[payloadOffset + entries.Sum(entry => entry.Value.Length)];
		Write32(data, 0, 0xf0000011); Write32(data, 4, (uint)types.Length); Write32(data, 8, (uint)entries.Count);
		for (var index = 0; index < types.Length; index++) { Write64(data, typeOffset + index * 32 + 8, types[index]); Write64(data, typeOffset + index * 32 + 16, (ulong)entries.Count(entry => entry.Key.TypeId == types[index])); }
		var cursor = payloadOffset;
		foreach (var (pair, index) in entries.Select((pair, index) => (pair, index))) { var record = entryOffset + index * 80; Write64(data, record, pair.Key.FileId); Write64(data, record + 8, pair.Key.TypeId); Write64(data, record + 16, (ulong)cursor); Write32(data, record + 56, (uint)pair.Value.Length); Write32(data, record + 76, (uint)(index + 1)); pair.Value.CopyTo(data, cursor); cursor += pair.Value.Length; }
		File.WriteAllBytes(path, data); File.WriteAllBytes(path + ".stream", []); File.WriteAllBytes(path + ".gpu_resources", []);
	}

	private static byte[] CreateUnitPayload(ulong materialId) { var data = new byte[0x84]; Write32(data, 0x70, 0x74); Write32(data, 0x74, 1); Write32(data, 0x78, 7); Write64(data, 0x7c, materialId); return data; }
	private static byte[] CreateMaterialPayload(ulong textureId) { var data = new byte[148]; Write32(data, 64, 1); Write64(data, 140, textureId); return data; }
	private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void Write64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
}
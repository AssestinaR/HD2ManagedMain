using System.Buffers.Binary;
using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies the first read-only patch-group analysis contract and its TOC projection.
public sealed class PatchGroupAnalyzerTests
{
	[Fact]
	public async Task AnalyzeAsync_ProjectsAssetKindsFromCanonicalToc()
	{
		var directory = CreateTemporaryDirectory();
		try
		{
			var tocPath = Path.Combine(directory, "sample.patch_0");
			await File.WriteAllBytesAsync(tocPath, CreateToc(
				new AssetKey(PatchUnitMeshReader.UnitTypeId, 1),
				new AssetKey(PatchUnitMeshReader.CompositeUnitTypeId, 2),
				new AssetKey(MaterialDependencyResolver.MaterialTypeId, 3),
				new AssetKey(MaterialDependencyResolver.TextureTypeId, 4)));

			var result = await new PatchGroupAnalyzer().AnalyzeAsync(new PatchGroupInput(tocPath));

			Assert.True(result.IsSuccessful);
			Assert.Empty(result.Issues);
			Assert.Equal(4, result.Assets.Count);
			Assert.Single(result.Assets, asset => asset.IsUnit);
			Assert.Single(result.Assets, asset => asset.IsCompositeUnit);
			Assert.Single(result.Assets, asset => asset.IsMaterial);
			Assert.Single(result.Assets, asset => asset.IsTexture);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public async Task AnalyzeAsync_MissingTocReturnsStructuredIssue()
	{
		var result = await new PatchGroupAnalyzer().AnalyzeAsync(new PatchGroupInput(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".patch_0")));

		Assert.False(result.IsSuccessful);
		var issue = Assert.Single(result.Issues);
		Assert.Equal("MissingToc", issue.Code);
		Assert.Empty(result.Assets);
	}

	private static string CreateTemporaryDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static byte[] CreateToc(params AssetKey[] keys)
	{
		const int entryOffset = 60;
		var data = new byte[entryOffset + keys.Length * 80];
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 0xf0000011);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8, 4), (uint)keys.Length);
		for (var index = 0; index < keys.Length; index++)
		{
			var offset = entryOffset + index * 80;
			BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), keys[index].FileId);
			BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset + 8, 8), keys[index].TypeId);
			BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 56, 4), 1);
		}
		return data;
	}
}

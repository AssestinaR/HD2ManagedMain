using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies the cross-analysis snapshot and its conservative missing-input behavior.
public sealed class AdaptationAnalysisBuilderTests
{
	[Fact]
	public async Task BuildAsync_CombinesPatchItemsAndReuseFacts()
	{
		var directory = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			var tocPath = Path.Combine(directory, "sample.patch_0");
			await File.WriteAllBytesAsync(tocPath, CreateToc(new AssetKey(PatchUnitMeshReader.UnitTypeId, 1)));
			var unit = new AssetKey(PatchUnitMeshReader.UnitTypeId, 1);
			var index = CreateIndex("armor.archive", unit);
			var input = new AdaptationAnalysisInput(index, new[] { new PatchGroupInput(tocPath) }, new[]
			{
				new GameItemInput("Armor A", "Armor", new[] { "armor.archive" }, CandidateUnitAssets: new[] { unit }),
				new GameItemInput("Armor B", "Armor", new[] { "armor.archive" }, CandidateUnitAssets: new[] { unit })
			}, "fingerprint");

			var result = await new AdaptationAnalysisBuilder().BuildAsync(input);

			Assert.Single(result.PatchGroups);
			Assert.Equal(2, result.Items.Count);
			Assert.Contains(result.ReuseGroups, group => group.SharedAsset == unit && group.Level == ResourceReuseLevel.ExactUnitReuse);
		Assert.Contains(result.ReplacementFindings, finding => finding.SharedAsset == unit);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task BuildAsync_ReportsMissingGameDataIndexWithoutGuessing()
	{
		var result = await new AdaptationAnalysisBuilder().BuildAsync(new AdaptationAnalysisInput(
			null,
			Array.Empty<PatchGroupInput>(),
			new[] { new GameItemInput("Armor", "Armor", Array.Empty<string>()) },
			"fingerprint"));

		Assert.Empty(result.Items);
		Assert.Contains(result.Issues, issue => issue.Code == "MissingGameDataIndex");
		Assert.Empty(result.ReuseGroups);
	}

	private static GameDataArchiveIndex CreateIndex(string packageName, params AssetKey[] keys)
	{
		var entries = keys.Select((key, index) => new GameDataArchiveEntryFact(key, packageName, (uint)index, 0, 0, 0, 24, 0, 0, 0, 0, 0, 0)).ToArray();
		var archive = new GameDataArchiveFact(packageName, null, null, null, false, entries, Array.Empty<PatchAnalysisIssue>());
		return new GameDataArchiveIndex(new GameDataArchiveInput("."), new[] { archive }, Array.Empty<GameDataStreamLayoutFact>(), Array.Empty<PatchAnalysisIssue>(), DateTimeOffset.UtcNow, "test", "test");
	}

	private static byte[] CreateToc(params AssetKey[] keys)
	{
		const int entryOffset = 60;
		var data = new byte[entryOffset + keys.Length * 80];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 0xf0000011);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8, 4), (uint)keys.Length);
		for (var index = 0; index < keys.Length; index++)
		{
			var offset = entryOffset + index * 80;
			System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), keys[index].FileId);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset + 8, 8), keys[index].TypeId);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 56, 4), 1);
		}
		return data;
	}
}
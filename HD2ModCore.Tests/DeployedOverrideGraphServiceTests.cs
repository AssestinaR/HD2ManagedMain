using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies actual deployment graphs reconcile Data with activation state and compute winners by target patch index.
public sealed class DeployedOverrideGraphServiceTests
{
	[Fact]
	public async Task BuildAsync_ComputesActualWinnerUsingTargetPatchIndex()
	{
		var root = CreateTempRoot();
		try
		{
			var gameData = Path.Combine(root, "data");
			var sourceDirectory = Path.Combine(root, "mods");
			Directory.CreateDirectory(gameData);
			Directory.CreateDirectory(sourceDirectory);
			var key = new AssetKey(10, 20);
			var sourceA = Path.Combine(sourceDirectory, "aaaaaaaaaaaaaaaa.patch_7");
			var sourceB = Path.Combine(sourceDirectory, "aaaaaaaaaaaaaaaa.patch_2");
			await File.WriteAllBytesAsync(sourceA, BuildToc([key]));
			await File.WriteAllBytesAsync(sourceB, BuildToc([key]));
			var nodeA = ModNodeId.New();
			var nodeB = ModNodeId.New();
			var plan = new ApplyPlan(gameData, ProfileId.New(), 5, DateTimeOffset.UtcNow,
			[
				new ApplyOperation(ApplyOperationKind.DeployPatch, Path.Combine(gameData, "aaaaaaaaaaaaaaaa.patch_0"), sourceA, "aaaaaaaaaaaaaaaa", 7, 0, PatchSidecarKind.Base, nodeA),
				new ApplyOperation(ApplyOperationKind.DeployPatch, Path.Combine(gameData, "aaaaaaaaaaaaaaaa.patch_1"), sourceB, "aaaaaaaaaaaaaaaa", 2, 1, PatchSidecarKind.Base, nodeB),
			], []);
			Assert.True((await new ApplyExecutor().ExecuteAsync(plan)).Success);
			var graph = await new DeployedOverrideGraphService(new JsonActivationStateStore(), new PatchFileNameParser()).BuildAsync(gameData);

			var chain = Assert.Single(graph.AssetChains);
			Assert.Equal(key, chain.AssetKey);
			Assert.Equal(nodeB, chain.Winner.NodeId);
			Assert.Equal(1, chain.Winner.TargetPatchIndex);
			Assert.DoesNotContain(graph.Issues, issue => issue.Severity == CoreIssueSeverity.Error);
		}
		finally
		{
			DeleteQuietly(root);
		}
	}

	[Fact]
	public async Task BuildAsync_ReportsMissingRecordedAndUntrackedDataFiles()
	{
		var root = CreateTempRoot();
		try
		{
			var gameData = Path.Combine(root, "data");
			Directory.CreateDirectory(gameData);
			var missing = Path.Combine(gameData, "aaaaaaaaaaaaaaaa.patch_0");
			var extra = Path.Combine(gameData, "bbbbbbbbbbbbbbbb.patch_0");
			await File.WriteAllBytesAsync(extra, BuildToc([]));
			var state = new ActivationState(2, ProfileId.New(), 1, DateTimeOffset.UtcNow, true,
			[
				new ActivationStateFileEntry(missing, Path.Combine(root, "source.patch"), DeploymentMethod.Copy, "aaaaaaaaaaaaaaaa", 0, 0, PatchSidecarKind.Base, ModNodeId.New(), 0, "deadbeef"),
			], []);
			await new JsonActivationStateStore().SaveAsync(gameData, state);

			var graph = await new DeployedOverrideGraphService(new JsonActivationStateStore(), new PatchFileNameParser()).BuildAsync(gameData);

			Assert.Contains(graph.Issues, issue => issue.Code == "RecordedTargetMissing");
			Assert.Contains(graph.Issues, issue => issue.Code == "UntrackedDataPatch");
		}
		finally
		{
			DeleteQuietly(root);
		}
	}

	private static string CreateTempRoot()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-deployed-graph-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		return root;
	}

	private static byte[] BuildToc(IReadOnlyList<AssetKey> entries)
	{
		const uint magic = 4026531857;
		var offset = 60;
		var buffer = new byte[offset + entries.Count * 80];
		WriteUInt32(buffer, 0, magic);
		WriteUInt32(buffer, 4, 0);
		WriteUInt32(buffer, 8, (uint)entries.Count);
		foreach (var entry in entries)
		{
			WriteUInt64(buffer, offset, entry.FileId);
			WriteUInt64(buffer, offset + 8, entry.TypeId);
			offset += 80;
		}
		return buffer;
	}

	private static void WriteUInt32(byte[] buffer, int offset, uint value)
	{
		for (var index = 0; index < 4; index++) buffer[offset + index] = (byte)(value >> (index * 8));
	}

	private static void WriteUInt64(byte[] buffer, int offset, ulong value)
	{
		for (var index = 0; index < 8; index++) buffer[offset + index] = (byte)(value >> (index * 8));
	}

	private static void DeleteQuietly(string root)
	{
		try { Directory.Delete(root, recursive: true); } catch { }
	}
}

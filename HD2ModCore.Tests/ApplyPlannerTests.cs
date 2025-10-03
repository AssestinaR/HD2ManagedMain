using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 ApplyPlanner 会基于真实索引按 Profile 顺序和 hex 分组生成连续目标编号。
// Purpose: Verifies ApplyPlanner builds continuous target numbering from the real index by profile order and archive hex.
public sealed class ApplyPlannerTests
{
	[Fact]
	public async Task BuildPlanAsync_RenumbersPatchFiles_PerArchiveHex()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var modsRoot = Path.Combine(root, "mods");
		var gameData = Path.Combine(root, "game", "data");
		Directory.CreateDirectory(modsRoot);
		Directory.CreateDirectory(gameData);

		try
		{
			File.WriteAllText(Path.Combine(gameData, "9ba626afa44a3aa3.patch_9"), "foreign");
			var firstDir = Path.Combine(modsRoot, "first");
			var secondDir = Path.Combine(modsRoot, "second");
			Directory.CreateDirectory(firstDir);
			Directory.CreateDirectory(secondDir);
			File.WriteAllText(Path.Combine(firstDir, "9ba626afa44a3aa3.patch_3"), "a");
			File.WriteAllText(Path.Combine(firstDir, "9ba626afa44a3aa3.patch_3.stream"), "as");
			File.WriteAllText(Path.Combine(secondDir, "9ba626afa44a3aa3.patch_7"), "b");

			var firstId = ModNodeId.New();
			var secondId = ModNodeId.New();
			var first = new ModNode(
				Id: firstId,
				RelativePath: "first",
				Metadata: new ModNodeMetadata("first", null, Array.Empty<string>(), null, DateTimeOffset.UtcNow, null),
				PatchGroups: Array.Empty<PatchGroupKey>(),
				Children: Array.Empty<ModNodeId>());
			var second = first with { Id = secondId, RelativePath = "second", Metadata = first.Metadata with { Name = "second" } };

			var snapshot = new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode> { [firstId] = first, [secondId] = second }, Array.Empty<Profile>());
			var index = await new PatchFileIndexBuilder(new PatchFileNameParser()).BuildAsync(snapshot, modsRoot);
			var profile = new Profile(ProfileId.New(), "p", DateTimeOffset.UtcNow, null, new[]
			{
				new ProfileEntry(secondId, LoadOrder: -1, Enabled: true, AddedUtc: DateTimeOffset.UtcNow),
				new ProfileEntry(firstId, LoadOrder: 0, Enabled: true, AddedUtc: DateTimeOffset.UtcNow.AddSeconds(1)),
			});

			var planner = new ApplyPlanner(new PatchFileNameParser());
			var plan = await planner.BuildPlanAsync(profile, snapshot, index, gameData);

			Assert.Equal(Path.GetFullPath(gameData), plan.GameDataDirectory);
			Assert.Contains(plan.Operations, o => o.Kind == ApplyOperationKind.DeletePatch && o.TargetPath.EndsWith("9ba626afa44a3aa3.patch_9"));
			var deploys = plan.Operations.Where(o => o.Kind == ApplyOperationKind.DeployPatch).ToList();
			Assert.Equal(new int?[] { 0, 1, 1 }, deploys.Select(o => o.TargetPatchIndex).ToArray());
			Assert.Contains(deploys, o => o.NodeId == secondId && o.TargetPath.EndsWith("9ba626afa44a3aa3.patch_0"));
			Assert.Contains(deploys, o => o.NodeId == firstId && o.TargetPath.EndsWith("9ba626afa44a3aa3.patch_1"));
			Assert.Contains(deploys, o => o.NodeId == firstId && o.TargetPath.EndsWith("9ba626afa44a3aa3.patch_1.stream"));
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}

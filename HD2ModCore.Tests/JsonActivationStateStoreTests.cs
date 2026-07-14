using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies the public activation-state contract is atomically persisted and removed.
public sealed class JsonActivationStateStoreTests
{
	[Fact]
	public async Task SaveLoadDelete_RoundtripsPublicState()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-activation-state-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var source = Path.Combine(root, "source.patch_4");
			var target = Path.Combine(root, "0123456789abcdef.patch_0");
			var profileId = ProfileId.New();
			var nodeId = ModNodeId.New();
			var state = new ActivationState(2, profileId, 7, DateTimeOffset.UtcNow, true,
			[
				new ActivationStateFileEntry(target, source, DeploymentMethod.Copy, "0123456789abcdef", 4, 0, PatchSidecarKind.Base, nodeId, 12, "abc"),
			], []);
			var store = new JsonActivationStateStore();

			await store.SaveAsync(root, state);
			var loaded = await store.TryLoadAsync(root);

			Assert.NotNull(loaded);
			Assert.Equal(profileId, loaded!.ProfileId);
			Assert.Equal(7, loaded.ProfileRevision);
			Assert.Equal(4, Assert.Single(loaded.Files).SourcePatchIndex);
			Assert.False(File.Exists(Path.Combine(root, JsonActivationStateStore.StateFileName + ".tmp")));
			await store.DeleteAsync(root);
			Assert.Null(await store.TryLoadAsync(root));
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}

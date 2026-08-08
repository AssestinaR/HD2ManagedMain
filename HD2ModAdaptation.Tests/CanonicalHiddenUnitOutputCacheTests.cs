using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

public sealed class CanonicalHiddenUnitOutputCacheTests : IDisposable
{
	private readonly string directory = Path.Combine(Path.GetTempPath(), "hd2-hidden-unit-cache-tests", Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task Cache_RoundTripsOutputAndInvalidatesWhenFingerprintChanges()
	{
		var cache = new CanonicalHiddenUnitOutputCache(directory);
		var key = new AssetKey(0x11, 0x22);
		var entry = new CanonicalPatchSessionEntry(key, CanonicalPatchEntryOwnership.TargetOutput, [1, 2], [3, 4], Array.Empty<byte>(), 5, 6, 7, 8);

		await cache.InitializeAsync("game-v1", gameDataIndexIsCurrent: true);
		await cache.StoreAsync("armor.pack", new CanonicalHiddenUnitOutput(entry, 9));
		var restored = await cache.TryReadAsync("armor.pack", key);

		Assert.NotNull(restored);
		Assert.Equal(9, restored.HiddenMeshCount);
		Assert.Equal(entry.EffectiveTocData, restored.Entry.EffectiveTocData);
		Assert.Equal(entry.EffectiveGpuData, restored.Entry.EffectiveGpuData);
		Assert.Equal(entry.Unknown1, restored.Entry.Unknown1);

		await cache.InitializeAsync("game-v2", gameDataIndexIsCurrent: true);
		Assert.Null(await cache.TryReadAsync("armor.pack", key));
	}

	[Fact]
	public async Task Cache_RemovesAllEntriesWhenIndexIsStale()
	{
		var cache = new CanonicalHiddenUnitOutputCache(directory);
		await cache.InitializeAsync("game-v1", gameDataIndexIsCurrent: true);
		await cache.InitializeAsync("game-v1", gameDataIndexIsCurrent: false);

		Assert.False(Directory.Exists(directory));
	}

	public void Dispose()
	{
		if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
	}
}

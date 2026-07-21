using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies lightweight asset facts and full model analysis persist independently.
public sealed class AdvancedModAnalysisCacheStoreTests
{
    [Fact]
    public async Task LightweightAndAdvancedSnapshotsDoNotOverwriteEachOther()
    {
        var root = Path.Combine(Path.GetTempPath(), "hd2-analysis-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var nodeId = ModNodeId.New();
            var store = new SqliteModFactsStore(new StoragePaths(root));
            var lightweight = Entry(7, nodeId, PatchAnalysisDepth.DependencyGraph, "lightweight");
            var advanced = Entry(8, nodeId, PatchAnalysisDepth.Full, "advanced");

            await store.SaveAsync(lightweight);
            await store.SaveAdvancedAsync(advanced);

            Assert.Equal("lightweight", Assert.Single((await store.TryLoadAsync(nodeId))!.Analyses).AnalyzerVersion);
            Assert.Equal("advanced", Assert.Single((await store.TryLoadAdvancedAsync(nodeId))!.Analyses).AnalyzerVersion);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static PatchGroupAnalysisCacheEntry Entry(int version, ModNodeId nodeId, PatchAnalysisDepth depth, string analyzer)
        => new(version, nodeId, "mod", [], DateTimeOffset.UtcNow,
        [
            new PatchGroupAnalysis(
                new PatchGroupInput("0123456789abcdef.patch_0"),
                [],
                [],
                [],
                DateTimeOffset.UtcNow,
                analyzer,
                depth)
        ]);
}

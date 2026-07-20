using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

public sealed class CachedPatchGroupAnalysisProviderTests
{
    [Fact]
    public async Task ReusesCachedFactsUntilPatchGroupSidecarChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "hd2mod-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var node = new ModNode(
                new ModNodeId(Guid.NewGuid()),
                "sample",
                new ModNodeMetadata("Sample", null, DateTimeOffset.UtcNow, null),
                Array.Empty<PatchGroupKey>(),
                Array.Empty<ModNodeId>());
            var nodeDirectory = Path.Combine(root, node.RelativePath);
            Directory.CreateDirectory(nodeDirectory);
            var patchPath = Path.Combine(nodeDirectory, "0123456789abcdef.patch_0");
            var streamPath = patchPath + ".stream";
            var gpuPath = patchPath + ".gpu_resources";
            await File.WriteAllBytesAsync(patchPath, [1]);
            await File.WriteAllBytesAsync(streamPath, [1]);
            await File.WriteAllBytesAsync(gpuPath, [1]);

            var inner = new CountingProvider();
            var cache = new InMemoryCacheStore();
            var provider = new CachedPatchGroupAnalysisProvider(inner, cache, new PatchFileNameParser());

            await provider.AnalyzeNodeAsync(node, root);
            await provider.AnalyzeNodeAsync(node, root);
            Assert.Equal(1, inner.CallCount);

            await File.WriteAllBytesAsync(streamPath, [1, 2]);
            await provider.AnalyzeNodeAsync(node, root);
            Assert.Equal(2, inner.CallCount);

            await File.WriteAllBytesAsync(gpuPath, [1, 2]);
            await provider.AnalyzeNodeAsync(node, root);
            Assert.Equal(3, inner.CallCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CountingProvider : IPatchGroupAnalysisProvider
    {
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<PatchGroupAnalysis>> AnalyzeNodeAsync(
            ModNode node,
            string modsRootDirectory,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult<IReadOnlyList<PatchGroupAnalysis>>(
            [
                new PatchGroupAnalysis(
                    new PatchGroupInput(Path.Combine(modsRootDirectory, node.RelativePath, "0123456789abcdef.patch_0")),
                    Array.Empty<PatchAssetFact>(),
					Array.Empty<PatchAssetReference>(),
                    Array.Empty<PatchAnalysisIssue>(),
                    DateTimeOffset.UtcNow,
                    "patch-group-v6-dependency-graph",
                    PatchAnalysisDepth.DependencyGraph)
            ]);
        }
    }

    private sealed class InMemoryCacheStore : IPatchGroupAnalysisCacheStore
    {
        private PatchGroupAnalysisCacheEntry? _entry;

        public ValueTask<PatchGroupAnalysisCacheEntry?> TryLoadAsync(ModNodeId nodeId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_entry?.NodeId == nodeId ? _entry : null);

        public ValueTask SaveAsync(PatchGroupAnalysisCacheEntry entry, CancellationToken cancellationToken = default)
        {
            _entry = entry;
            return ValueTask.CompletedTask;
        }
    }
}

using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

public sealed class ModFileResolverTests
{
    [Fact]
    public async Task ResolvePatchFilesAsync_ReturnsOnlyBaseTocFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "9ba626afa44a3aa3.patch_0"), "toc");
            File.WriteAllText(Path.Combine(root, "9ba626afa44a3aa3.patch_0.stream"), "stream");
            File.WriteAllText(Path.Combine(root, "9ba626afa44a3aa3.patch_0.gpu_resources"), "gpu");
            var node = new ModNode(ModNodeId.New(), string.Empty, new ModNodeMetadata("test", null, DateTimeOffset.UtcNow, null), [], []);

            var files = await new ModFileResolver(new PatchFileNameParser()).ResolvePatchFilesAsync(node, root);

            Assert.Single(files);
            Assert.EndsWith(".patch_0", files[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ResolvePatchFilesAsync_PrefersValidSameNamedOverwritePatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Overwrite"));
        try
        {
            var name = "9ba626afa44a3aa3.patch_0";
            var original = Path.Combine(root, name);
            var overwrite = Path.Combine(root, "Overwrite", name);
            File.WriteAllText(original, "root toc");
            await using (var output = File.Create(overwrite)) await output.WriteAsync(BitConverter.GetBytes(4026531857u));
            var node = new ModNode(ModNodeId.New(), string.Empty, new ModNodeMetadata("test", null, DateTimeOffset.UtcNow, null), [], []);

            var files = await new ModFileResolver(new PatchFileNameParser()).ResolvePatchFilesAsync(node, root);

            Assert.Equal(overwrite, Assert.Single(files));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

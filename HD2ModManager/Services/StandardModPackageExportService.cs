using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Services;

// Produces a community-compatible package while retaining the manager's per-node identities.
public sealed class StandardModPackageExportService(ModLibraryService library)
{
    private readonly ModLibraryService _library = library;

    public async Task<string> ExportAsync(ExportPackageEntry root, string packageName, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName)) throw new InvalidOperationException("请输入导出包名称。");
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new InvalidOperationException("请选择输出目录。");
        Validate(root);
        var staging = Path.Combine(Path.GetTempPath(), "HD2ModManager_Export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var usedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<ExportPackageEntry, string>();
            paths[root] = string.Empty;
            foreach (var option in root.Children)
            {
                var optionPath = CreateDirectoryName(option.Name, usedDirectories);
                paths[option] = optionPath;
                var usedSubDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var subOption in option.Children)
                    paths[subOption] = Path.Combine(optionPath, CreateDirectoryName(subOption.Name, usedSubDirectories));
            }

            foreach (var pair in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(staging, pair.Value);
                Directory.CreateDirectory(target);
                await CopyEntryAsync(pair.Key, target, cancellationToken).ConfigureAwait(false);
            }

            var manifest = CreateManifest(root, packageName, paths);
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(outputDirectory);
            var zipPath = Path.Combine(outputDirectory, SanitizeFileName(packageName) + ".zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(staging, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return zipPath;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private async Task CopyEntryAsync(ExportPackageEntry entry, string target, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(entry.ModId))
        {
            var mod = _library.Get(entry.ModId) ?? throw new InvalidOperationException($"找不到来源 Mod：{entry.Name}");
            var source = _library.ResolveAbsolutePath(mod.SourcePath);
            if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source)) throw new DirectoryNotFoundException($"来源目录不存在：{mod.Name}");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                await using var input = File.OpenRead(file);
                await using var output = File.Create(Path.Combine(target, name));
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
        if (!string.IsNullOrWhiteSpace(entry.ImagePath) && File.Exists(entry.ImagePath))
        {
            var extension = Path.GetExtension(entry.ImagePath).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp") File.Copy(entry.ImagePath, Path.Combine(target, "icon" + extension), overwrite: true);
        }
    }

    private static void Validate(ExportPackageEntry root)
    {
        foreach (var entry in root.Children.SelectMany(option => option.Children.Append(option)))
            if (entry.Children.Count == 0 && string.IsNullOrWhiteSpace(entry.ModId)) throw new InvalidOperationException($"“{entry.Name}”尚未选择来源 Mod。");
    }

    private static ExportManifest CreateManifest(ExportPackageEntry root, string packageName, IReadOnlyDictionary<ExportPackageEntry, string> paths)
    {
        var rootGuid = Guid.TryParse(root.ModId, out var parsed) ? parsed.ToString("D") : Guid.NewGuid().ToString("D");
        return new ExportManifest
        {
            Version = 1,
            Guid = rootGuid,
            Name = string.IsNullOrWhiteSpace(root.Name) ? packageName : root.Name,
            Description = root.Notes ?? string.Empty,
            IconPath = IconPathFor(paths[root], root.ImagePath),
            Options = root.Children.Select(option => new ExportOption
            {
                Name = option.Name,
                Description = option.Notes ?? string.Empty,
                Image = IconPathFor(paths[option], option.ImagePath),
                Include = string.IsNullOrWhiteSpace(option.ModId) ? [] : [ToManifestPath(paths[option])],
                SubOptions = option.Children.Select(sub => new ExportSubOption
                {
                    Name = sub.Name,
                    Description = sub.Notes ?? string.Empty,
                    Image = IconPathFor(paths[sub], sub.ImagePath),
                    Include = [ToManifestPath(paths[sub])],
                }).ToList(),
            }).ToList(),
            Nodes = paths.Select(pair => new ExportNode { RelativePath = ToManifestPath(pair.Value), Guid = pair.Key.ModId ?? Guid.NewGuid().ToString("D"), Name = pair.Key.Name, Notes = pair.Key.Notes }).ToList(),
        };
    }

    private static string IconPathFor(string path, string? imagePath)
    {
        var extension = Path.GetExtension(imagePath ?? string.Empty).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp" and not ".webp") extension = ".png";
        return string.IsNullOrWhiteSpace(path) ? "icon" + extension : ToManifestPath(Path.Combine(path, "icon" + extension));
    }
    private static string ToManifestPath(string path) => path.Replace('\\', '/');
    private static string CreateDirectoryName(string name, HashSet<string> used) { var baseName = SanitizeFileName(name); if (string.IsNullOrWhiteSpace(baseName)) baseName = "Option"; var value = baseName; var index = 2; while (!used.Add(value)) value = baseName + "_" + index++; return value; }
    private static string SanitizeFileName(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private sealed class ExportManifest { public int Version { get; set; } public string Guid { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public string IconPath { get; set; } = string.Empty; public List<ExportOption> Options { get; set; } = []; public List<ExportNode> Nodes { get; set; } = []; }
    private sealed class ExportOption { public string Name { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public string Image { get; set; } = string.Empty; public List<string> Include { get; set; } = []; public List<ExportSubOption> SubOptions { get; set; } = []; }
    private sealed class ExportSubOption { public string Name { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public string Image { get; set; } = string.Empty; public List<string> Include { get; set; } = []; }
    private sealed class ExportNode { public string RelativePath { get; set; } = string.Empty; public string Guid { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string? Notes { get; set; } }
}

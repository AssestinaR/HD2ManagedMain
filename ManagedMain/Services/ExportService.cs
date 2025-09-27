using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedMain.Models;

namespace ManagedMain.Services
{
    public class ExportService
    {
        private class ExportMain
        {
            public int Version { get; set; }
            public Guid Guid { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string IconPath { get; set; } = string.Empty;
            public List<ExportOption> Options { get; set; } = new();
        }
        private class ExportOption
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<string> Include { get; set; } = new();
            public string Image { get; set; } = string.Empty;
            public List<ExportSub> SubOptions { get; set; } = new();
        }
        private class ExportSub
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<string> Include { get; set; } = new();
            public string Image { get; set; } = string.Empty;
        }

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public string ExportMod(string profileRoot, MainModItem mod, string outputFolder, int version = 1)
        {
            // 1. 准备临时导出目录
            var tempDir = Path.Combine(Path.GetTempPath(), "ManagedMain_Export_", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var modRoot = Path.Combine(profileRoot, mod.Name);
                // 2. 复制文件：文件组 + 图片
                CopyGroups(modRoot, tempDir, mod.FileGroups);

                var mainIconRel = !string.IsNullOrWhiteSpace(mod.IconPath) ? mod.IconPath : (mod.Image ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(mainIconRel))
                {
                    CopyIfExists(Path.Combine(modRoot, mainIconRel), Path.Combine(tempDir, mainIconRel));
                }

                foreach (var opt in mod.Options)
                {
                    CopyGroups(modRoot, tempDir, opt.FileGroups);
                    if (!string.IsNullOrWhiteSpace(opt.Image)) CopyIfExists(Path.Combine(modRoot, opt.Image), Path.Combine(tempDir, opt.Image));
                    foreach (var sub in opt.SubOptions)
                    {
                        CopyGroups(modRoot, tempDir, sub.FileGroups);
                        if (!string.IsNullOrWhiteSpace(sub.Image)) CopyIfExists(Path.Combine(modRoot, sub.Image), Path.Combine(tempDir, sub.Image));
                    }
                }

                // 3. 生成 manifest.json
                var manifest = BuildManifest(mod, version);
                File.WriteAllText(Path.Combine(tempDir, "manifest.json"), JsonSerializer.Serialize(manifest, _jsonOptions));

                // 4. 压缩为 zip
                Directory.CreateDirectory(outputFolder);
                var zipPath = Path.Combine(outputFolder, mod.Name + ".zip");
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false);
                return zipPath;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static ExportMain BuildManifest(MainModItem mod, int version)
        {
            var resolvedIcon = !string.IsNullOrWhiteSpace(mod.IconPath) ? mod.IconPath : (mod.Image ?? string.Empty);
            var em = new ExportMain
            {
                Version = version,
                Guid = mod.Guid,
                Name = mod.Name,
                Description = mod.Description ?? string.Empty,
                IconPath = resolvedIcon,
                Options = new List<ExportOption>()
            };
            foreach (var o in mod.Options)
            {
                var eo = new ExportOption
                {
                    Name = o.Name,
                    Description = o.Description ?? string.Empty,
                    Image = o.Image ?? string.Empty,
                    Include = new List<string> { o.Name },
                    SubOptions = new List<ExportSub>()
                };
                foreach (var s in o.SubOptions)
                {
                    eo.SubOptions.Add(new ExportSub
                    {
                        Name = s.Name,
                        Description = s.Description ?? string.Empty,
                        Image = s.Image ?? string.Empty,
                        Include = new List<string> { o.Name + "/" + s.Name }
                    });
                }
                em.Options.Add(eo);
            }
            return em;
        }

        private static void CopyGroups(string modRoot, string tempDir, IEnumerable<ModFileGroup> groups)
        {
            foreach (var g in groups)
            {
                foreach (var rel in g.Files)
                {
                    var src = Path.Combine(modRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    var dst = Path.Combine(tempDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    CopyIfExists(src, dst);
                }
            }
        }

        private static void CopyIfExists(string src, string dst)
        {
            try
            {
                if (!File.Exists(src)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
            catch { }
        }
    }
}

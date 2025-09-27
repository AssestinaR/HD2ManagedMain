using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ManagedMain.Models;

namespace ManagedMain.Services
{
    public static class FileGroupScanner
    {
        private static readonly string[] ImageExts = new[] { ".png", ".jpg", ".jpeg" };
        private static readonly Regex PatchRegex = new Regex(
            @"([a-fA-F0-9]{16})\.patch_(\d+)(\.stream|\.gpu_resources)?$",
            RegexOptions.Compiled);

        public static List<ModFileGroup> GetModFileGroups(string rootFolder, string currentFolder)
        {
            var groups = new Dictionary<string, ModFileGroup>();
            if (string.IsNullOrWhiteSpace(currentFolder) || !Directory.Exists(currentFolder)) return new List<ModFileGroup>();

            foreach (var file in Directory.GetFiles(currentFolder, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var match = PatchRegex.Match(name);
                if (!match.Success) continue;
                var hex = match.Groups[1].Value;
                var patchN = int.TryParse(match.Groups[2].Value, out var n) ? n : 0;

                string relDir = string.Empty;
                try { relDir = Path.GetRelativePath(rootFolder ?? string.Empty, currentFolder ?? string.Empty).Replace('\\', '/'); }
                catch { relDir = string.Empty; }
                if (relDir == "." || relDir == rootFolder) relDir = string.Empty;

                var relPath = string.IsNullOrEmpty(relDir) ? hex : $"{relDir}/{hex}";
                var key = $"{relPath}.{patchN}";
                if (!groups.TryGetValue(key, out var g))
                {
                    g = new ModFileGroup { RelativePath = relPath, HexPrefix = hex, PatchN = patchN, Files = new List<string>() };
                    groups[key] = g;
                }

                string relFile;
                try { relFile = Path.GetRelativePath(rootFolder ?? string.Empty, file).Replace('\\', '/'); }
                catch { relFile = name; }
                g.Files.Add(relFile);
            }
            return new List<ModFileGroup>(groups.Values);
        }

        public static string? FindImageFile(string dir)
        {
            try
            {
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList();
                if (files.Count == 0) return null;
                if (files.Count == 1) return files[0]!;
                string[] prefs = new[] { "icon", "cover", "logo", "preview", "thumbnail", "thumb" };
                var picked = files.FirstOrDefault(f => prefs.Any(p => Path.GetFileNameWithoutExtension(f!).Contains(p, StringComparison.OrdinalIgnoreCase)));
                return picked ?? files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            }
            catch { return null; }
        }

        public static MainModItem BuildModFromDirectory(string modName, string rootDir)
        {
            var main = new MainModItem
            {
                Name = modName,
                Guid = Guid.NewGuid(),
                Description = null,
                FileGroups = GetModFileGroups(rootDir, rootDir)
            };
            var rootImg = FindImageFile(rootDir);
            if (!string.IsNullOrEmpty(rootImg)) { main.IconPath = rootImg; main.Image = rootImg; }

            foreach (var firstDir in Directory.GetDirectories(rootDir))
            {
                var optionName = Path.GetFileName(firstDir);
                var opt = new OptionItem
                {
                    Name = optionName,
                    Description = null,
                    Image = null,
                    IconPath = null,
                    FileGroups = GetModFileGroups(rootDir, firstDir)
                };
                var optImg = FindImageFile(firstDir);
                if (!string.IsNullOrEmpty(optImg)) { opt.Image = optionName + "/" + optImg; opt.IconPath = opt.Image; }

                foreach (var secondDir in Directory.GetDirectories(firstDir))
                {
                    var subName = Path.GetFileName(secondDir);
                    var sub = new SubOptionItem
                    {
                        Name = subName,
                        Description = null,
                        Image = null,
                        IconPath = null,
                        FileGroups = GetModFileGroups(rootDir, secondDir)
                    };
                    var subImg = FindImageFile(secondDir);
                    if (!string.IsNullOrEmpty(subImg)) { sub.Image = optionName + "/" + subName + "/" + subImg; sub.IconPath = sub.Image; }
                    opt.SubOptions.Add(sub);
                }
                main.Options.Add(opt);
            }
            return main;
        }
    }
}

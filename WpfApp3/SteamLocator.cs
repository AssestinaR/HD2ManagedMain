using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LiberTeaManager
{
    public class SteamGame
    {
        public string AppId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string InstallDir { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? InstallDir : Name;
    }

    public static class SteamLocator
    {
        // Collapse repeated backslashes (except UNC leading \\) & unify separators
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            path = path.Replace('/', Path.DirectorySeparatorChar);
            // Collapse duplicate backslashes after start
            while (path.Contains("\\\\")) path = path.Replace("\\\\", "\\");
            return path.TrimEnd(Path.DirectorySeparatorChar);
        }

        public static string? FindSteamRoot()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\\Valve\\Steam");
                var path = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return NormalizePath(path);
            }
            catch { }
            string[] common =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
            };
            return common.Select(NormalizePath).FirstOrDefault(Directory.Exists);
        }

        public static IEnumerable<string> EnumerateLibraryRoots()
        {
            var root = FindSteamRoot();
            if (root == null) yield break;
            var mainLib = Path.Combine(root, "steamapps");
            if (Directory.Exists(mainLib)) yield return mainLib;
            var libraryFile = Path.Combine(mainLib, "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) yield break;
            string text;
            try { text = File.ReadAllText(libraryFile); } catch { yield break; }
            // New format is pseudo JSON; capture paths after "path" or numeric keys.
            var matches = Regex.Matches(text, "\"path\"\\s*\"(?<p>[^\"]+)\"");
            foreach (Match m in matches)
            {
                var p = NormalizePath(m.Groups["p"].Value);
                if (Directory.Exists(p))
                {
                    var sa = Path.Combine(p, "steamapps");
                    if (Directory.Exists(sa)) yield return sa;
                }
            }
        }

        public static List<SteamGame> GetInstalledGames()
        {
            var list = new List<SteamGame>();
            foreach (var lib in EnumerateLibraryRoots().Distinct())
            {
                try
                {
                    foreach (var manifest in Directory.EnumerateFiles(lib, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var text = File.ReadAllText(manifest);
                            var appId = Regex.Match(text, "\\\"appid\\\"\\s*\\\"(\\d+)\\\"").Groups[1].Value;
                            var name = Regex.Match(text, "\\\"name\\\"\\s*\\\"([^\\\"]+)\\\"").Groups[1].Value;
                            var installdir = Regex.Match(text, "\\\"installdir\\\"\\s*\\\"([^\\\"]+)\\\"").Groups[1].Value;
                            if (string.IsNullOrWhiteSpace(installdir)) continue;
                            var gamePath = NormalizePath(Path.Combine(lib, "common", installdir));
                            if (!Directory.Exists(gamePath)) continue;
                            list.Add(new SteamGame { AppId = appId, Name = name, InstallDir = installdir, Path = gamePath });
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return list.OrderBy(g => g.Name).ToList();
        }

        /// <summary>
        /// 尝试在所有 Steam 库中定位 HELLDIVERS 2 的 Data 目录
        /// </summary>
        public static string? TryFindHelldivers2Data()
        {
            try
            {
                foreach (var lib in EnumerateLibraryRoots())
                {
                    var common = Path.Combine(lib, "common");
                    if (!Directory.Exists(common)) continue;
                    foreach (var dir in Directory.GetDirectories(common))
                    {
                        var name = Path.GetFileName(dir);
                        if (name.Contains("helldivers", StringComparison.OrdinalIgnoreCase))
                        {
                            // Helldivers 2 目录名可能为 HELLDIVERS 2
                            var data = Path.Combine(dir, "data");
                            if (Directory.Exists(data)) return NormalizePath(data);
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}

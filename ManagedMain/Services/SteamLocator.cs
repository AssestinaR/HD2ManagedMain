using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics; // added for logging

namespace ManagedMain.Services
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
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            path = path.Replace('/', Path.DirectorySeparatorChar);
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
            catch (Exception ex)
            {
                Debug.WriteLine("[SteamLocator] ∂¡»°◊¢≤·±Ì SteamPath  ß∞‹: " + ex.Message);
            }
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
            try { text = File.ReadAllText(libraryFile); }
            catch (Exception ex)
            {
                Debug.WriteLine("[SteamLocator] ∂¡»° libraryfolders.vdf  ß∞‹: " + ex.Message); yield break;
            }
            var matches = Regex.Matches(text, "\"path\"\\s*\"(?<p>[^\"]+)\"");
            foreach (Match m in matches.Cast<Match>())
            {
                var p = NormalizePath(m.Groups["p"].Value);
                if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                {
                    var sa = Path.Combine(p, "steamapps");
                    if (Directory.Exists(sa)) yield return sa;
                }
            }
        }

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
                            var data = Path.Combine(dir, "data");
                            if (Directory.Exists(data)) return NormalizePath(data);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SteamLocator] ≤È’“ Helldivers2 data  ß∞‹: " + ex.Message);
            }
            return null;
        }
    }
}

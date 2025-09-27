using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ManagedMain.Models;

namespace ManagedMain.Services
{
    public class ActivationService
    {
        private static readonly Regex PatchRegex = new Regex(
            @"^(?<hex>[a-fA-F0-9]{16})\.patch_(?<n>\d+)(?<tail>(?:\.stream|\.gpu_resources)?)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public record LinkStats(int GroupsTried, int FileSuccess, int FileFailed);

        public int EnableForMain(string profileRoot, string gameFolder, MainModItem main)
        {
            var modRoot = Path.Combine(profileRoot, main.Name);
            var groups = new List<ModFileGroup>();
            groups.AddRange(main.FileGroups);
            foreach (var o in main.Options)
            {
                groups.AddRange(o.FileGroups);
                foreach (var s in o.SubOptions) groups.AddRange(s.FileGroups);
            }
            return EnableGroups(modRoot, gameFolder, groups);
        }

        public int EnableForOption(string profileRoot, string gameFolder, string mainName, OptionItem opt)
        {
            var modRoot = Path.Combine(profileRoot, mainName);
            var groups = new List<ModFileGroup>();
            groups.AddRange(opt.FileGroups);
            foreach (var s in opt.SubOptions) groups.AddRange(s.FileGroups);
            return EnableGroups(modRoot, gameFolder, groups);
        }

        public int EnableForSub(string profileRoot, string gameFolder, string mainName, SubOptionItem sub)
        {
            var modRoot = Path.Combine(profileRoot, mainName);
            return EnableGroups(modRoot, gameFolder, sub.FileGroups);
        }

        public int EnableGroupsForMod(string modRoot, string gameFolder, IEnumerable<ModFileGroup> groups)
        {
            return EnableGroups(modRoot, gameFolder, groups);
        }

        public int DisableByHexes(string gameFolder, IEnumerable<string> hexes)
        {
            Directory.CreateDirectory(gameFolder);
            var set = new HashSet<string>(hexes.Select(h => h.ToLowerInvariant()));
            int removed = 0;
            foreach (var file in Directory.EnumerateFiles(gameFolder, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var m = PatchRegex.Match(name);
                if (!m.Success) continue;
                var hex = m.Groups["hex"].Value.ToLowerInvariant();
                if (set.Contains(hex))
                {
                    try { File.Delete(file); removed++; } catch { }
                }
            }
            return removed;
        }

        public int RemoveAllPatchFiles(string gameFolder)
        {
            Directory.CreateDirectory(gameFolder);
            int removed = 0;
            foreach (var file in Directory.EnumerateFiles(gameFolder, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (PatchRegex.IsMatch(name))
                {
                    try { File.Delete(file); removed++; } catch { }
                }
            }
            return removed;
        }

        public LinkStats NormalizeAndRelinkAll(string profileRoot, string gameFolder, IEnumerable<MainModItem> mods)
        {
            // Build per-hex buckets in UI order of enabled items. Within a single item (main/option/sub)
            // if multiple groups have the same hex, order them by their original PatchN ascending.
            var perHex = new Dictionary<string, List<(string mainName, ModFileGroup group)>>(StringComparer.OrdinalIgnoreCase);
            void AddOrdered(string hex, string mainName, IEnumerable<ModFileGroup> groups)
            {
                if (!perHex.TryGetValue(hex, out var list)) { list = new List<(string, ModFileGroup)>(); perHex[hex] = list; }
                foreach (var g in groups.OrderBy(g => g.PatchN)) list.Add((mainName, g));
            }

            foreach (var m in mods)
            {
                if (IsEnabled(m))
                {
                    foreach (var grp in m.FileGroups.GroupBy(g => g.HexPrefix, StringComparer.OrdinalIgnoreCase))
                        AddOrdered(grp.Key, m.Name, grp);
                }
                foreach (var o in m.Options)
                {
                    if (IsEnabled(o))
                    {
                        foreach (var grp in o.FileGroups.GroupBy(g => g.HexPrefix, StringComparer.OrdinalIgnoreCase))
                            AddOrdered(grp.Key, m.Name, grp);
                    }
                    foreach (var s in o.SubOptions)
                    {
                        if (IsEnabled(s))
                        {
                            foreach (var grp in s.FileGroups.GroupBy(g => g.HexPrefix, StringComparer.OrdinalIgnoreCase))
                                AddOrdered(grp.Key, m.Name, grp);
                        }
                    }
                }
            }

            // Renumber within each hex bucket and rename files in profile folders accordingly
            int groupsTried = 0;
            foreach (var (hex, list) in perHex)
            {
                int n = 0;
                foreach (var (mainName, g) in list)
                {
                    g.PatchN = n;
                    for (int i = 0; i < g.Files.Count; i++)
                    {
                        var oldRel = g.Files[i].Replace('\\', '/');
                        var dirRel = Path.GetDirectoryName(oldRel)?.Replace('\\', '/') ?? string.Empty;
                        var fn = Path.GetFileName(oldRel) ?? string.Empty;
                        var tail = ExtractTail(fn);
                        var newFileName = $"{g.HexPrefix}.patch_{n}{tail}";
                        var newRel = string.IsNullOrEmpty(dirRel) ? newFileName : (dirRel + "/" + newFileName);

                        var modRoot = Path.Combine(profileRoot, mainName);
                        var oldAbs = Path.Combine(modRoot, oldRel.Replace('/', Path.DirectorySeparatorChar));
                        var newAbs = Path.Combine(modRoot, newRel.Replace('/', Path.DirectorySeparatorChar));
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(newAbs)!);
                            if (!string.Equals(oldAbs, newAbs, StringComparison.OrdinalIgnoreCase))
                            {
                                if (File.Exists(oldAbs))
                                {
                                    if (File.Exists(newAbs)) try { File.Delete(newAbs); } catch { }
                                    File.Move(oldAbs, newAbs);
                                }
                            }
                            g.Files[i] = newRel;
                        }
                        catch { }
                    }
                    n++; groupsTried++;
                }
            }

            // Relink: first remove any existing target files per hex, then link all files from buckets
            Directory.CreateDirectory(gameFolder);
            int success = 0, fail = 0;
            foreach (var hex in perHex.Keys)
            {
                DisableByHexes(gameFolder, new[] { hex });
                foreach (var (mainName, g) in perHex[hex])
                {
                    var modRoot = Path.Combine(profileRoot, mainName);
                    foreach (var rel in g.Files)
                    {
                        var src = Path.Combine(modRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                        var fileName = Path.GetFileName(rel);
                        var dst = Path.Combine(gameFolder, fileName);
                        bool ok = CreateLinkPreferHard(dst, src) || TryCopy(dst, src);
                        if (ok) success++; else fail++;
                    }
                }
            }

            return new LinkStats(groupsTried, success, fail);
        }

        /// <summary>
        /// Settings page enable: link currently-enabled groups as-is (no renaming/reordering).
        /// For each group's hex, first remove any existing links in game folder to avoid conflicts.
        /// </summary>
        public LinkStats LinkEnabledByFiles(string profileRoot, string gameFolder, IEnumerable<MainModItem> mods)
        {
            Directory.CreateDirectory(gameFolder);
            var queue = new List<(string mainName, ModFileGroup group)>();
            foreach (var m in mods)
            {
                if (IsEnabled(m)) foreach (var g in m.FileGroups) queue.Add((m.Name, g));
                foreach (var o in m.Options)
                {
                    if (IsEnabled(o)) foreach (var g in o.FileGroups) queue.Add((m.Name, g));
                    foreach (var s in o.SubOptions)
                    {
                        if (IsEnabled(s)) foreach (var g in s.FileGroups) queue.Add((m.Name, g));
                    }
                }
            }

            int success = 0, fail = 0;
            foreach (var groupHex in queue.Select(t => t.group.HexPrefix).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                DisableByHexes(gameFolder, new[] { groupHex });
            }
            foreach (var (mainName, g) in queue)
            {
                var modRoot = Path.Combine(profileRoot, mainName);
                foreach (var rel in g.Files)
                {
                    var src = Path.Combine(modRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    var fileName = Path.GetFileName(rel);
                    var dst = Path.Combine(gameFolder, fileName);
                    bool ok = CreateLinkPreferHard(dst, src) || TryCopy(dst, src);
                    if (ok) success++; else fail++;
                }
            }
            return new LinkStats(queue.Count, success, fail);
        }

        private static bool IsEnabled(object o)
        {
            try
            {
                var prop = o.GetType().GetProperty("Enabled");
                if (prop == null) return false;
                var v = prop.GetValue(o);
                if (v is int i) return i == 1;
                if (v is bool b) return b;
            }
            catch { }
            return false;
        }

        private int EnableGroups(string modRoot, string gameFolder, IEnumerable<ModFileGroup> groups)
        {
            Directory.CreateDirectory(gameFolder);
            int newN = 0;
            int success = 0;
            foreach (var g in groups.OrderBy(g => g.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.PatchN))
            {
                // Remove existing files for this hex
                DisableByHexes(gameFolder, new[] { g.HexPrefix });
                foreach (var rel in g.Files)
                {
                    var src = Path.Combine(modRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    var fileName = Path.GetFileName(rel);
                    var tail = ExtractTail(fileName);
                    var linkName = $"{g.HexPrefix}.patch_{newN}{tail}";
                    var dst = Path.Combine(gameFolder, linkName);
                    if (CreateLinkPreferHard(dst, src) || TryCopy(dst, src)) success++;
                }
                newN++;
            }
            return success;
        }

        private static bool AreOnSameVolume(string path1, string path2)
        {
            try
            {
                var r1 = Path.GetPathRoot(Path.GetFullPath(path1));
                var r2 = Path.GetPathRoot(Path.GetFullPath(path2));
                return !string.IsNullOrEmpty(r1) && !string.IsNullOrEmpty(r2) && string.Equals(r1, r2, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool CreateLinkPreferHard(string dest, string src)
        {
            dest = NormalizeFullPath(dest);
            src = NormalizeFullPath(src);
            try { var dir = Path.GetDirectoryName(dest); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir); } catch { }
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }

            if (AreOnSameVolume(dest, src) && TryCreateHardLink(dest, src)) return true;
            if (TryCreateSymbolicLink(dest, src)) return true;
            return false;
        }

        private static string NormalizeFullPath(string p)
        {
            try { return Path.GetFullPath(p); } catch { return p; }
        }

        private static string ExtractTail(string fileName)
        {
            var m = PatchRegex.Match(fileName);
            if (m.Success) return m.Groups["tail"].Value;
            if (fileName.EndsWith(".gpu_resources", StringComparison.OrdinalIgnoreCase)) return ".gpu_resources";
            if (fileName.EndsWith(".stream", StringComparison.OrdinalIgnoreCase)) return ".stream";
            return string.Empty;
        }

        private static bool TryCopy(string linkPath, string srcPath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
                File.Copy(srcPath, linkPath, overwrite: true);
                return true;
            }
            catch { return false; }
        }

        // Single implementations of link creation wrappers
        public static bool TryCreateHardLink(string linkPath, string existingFilePath)
        {
            try
            {
                linkPath = Path.GetFullPath(linkPath);
                existingFilePath = Path.GetFullPath(existingFilePath);
                var dir = Path.GetDirectoryName(linkPath); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(linkPath)) { try { File.Delete(linkPath); } catch { } }
                if (CreateHardLink(linkPath, existingFilePath, IntPtr.Zero)) return true;
            }
            catch { }
            return false;
        }

        public static bool TryCreateSymbolicLink(string linkPath, string targetPath)
        {
            try
            {
                linkPath = Path.GetFullPath(linkPath);
                targetPath = Path.GetFullPath(targetPath);
                var dir = Path.GetDirectoryName(linkPath); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(linkPath)) { try { File.Delete(linkPath); } catch { } }
                const int FILE_FLAG = 0x0; // file symlink
                const int ALLOW_UNPRIVILEGED = 0x2;
                if (CreateSymbolicLink(linkPath, targetPath, FILE_FLAG | ALLOW_UNPRIVILEGED)) return true;
                if (CreateSymbolicLink(linkPath, targetPath, FILE_FLAG)) return true;
            }
            catch { }
            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);
    }
}

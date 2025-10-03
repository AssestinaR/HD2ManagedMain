using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;

namespace ManagedMain.Views
{
    // Scanner module: group-level signature matching only
    public static partial class StatusDrawerHost
    {
        public class FileGroupStatus
        {
            public string OwnerDisplay { get; set; } = string.Empty;
            public string HexPrefix { get; set; } = string.Empty;
            public int PatchN_ModList { get; set; }
            public int PatchN_Game { get; set; } = -1;
            public string GameFileName { get; set; } = string.Empty;
            public string[] GameFiles { get; set; } = Array.Empty<string>();
            public bool ExistsInGame { get; set; }
            public bool FilesAllLinked { get; set; }
            public int FileCount { get; set; }
            public string LinkType { get; set; } = string.Empty;
            public string Tooltip { get; set; } = string.Empty;
            public bool IsMissing { get; set; }
            public bool IsSequenceGap { get; set; }
            public bool IsDuplicate { get; set; }
            public bool IsExtra { get; set; }
            public bool IsPatchNMismatch { get; set; }
        }

        private static class StatusScanner
        {
            private static readonly System.Text.RegularExpressions.Regex PatchRegex = new("^([a-fA-F0-9]{16})\\.patch_(\\d+)(?:\\.stream|\\.gpu_resources)?$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            private static readonly Dictionary<string, string> s_sha256Cache = new(StringComparer.OrdinalIgnoreCase);

            private sealed class GameEntry
            {
                public string Hex = string.Empty;
                public int N;
                public string FullPath = string.Empty;
                public string TargetPath = string.Empty; // if symlink, resolved target full path; otherwise empty or same as FullPath
                public string FileName = string.Empty;
                public long Length;
                public string Tail = string.Empty;
                public string LinkType = string.Empty;
                public string Sha256 = string.Empty;
            }

            private static string ExtractTail(string fileName)
            {
                if (fileName.EndsWith(".gpu_resources", StringComparison.OrdinalIgnoreCase)) return ".gpu_resources";
                if (fileName.EndsWith(".stream", StringComparison.OrdinalIgnoreCase)) return ".stream";
                return string.Empty;
            }

            private static bool IsEnabled(object o)
            {
                try
                {
                    var prop = o.GetType().GetProperty("Enabled");
                    if (prop == null) return false;
                    var v = prop.GetValue(o);
                    if (v is int i) return i != 0;
                    if (v is bool b) return b;
                }
                catch { }
                return false;
            }

            private static string HashOf(string path)
            {
                try
                {
                    var finfo = new FileInfo(path);
                    string key = path + "|" + finfo.Length + "|" + finfo.LastWriteTimeUtc.Ticks;
                    if (s_sha256Cache.TryGetValue(key, out var v)) return v;
                    using var fs = File.OpenRead(path);
                    var hash = SHA256.HashData(fs);
                    var hex = BitConverter.ToString(hash).Replace("-", string.Empty);
                    s_sha256Cache[key] = hex; return hex;
                }
                catch { return string.Empty; }
            }

            private static string HashText(string text)
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    var hash = SHA256.HashData(bytes);
                    return BitConverter.ToString(hash).Replace("-", string.Empty);
                }
                catch { return string.Empty; }
            }

            public static IEnumerable<FileGroupStatus> Scan(string profileRoot, string gameFolder, IEnumerable mods)
            {
                if (string.IsNullOrWhiteSpace(profileRoot) || string.IsNullOrWhiteSpace(gameFolder)) yield break;

                // Scan game files -> map by Hex, and per Hex compute gapStart and group signatures per N
                var gameByHex = new Dictionary<string, List<GameEntry>>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(gameFolder, "*.patch_*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file)!;
                    var m = PatchRegex.Match(name);
                    if (!m.Success) continue;
                    string hex = m.Groups[1].Value;
                    int n = int.TryParse(m.Groups[2].Value, out var pn) ? pn : -1;
                    string tail = ExtractTail(name);
                    long len = 0; string link = "Unknown"; string targetPath = string.Empty;
                    try
                    {
                        var attr = File.GetAttributes(file);
                        bool isSym = (attr & FileAttributes.ReparsePoint) != 0;
                        link = isSym ? "Sym" : "Hard/Copy";
                        if (isSym)
                        {
                            try
                            {
                                var fsi = File.ResolveLinkTarget(file, returnFinalTarget: true);
                                if (fsi is FileInfo tfi)
                                {
                                    targetPath = tfi.FullName;
                                    len = tfi.Length;
                                }
                                else
                                {
                                    var fi = new FileInfo(file); len = fi.Length; targetPath = file;
                                }
                            }
                            catch
                            {
                                var fi = new FileInfo(file); len = fi.Length; targetPath = file;
                            }
                        }
                        else
                        {
                            var fi = new FileInfo(file); len = fi.Length; targetPath = file;
                        }
                    }
                    catch { }
                    if (!gameByHex.TryGetValue(hex, out var list)) { list = new List<GameEntry>(); gameByHex[hex] = list; }
                    list.Add(new GameEntry { Hex = hex, N = n, FullPath = file, TargetPath = targetPath, FileName = name, Length = len, Tail = tail, LinkType = link });
                }

                var gapStart = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var gameSig = new Dictionary<string, Dictionary<string, List<int>>>(StringComparer.OrdinalIgnoreCase); // hex -> sig -> Ns
                foreach (var kv in gameByHex)
                {
                    var hex = kv.Key; var list = kv.Value;
                    var ns = list.Select(e => e.N).Where(n => n >= 0).Distinct().OrderBy(n => n).ToList();
                    int expected = 0; int missingAt = -1;
                    foreach (var n in ns) { if (n != expected) { missingAt = expected; break; } expected++; }
                    if (missingAt < 0 && ns.Count > 0 && ns.Last() == ns.Count - 1) missingAt = -1;
                    gapStart[hex] = missingAt;

                    var byN = list.GroupBy(e => e.N);
                    var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var gN in byN)
                    {
                        var parts = new List<string>();
                        foreach (var e in gN)
                        {
                            var pathToHash = !string.IsNullOrEmpty(e.TargetPath) ? e.TargetPath : e.FullPath;
                            if (string.IsNullOrEmpty(e.Sha256)) e.Sha256 = HashOf(pathToHash);
                            parts.Add($"{e.Tail}|{e.Length}|{e.Sha256}");
                        }
                        parts.Sort(StringComparer.Ordinal);
                        var sig = HashText(string.Join("\n", parts));
                        if (string.IsNullOrEmpty(sig)) continue;
                        if (!map.TryGetValue(sig, out var nsList)) { nsList = new List<int>(); map[sig] = nsList; }
                        nsList.Add(gN.Key);
                    }
                    gameSig[hex] = map;
                }

                var usedGroups = new HashSet<(string Hex, int N)>();
                
                IEnumerable<FileGroupStatus> EmitGroups(IEnumerable<ManagedMain.Models.ModFileGroup> groups, string owner)
                {
                    foreach (var g in groups)
                    {
                        // Build expected group signature (include 0B files)
                        var parts = new List<string>();
                        int expectedCount = g.Files?.Count ?? 0;
                        if (g.Files != null)
                        {
                            foreach (var rel in g.Files)
                            {
                                var abs = Path.Combine(profileRoot, owner.Split('/')[0], rel.Replace('/', Path.DirectorySeparatorChar));
                                string tail = ExtractTail(Path.GetFileName(rel) ?? string.Empty);
                                long len = 0; try { var fi = new FileInfo(abs); len = fi.Length; } catch { }
                                string sha = HashOf(abs);
                                parts.Add($"{tail}|{len}|{sha}");
                            }
                        }
                        parts.Sort(StringComparer.Ordinal);
                        var expSig = HashText(string.Join("\n", parts));

                        int chosenN = -1; bool dup = false;
                        if (!string.IsNullOrEmpty(expSig) && gameSig.TryGetValue(g.HexPrefix, out var sigMap))
                        {
                            if (sigMap.TryGetValue(expSig, out var nsList) && nsList.Count > 0)
                            {
                                dup = nsList.Count > 1;
                                chosenN = nsList.Contains(g.PatchN) ? g.PatchN : nsList.OrderBy(n => Math.Abs(n - g.PatchN)).First();
                                usedGroups.Add((g.HexPrefix, chosenN));
                            }
                        }

                        bool isMissing = chosenN < 0;
                        bool anyExpectedN = chosenN == g.PatchN;
                        bool isMismatch = !isMissing && !anyExpectedN;
                        bool isSeqGap = false;
                        if (!isMissing && gapStart.TryGetValue(g.HexPrefix, out var gs) && gs >= 0)
                        {
                            if (chosenN > gs) isSeqGap = true;
                        }

                        // Build aggregated game file names and link type for display
                        string[] gameFiles = Array.Empty<string>();
                        string linkType = string.Empty;
                        if (!isMissing && gameByHex.TryGetValue(g.HexPrefix, out var all))
                        {
                            var matched = all.Where(e => e.N == chosenN).OrderBy(e => e.Tail).ToList();
                            gameFiles = matched.Select(e => e.FileName).ToArray();
                            bool anySym = matched.Any(e => string.Equals(e.LinkType, "Sym", StringComparison.OrdinalIgnoreCase));
                            bool allHardCopy = matched.All(e => string.Equals(e.LinkType, "Hard/Copy", StringComparison.OrdinalIgnoreCase));
                            linkType = anySym ? (allHardCopy ? "Mixed" : "Sym") : "Hard/Copy";
                        }

                        var tips = new List<string>();
                        if (isMissing) tips.Add(ManagedMain.Resources.Strings.SR_Status_Missing);
                        if (isSeqGap) tips.Add(ManagedMain.Resources.Strings.SR_Status_SeqGap);
                        if (dup) tips.Add(ManagedMain.Resources.Strings.SR_Status_Duplicate);
                        if (isMismatch) tips.Add(ManagedMain.Resources.Strings.SR_Status_PatchMismatch);
                        if (tips.Count == 0) tips.Add(ManagedMain.Resources.Strings.SR_Status_Normal);

                        yield return new FileGroupStatus
                        {
                            OwnerDisplay = owner,
                            HexPrefix = g.HexPrefix,
                            PatchN_ModList = g.PatchN,
                            PatchN_Game = chosenN,
                            GameFileName = gameFiles.Length > 0 ? string.Join(" | ", gameFiles) : string.Empty,
                            GameFiles = gameFiles,
                            ExistsInGame = !isMissing,
                            FilesAllLinked = !isMissing && anyExpectedN,
                            FileCount = expectedCount,
                            LinkType = linkType,
                            Tooltip = string.Join("\n", tips),
                            IsMissing = isMissing,
                            IsSequenceGap = isSeqGap,
                            IsDuplicate = dup,
                            IsPatchNMismatch = isMismatch,
                            IsExtra = false
                        };
                     }
                 }

                 foreach (var m in mods)
                 {
                     if (m is not ManagedMain.Models.MainModItem main) continue;
                     if (!IsEnabled(main)) continue;
                     foreach (var st in EmitGroups(main.FileGroups, main.Name)) yield return st;
                     foreach (var o in main.Options)
                     {
                         if (!IsEnabled(o)) continue;
                         foreach (var st in EmitGroups(o.FileGroups, main.Name + "/" + o.Name)) yield return st;
                         foreach (var s in o.SubOptions)
                         {
                             if (!IsEnabled(s)) continue;
                             foreach (var st in EmitGroups(s.FileGroups, main.Name + "/" + o.Name + "/" + s.Name)) yield return st;
                         }
                     }
                 }

                // Extras: any game file in Hex+N not used by any group
                foreach (var kv in gameByHex)
                {
                    foreach (var e in kv.Value)
                    {
                        if (!usedGroups.Contains((e.Hex, e.N)))
                        {
                            yield return new FileGroupStatus
                            {
                                OwnerDisplay = string.Empty,
                                HexPrefix = e.Hex,
                                PatchN_ModList = -1,
                                PatchN_Game = e.N,
                                GameFileName = e.FileName,
                                GameFiles = new[] { e.FileName },
                                ExistsInGame = true,
                                FilesAllLinked = true,
                                FileCount = 1,
                                LinkType = e.LinkType,
                                Tooltip = ManagedMain.Resources.Strings.SR_Status_ExtraTip,
                                IsMissing = false,
                                IsDuplicate = false,
                                IsSequenceGap = false,
                                IsPatchNMismatch = false,
                                IsExtra = true
                            };
                        }
                    }
                }
            }
        }
    }
}

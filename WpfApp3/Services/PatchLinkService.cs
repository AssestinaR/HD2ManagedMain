using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiberTeaManager.Services
{
    internal sealed class PatchLinkService : IPatchLinkService
    {
        private readonly ILogService _log;
        private readonly Func<string> _gameFolderAccessor;
        private readonly Func<string> _modFolderAccessor;
        private readonly IList<MainModItem> _mods; // 直接共享集合引用（现阶段简化）

        private int _hardLinkCounter;
        private int _symLinkCounter;
        private int _copyCounter;

        // 记录上次已处理的文件组 (key=Hex:PatchN) -> signature，用于差量更新 (含文件元数据)
        private Dictionary<string, string> _lastGroupSignatures = new();
        // 记录单文件元数据，避免无变化重复硬链接 (destFullPath -> info)
        private readonly Dictionary<string, FileLinkInfo> _fileMeta = new(StringComparer.OrdinalIgnoreCase);

        private sealed class FileLinkInfo
        {
            public string Src { get; init; } = string.Empty;
            public long Length { get; init; }
            public long WriteTicks { get; init; }
            public LinkMethod Method { get; init; }
        }

        public int HardLinkCount => _hardLinkCounter;
        public int SymLinkCount => _symLinkCounter;
        public int CopyCount => _copyCounter;

        public PatchLinkService(IList<MainModItem> mods, ILogService logService, Func<string> gameFolderAccessor, Func<string> modFolderAccessor)
        {
            _mods = mods;
            _log = logService;
            _gameFolderAccessor = gameFolderAccessor;
            _modFolderAccessor = modFolderAccessor;
        }

        public Task ReorderAndLinkAsync(bool fullRebuild, bool logPerGroup)
        {
            string gameFolder = _gameFolderAccessor();
            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            {
                _log.Log("GameFolder 未设置或不存在, 跳过 Patch 排序");
                return Task.CompletedTask;
            }
            return Task.Run(() => Execute(fullRebuild, logPerGroup, gameFolder));
        }

        private void Execute(bool fullRebuild, bool logPerGroup, string gameFolder)
        {
            try
            {
                _hardLinkCounter = _symLinkCounter = _copyCounter = 0;
                if (fullRebuild)
                {
                    // 重要: 清空旧的目标文件元数据，否则会因为字典中仍有记录而误判“无需刷新链接”
                    _fileMeta.Clear();
                    try
                    {
                        foreach (var f in Directory.GetFiles(gameFolder, "*.patch_*"))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                    catch (Exception ex) { _log.Log("清理旧 Patch 文件失败: " + ex.Message); }
                }

                var ordered = new List<(string Hex, MainModItem Mod, ModFileGroup Group)>();
                foreach (var mod in _mods)
                {
                    if (mod.Enabled == EnabledState.Enabled)
                        foreach (var g in mod.FileGroups) ordered.Add((g.HexPrefix, mod, g));
                    foreach (var opt in mod.Options)
                    {
                        if (opt.Enabled == EnabledState.Enabled)
                            foreach (var g in opt.FileGroups) ordered.Add((g.HexPrefix, mod, g));
                        foreach (var sub in opt.SubOptions)
                            if (sub.Enabled == EnabledState.Enabled)
                                foreach (var g in sub.FileGroups) ordered.Add((g.HexPrefix, mod, g));
                    }
                }

                var groupsByHex = new Dictionary<string, List<(MainModItem Mod, ModFileGroup Group)>>(StringComparer.OrdinalIgnoreCase);
                var hexOrder = new List<string>();
                foreach (var (hex, mod, grp) in ordered)
                {
                    if (!groupsByHex.TryGetValue(hex, out var list)) { list = new List<(MainModItem, ModFileGroup)>(); groupsByHex[hex] = list; hexOrder.Add(hex); }
                    list.Add((mod, grp));
                }

                var patchRegex = new Regex(@"\.patch_(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                foreach (var hex in hexOrder)
                {
                    var list = groupsByHex[hex];
                    for (int i = 0; i < list.Count; i++)
                    {
                        var (mod, group) = list[i];
                        int desired = i;
                        if (group.PatchN != desired) group.PatchN = desired;
                        for (int fi = 0; fi < group.Files.Count; fi++)
                        {
                            var rel = group.Files[fi]; if (string.IsNullOrEmpty(rel)) continue;
                            var fn = Path.GetFileName(rel); if (fn == null) continue;
                            var match = patchRegex.Match(fn);
                            int current = -1; if (match.Success) int.TryParse(match.Groups[1].Value, out current);
                            if (current == desired) continue;
                            var newFn = match.Success ? patchRegex.Replace(fn, $".patch_{desired}") : fn + $".patch_{desired}";
                            var relDir = Path.GetDirectoryName(rel)?.Replace('\\','/') ?? string.Empty;
                            var modDir = Path.Combine(_modFolderAccessor(), mod.Name, relDir.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(modDir);
                            var oldAbs = Path.Combine(modDir, fn);
                            var newAbs = Path.Combine(modDir, newFn);
                            try
                            {
                                if (File.Exists(oldAbs))
                                {
                                    if (File.Exists(newAbs)) File.Delete(newAbs);
                                    File.Move(oldAbs, newAbs);
                                }
                                group.Files[fi] = string.IsNullOrEmpty(relDir) ? newFn : relDir + "/" + newFn;
                            }
                            catch (Exception ex) { _log.Log($"重命名失败: {fn} => {newFn}: {ex.Message}"); }
                        }
                    }
                    if (!fullRebuild)
                    {
                        try
                        {
                            var valid = new HashSet<string>(list.SelectMany(p => p.Group.Files.Select(f => Path.GetFileName(f) ?? string.Empty)));
                            foreach (var existing in Directory.GetFiles(gameFolder, hex + ".patch_*"))
                            {
                                var name = Path.GetFileName(existing) ?? string.Empty;
                                if (!valid.Contains(name)) { try { File.Delete(existing); } catch { } }
                            }
                        }
                        catch (Exception ex) { _log.Log($"增量清理失败({hex}): {ex.Message}"); }
                    }
                }

                // 重新生成签名并建立链接
                var newSignatures = new Dictionary<string, string>();
                foreach (var (hex, mod, group) in ordered)
                {
                    string key = hex + ":" + group.PatchN;
                    var fileMetaParts = new List<string>(group.Files.Count);
                    foreach (var f in group.Files)
                    {
                        string src = Path.Combine(_modFolderAccessor(), mod.Name, f.Replace('/', Path.DirectorySeparatorChar));
                        try
                        {
                            var fi = new FileInfo(src);
                            fileMetaParts.Add(fi.Exists ? ($"{Path.GetFileName(f)}#{fi.Length}:{fi.LastWriteTimeUtc.Ticks}") : ($"{Path.GetFileName(f)}#0:0"));
                        }
                        catch { fileMetaParts.Add($"{Path.GetFileName(f)}#0:0"); }
                    }
                    string signature = group.PatchN + "|" + string.Join("|", fileMetaParts);
                    newSignatures[key] = signature;
                    bool needLink = fullRebuild || !_lastGroupSignatures.TryGetValue(key, out var oldSig) || !string.Equals(oldSig, signature, StringComparison.Ordinal);
                    if (needLink)
                    {
                        LinkGroupFiles(mod, group, logPerGroup);
                    }
                }
                _lastGroupSignatures = newSignatures; // 更新缓存
            }
            catch (Exception ex) { _log.Log("Patch 排序/链接异常: " + ex.Message); }
            ModListManager.SaveModList(new System.Collections.ObjectModel.ObservableCollection<MainModItem>(_mods));
        }

        private enum LinkMethod { None, Hard, Sym, Copy }

        private void LinkGroupFiles(MainModItem mod, ModFileGroup group, bool logPerGroup)
        {
            LinkMethod aggregate = LinkMethod.None;
            foreach (var rel in group.Files)
            {
                try
                {
                    var src = Path.Combine(_modFolderAccessor(), mod.Name, rel.Replace('/', Path.DirectorySeparatorChar));
                    var dest = Path.Combine(_gameFolderAccessor(), Path.GetFileName(rel)!);
                    bool skip = false;
                    try
                    {
                        var fi = new FileInfo(src);
                        // 只有在目标文件仍然存在时才允许“跳过”
                        if (fi.Exists && File.Exists(dest) && _fileMeta.TryGetValue(dest, out var meta) && meta.Src.Equals(src, StringComparison.OrdinalIgnoreCase) && meta.Length == fi.Length && meta.WriteTicks == fi.LastWriteTimeUtc.Ticks)
                        {
                            skip = true;
                        }
                    }
                    catch { }
                    if (skip) continue;

                    if (File.Exists(src))
                    {
                        if (CreateOrReplaceHardLink(dest, src, out var method))
                        {
                            try
                            {
                                var fi = new FileInfo(src);
                                _fileMeta[dest] = new FileLinkInfo { Src = src, Length = fi.Exists ? fi.Length : 0, WriteTicks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0, Method = method };
                            }
                            catch { }
                            aggregate = method switch
                            {
                                LinkMethod.Copy => LinkMethod.Copy,
                                LinkMethod.Sym => aggregate == LinkMethod.Copy ? LinkMethod.Copy : LinkMethod.Sym,
                                LinkMethod.Hard => aggregate == LinkMethod.None ? LinkMethod.Hard : aggregate,
                                _ => aggregate
                            };
                        }
                    }
                }
                catch (Exception ex) { _log.Log($"链接/同步失败: {rel} => {ex.Message}"); }
            }
            if (logPerGroup)
            {
                string methodText = aggregate switch { LinkMethod.Hard => "硬链接", LinkMethod.Sym => "符号链接", LinkMethod.Copy => "复制", _ => "未知" };
                _log.Log($"已启用 {group.HexPrefix}.patch_{group.PatchN}（{methodText}）");
            }
        }

        private bool CreateOrReplaceHardLink(string dest, string src, out LinkMethod method)
        {
            method = LinkMethod.None;
            try
            {
                if (File.Exists(dest))
                {
                    try { File.Delete(dest); } catch { }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (CreateHardLink(dest, src, IntPtr.Zero)) { Interlocked.Increment(ref _hardLinkCounter); method = LinkMethod.Hard; return true; }
                int hardErr = Marshal.GetLastWin32Error();
                bool symlinkOk = CreateSymbolicLink(dest, src, 0x2);
                if (!symlinkOk)
                {
                    int symErr1 = Marshal.GetLastWin32Error();
                    symlinkOk = CreateSymbolicLink(dest, src, 0x0);
                    if (!symlinkOk)
                    {
                        int symErr2 = Marshal.GetLastWin32Error();
                        _log.Log($"硬链接失败(Win32={hardErr}), 软链接失败(1={symErr1},2={symErr2})，改为复制: {Path.GetFileName(src)}");
                    }
                }
                if (symlinkOk) { Interlocked.Increment(ref _symLinkCounter); method = LinkMethod.Sym; return true; }
                File.Copy(src, dest, true); Interlocked.Increment(ref _copyCounter); method = LinkMethod.Copy; return true;
            }
            catch (Exception ex)
            {
                _log.Log($"链接/复制失败: {Path.GetFileName(src)} => {ex.Message}");
                method = LinkMethod.None; return false;
            }
        }

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);
    }
}

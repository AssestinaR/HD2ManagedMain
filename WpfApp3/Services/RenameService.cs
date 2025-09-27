using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using LiberTeaManager.Services;

namespace LiberTeaManager.Services
{
    internal sealed class RenameService : IRenameService
    {
        private readonly ILogService _log;
        public RenameService(ILogService log) => _log = log;

        public bool TryRename(object target, string newName, ObservableCollection<MainModItem> mods)
        {
            try
            {
                switch (target)
                {
                    case MainModItem m: return RenameMain(m, newName, mods);
                    case OptionItem o: return RenameOption(o, mods, newName);
                    case SubOptionItem s: return RenameSubOption(s, mods, newName);
                }
            }
            catch (Exception ex)
            {
                _log.Log("重命名失败: " + ex.Message);
            }
            return false;
        }

        private bool RenameMain(MainModItem m, string newVal, ObservableCollection<MainModItem> mods)
        {
            string modRoot = SettingsContext.ModFolder;
            string oldName = m.Name;
            if (string.Equals(oldName, newVal, StringComparison.OrdinalIgnoreCase)) return false;
            if (mods.Any(x => x != m && string.Equals(x.Name, newVal, StringComparison.OrdinalIgnoreCase))) { _log.Log($"已存在同名 Mod: {newVal}"); return false; }
            string oldDir = Path.Combine(modRoot, oldName);
            string newDir = Path.Combine(modRoot, newVal);
            if (Directory.Exists(newDir)) { _log.Log($"已存在同名Mod目录: {newVal}"); return false; }
            if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);
            UpdatePathsForRename(m, oldName, newVal);
            m.Name = newVal; m.RootModName = newVal;
            _log.Log($"已重命名: {oldName} -> {newVal}");
            return true;
        }

        private bool RenameOption(OptionItem o, ObservableCollection<MainModItem> mods, string newVal)
        {
            var parent = mods.FirstOrDefault(mm => mm.Options.Contains(o)); if (parent == null) return false;
            string oldName = o.Name;
            if (string.Equals(oldName, newVal, StringComparison.OrdinalIgnoreCase)) return false;
            if (parent.Options.Any(x => x != o && string.Equals(x.Name, newVal, StringComparison.OrdinalIgnoreCase))) { _log.Log($"已存在同名选项: {newVal}"); return false; }
            string parentDir = Path.Combine(SettingsContext.ModFolder, parent.Name);
            string oldDir = Path.Combine(parentDir, oldName);
            string newDir = Path.Combine(parentDir, newVal);
            if (Directory.Exists(newDir)) { _log.Log($"已存在同名选项目录: {newVal}"); return false; }
            if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);
            foreach (var g in o.FileGroups) ReplaceFirstSegmentInGroup(g, oldName, newVal);
            if (!string.IsNullOrEmpty(o.IconPath) && o.IconPath.StartsWith(oldName + "/", StringComparison.OrdinalIgnoreCase))
                o.IconPath = newVal + o.IconPath.Substring(oldName.Length);
            foreach (var sub in o.SubOptions)
            {
                foreach (var g in sub.FileGroups) ReplaceFirstSegmentInGroup(g, oldName, newVal);
                if (!string.IsNullOrEmpty(sub.IconPath))
                {
                    var optPrefix = oldName + "/";
                    if (sub.IconPath.StartsWith(optPrefix, StringComparison.OrdinalIgnoreCase))
                        sub.IconPath = newVal + sub.IconPath.Substring(oldName.Length);
                }
            }
            o.Name = newVal;
            _log.Log($"已重命名: {oldName} -> {newVal}");
            return true;
        }

        private bool RenameSubOption(SubOptionItem s, ObservableCollection<MainModItem> mods, string newVal)
        {
            foreach (var mod in mods)
            {
                var opt = mod.Options.FirstOrDefault(op => op.SubOptions.Contains(s));
                if (opt != null)
                {
                    string oldName = s.Name;
                    if (string.Equals(oldName, newVal, StringComparison.OrdinalIgnoreCase)) return false;
                    if (opt.SubOptions.Any(x => x != s && string.Equals(x.Name, newVal, StringComparison.OrdinalIgnoreCase))) { _log.Log($"已存在同名子选项: {newVal}"); return false; }
                    string baseDir = Path.Combine(SettingsContext.ModFolder, mod.Name, opt.Name);
                    string oldDir = Path.Combine(baseDir, oldName);
                    string newDir = Path.Combine(baseDir, newVal);
                    if (Directory.Exists(newDir)) { _log.Log($"已存在同名子选项目录: {newVal}"); return false; }
                    if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);
                    foreach (var g in s.FileGroups) ReplaceFirstSegmentInGroup(g, oldName, newVal);
                    if (!string.IsNullOrEmpty(s.IconPath))
                    {
                        var subPrefix = opt.Name + "/" + oldName + "/";
                        if (s.IconPath.StartsWith(subPrefix, StringComparison.OrdinalIgnoreCase))
                            s.IconPath = opt.Name + "/" + newVal + s.IconPath.Substring(subPrefix.Length);
                        else if (s.IconPath.StartsWith(oldName + "/", StringComparison.OrdinalIgnoreCase))
                            s.IconPath = newVal + s.IconPath.Substring(oldName.Length);
                    }
                    s.Name = newVal;
                    _log.Log($"已重命名: {oldName} -> {newVal}");
                    return true;
                }
            }
            return false;
        }

        private static void UpdatePathsForRename(MainModItem m, string oldSeg, string newSeg)
        {
            foreach (var g in m.FileGroups) ReplaceFirstSegmentInGroup(g, oldSeg, newSeg);
            foreach (var opt in m.Options)
            {
                ReplaceFirstSegmentInInclude(opt.Include, oldSeg, newSeg);
                foreach (var g in opt.FileGroups) ReplaceFirstSegmentInGroup(g, oldSeg, newSeg);
                foreach (var sub in opt.SubOptions)
                {
                    ReplaceFirstSegmentInInclude(sub.Include, oldSeg, newSeg);
                    foreach (var g in sub.FileGroups) ReplaceFirstSegmentInGroup(g, oldSeg, newSeg);
                }
            }
        }

        private static void ReplaceFirstSegmentInGroup(ModFileGroup g, string oldSeg, string newSeg)
        {
            if (!string.IsNullOrEmpty(g.RelativePath)) g.RelativePath = ReplaceFirstSegment(g.RelativePath, oldSeg, newSeg);
            for (int i = 0; i < g.Files.Count; i++) g.Files[i] = ReplaceFirstSegment(g.Files[i], oldSeg, newSeg);
        }
        private static void ReplaceFirstSegmentInInclude(List<string> inc, string oldSeg, string newSeg)
        {
            if (inc == null) return; for (int i = 0; i < inc.Count; i++) inc[i] = ReplaceFirstSegment(inc[i], oldSeg, newSeg);
        }
        private static string ReplaceFirstSegment(string path, string oldSeg, string newSeg)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var p = path.Replace('\\', '/');
            if (p.StartsWith(oldSeg + "/", StringComparison.OrdinalIgnoreCase)) return newSeg + p.Substring(oldSeg.Length);
            return p;
        }
    }
}

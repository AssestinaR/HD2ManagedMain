using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ManagedMain.Models;

namespace ManagedMain.Services
{
    // 自包含的树结构变换服务（ManagedMain）
    public class TreeTransformPort
    {
        public enum TreePlacement { None, Before, After, Inside }
        public enum StructureOpResult { None, Reordered, Moved, Promoted, Demoted }

        public StructureOpResult Execute(IEnumerable<object> dragged,
            object? target,
            TreePlacement placement,
            ObservableCollection<MainModItem> roots,
            string profileRoot,
            Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(profileRoot)) { log("ProfileRoot 为空，取消结构变更"); return StructureOpResult.None; }
            var list = dragged?.Distinct().ToList() ?? new List<object>(); if (list.Count == 0) return StructureOpResult.None;
            if (list.Select(GetKind).Distinct().Count() > 1) return StructureOpResult.None;
            var kind = GetKind(list.First()); var tkind = GetKind(target);
            if (target != null)
            {
                if (list.Contains(target)) return StructureOpResult.None;
                foreach (var d in list) if (IsAncestorOf(d, target!, roots)) return StructureOpResult.None;
            }

            // 1) 同层重排 (only when same parent)
            if (placement is TreePlacement.Before or TreePlacement.After && kind == tkind && target != null && HasSameParent(list, target, roots))
            {
                ReorderSameLevel(list, target, placement, roots);
                log("已重排"); return StructureOpResult.Reordered;
            }

            // 1.1) Cross-parent sibling placement for Options: move to target's parent then insert
            if (placement is TreePlacement.Before or TreePlacement.After && target is OptionItem anchorOpt && kind == NodeKind.Option)
            {
                MoveOptionsToSiblingPlacement(profileRoot, list.Cast<OptionItem>(), anchorOpt, placement, roots, log);
                return StructureOpResult.Moved;
            }

            // 2) Inside 各类移动/晋升/降级
            if (placement == TreePlacement.Inside)
            {
                switch (kind)
                {
                    case NodeKind.Option:
                        if (target is MainModItem mainT)
                        { MoveOptionsToMain(profileRoot, list.Cast<OptionItem>(), mainT, roots, log); return StructureOpResult.Moved; }
                        if (target is OptionItem optT)
                        { DemoteOptionsToSub(profileRoot, list.Cast<OptionItem>(), optT, roots, log); return StructureOpResult.Demoted; }
                        break;
                    case NodeKind.Sub:
                        if (target is OptionItem optTarget)
                        { MoveSubsToOption(profileRoot, list.Cast<SubOptionItem>(), optTarget, roots, log); return StructureOpResult.Moved; }
                        if (target is MainModItem mainP)
                        { PromoteSubsToOptions(profileRoot, list.Cast<SubOptionItem>(), mainP, roots, log); return StructureOpResult.Promoted; }
                        break;
                    case NodeKind.Main:
                        if (target is MainModItem other)
                        { DemoteMainsIntoMain(profileRoot, list.Cast<MainModItem>(), other, roots, log); return StructureOpResult.Demoted; }
                        // 新增：Main 拖入 Option 内部 -> 作为该 Option 的 SubOption
                        if (target is OptionItem optInside)
                        { DemoteMainsIntoOptionSub(profileRoot, list.Cast<MainModItem>(), optInside, roots, log); return StructureOpResult.Demoted; }
                        break;
                }
            }

            // 3) Before/After 跨层晋升
            if (placement is TreePlacement.Before or TreePlacement.After && target is MainModItem mainTarget)
            {
                if (kind == NodeKind.Option)
                { PromoteOptionsToMains(profileRoot, list.Cast<OptionItem>(), roots, mainTarget, placement, log); return StructureOpResult.Promoted; }
                if (kind == NodeKind.Sub)
                { PromoteSubsToMains(profileRoot, list.Cast<SubOptionItem>(), roots, mainTarget, placement, log); return StructureOpResult.Promoted; }
            }

            // 新增：Sub 在 Option 的 Before/After -> 晋升为与目标同 Main 下的 Option，并按位置插入
            if (placement is TreePlacement.Before or TreePlacement.After && target is OptionItem optSibling && kind == NodeKind.Sub)
            {
                PromoteSubsToSiblingOptions(profileRoot, list.Cast<SubOptionItem>(), roots, optSibling, placement, log);
                return StructureOpResult.Promoted;
            }

            // SubOption Before/After SubOption: move into the target's option and reorder at the anchor position
            if (placement is TreePlacement.Before or TreePlacement.After && target is SubOptionItem anchorSub && kind == NodeKind.Sub)
            {
                MoveSubsToSiblingPlacement(profileRoot, list.Cast<SubOptionItem>(), anchorSub, placement, roots, log);
                return StructureOpResult.Moved;
            }

            return StructureOpResult.None;
        }

        private enum NodeKind { Unknown, Main, Option, Sub }
        private static NodeKind GetKind(object? o) => o switch { MainModItem => NodeKind.Main, OptionItem => NodeKind.Option, SubOptionItem => NodeKind.Sub, _ => NodeKind.Unknown };
        private static object? GetParent(object item, ObservableCollection<MainModItem> roots)
        {
            if (item is MainModItem) return null;
            if (item is OptionItem opt) return roots.FirstOrDefault(m => m.Options.Contains(opt));
            if (item is SubOptionItem sub) return roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub));
            return null;
        }
        private static bool HasSameParent(IEnumerable<object> moving, object target, ObservableCollection<MainModItem> roots)
        {
            var tparent = GetParent(target, roots);
            foreach (var m in moving)
            {
                if (!Equals(GetParent(m, roots), tparent)) return false;
            }
            return true;
        }
        private static bool IsAncestorOf(object a, object b, ObservableCollection<MainModItem> roots)
        {
            if (a == b) return true;
            if (a is MainModItem m) { if (b is OptionItem o && m.Options.Contains(o)) return true; if (b is SubOptionItem s && m.Options.Any(o2 => o2.SubOptions.Contains(s))) return true; }
            if (a is OptionItem opt) { if (b is SubOptionItem s2 && opt.SubOptions.Contains(s2)) return true; }
            return false;
        }

        #region Reorder
        private static void ReorderSameLevel(List<object> moving, object target, TreePlacement placement, ObservableCollection<MainModItem> roots)
        {
            var parent = GetParent(target, roots);
            if (target is MainModItem)
            {
                var snap = roots.Cast<object>().ToList(); ApplyReorder(snap, moving, target, placement == TreePlacement.After); roots.Clear(); foreach (var m in snap.Cast<MainModItem>()) roots.Add(m);
            }
            else if (target is OptionItem tOpt && parent is MainModItem pm)
            {
                var snap = pm.Options.Cast<object>().ToList(); ApplyReorder(snap, moving, target, placement == TreePlacement.After); pm.Options.Clear(); foreach (var o in snap.Cast<OptionItem>()) pm.Options.Add(o);
            }
            else if (target is SubOptionItem tSub && parent is OptionItem po)
            {
                var snap = po.SubOptions.Cast<object>().ToList(); ApplyReorder(snap, moving, target, placement == TreePlacement.After); po.SubOptions.Clear(); foreach (var s in snap.Cast<SubOptionItem>()) po.SubOptions.Add(s);
            }
        }
        private static void ApplyReorder(List<object> snap, List<object> moving, object target, bool after)
        {
            moving = moving.OrderBy(i => snap.IndexOf(i)).ToList(); foreach (var m in moving) snap.Remove(m); int ti = snap.IndexOf(target); if (ti < 0) return; int insertAt = after ? ti + 1 : ti; if (insertAt > snap.Count) insertAt = snap.Count; snap.InsertRange(insertAt, moving);
        }
        #endregion

        #region FS Helpers
        private static string MainDir(string profileRoot, string main) => Path.Combine(profileRoot, main);
        private static string OptionDir(string profileRoot, string main, string opt) => Path.Combine(profileRoot, main, opt);
        private static string SubDir(string profileRoot, string main, string opt, string sub) => Path.Combine(profileRoot, main, opt, sub);

        private static void SafeMoveDir(string src, string dest, Action<string> log)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dest)) return;
                if (!Directory.Exists(src)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (Directory.Exists(dest))
                {
                    foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(src, f);
                        var df = Path.Combine(dest, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(df)!);
                        File.Copy(f, df, true);
                    }
                    Directory.Delete(src, true);
                }
                else Directory.Move(src, dest);
                log($"FS: {src} -> {dest}");
            }
            catch (Exception ex) { log("FS move failed: " + ex.Message); }
        }

        private static string StripPrefix(string? value, string prefix)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var v = value.Replace('\\', '/'); prefix = prefix.Replace('\\', '/');
            if (v.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) return v.Substring(prefix.Length + 1);
            return v;
        }
        private static string AddPrefix(string? value, string prefix)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var v = value.Replace('\\', '/'); prefix = prefix.Replace('\\', '/');
            if (v.Contains('/')) return prefix + "/" + v.Split('/', 2)[1];
            return prefix + "/" + v;
        }
        #endregion

        #region Rebuild FileGroups
        private static void RebuildMain(string profileRoot, MainModItem main, Action<string> log)
        {
            try
            {
                var dir = MainDir(profileRoot, main.Name); if (!Directory.Exists(dir)) return;
                main.FileGroups = FileGroupScanner.GetModFileGroups(dir, dir);
            }
            catch (Exception ex) { log("重建 Mod 索引失败: " + ex.Message); }
        }
        private static void RebuildOption(string profileRoot, MainModItem main, OptionItem opt, Action<string> log)
        {
            try
            {
                var root = MainDir(profileRoot, main.Name); var dir = OptionDir(profileRoot, main.Name, opt.Name); if (!Directory.Exists(dir)) return;
                opt.FileGroups = FileGroupScanner.GetModFileGroups(root, dir);
            }
            catch (Exception ex) { log("重建 选项 索引失败: " + ex.Message); }
        }
        private static void RebuildSub(string profileRoot, MainModItem main, OptionItem opt, SubOptionItem sub, Action<string> log)
        {
            try
            {
                var root = MainDir(profileRoot, main.Name); var dir = SubDir(profileRoot, main.Name, opt.Name, sub.Name); if (!Directory.Exists(dir)) return;
                sub.FileGroups = FileGroupScanner.GetModFileGroups(root, dir);
            }
            catch (Exception ex) { log("重建 子选项 索引失败: " + ex.Message); }
        }
        #endregion

        #region Transform Ops
        private static void MoveOptionsToMain(string profileRoot, IEnumerable<OptionItem> opts, MainModItem newParent, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = opts.ToList(); if (list.Count == 0) return;
            foreach (var opt in list)
            {
                var oldParent = roots.FirstOrDefault(m => m.Options.Contains(opt)); if (oldParent == null) continue;
                if (oldParent != newParent)
                {
                    SafeMoveDir(OptionDir(profileRoot, oldParent.Name, opt.Name), OptionDir(profileRoot, newParent.Name, opt.Name), log);
                    opt.Image = StripPrefix(opt.Image, opt.Name); opt.IconPath = StripPrefix(opt.IconPath, opt.Name);
                }
                oldParent.Options.Remove(opt);
                newParent.Options.Add(opt);
                RebuildOption(profileRoot, newParent, opt, log);
            }
            log($"移动 选项 -> {newParent.Name}");
        }

        private static void MoveOptionsToSiblingPlacement(string profileRoot, IEnumerable<OptionItem> opts, OptionItem anchorOpt, TreePlacement placement, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var newParent = roots.First(m => m.Options.Contains(anchorOpt));
            int anchorIndex = newParent.Options.IndexOf(anchorOpt);
            if (anchorIndex < 0) anchorIndex = newParent.Options.Count - 1;
            int insertAt = placement == TreePlacement.After ? anchorIndex + 1 : anchorIndex;
            if (insertAt < 0) insertAt = 0; if (insertAt > newParent.Options.Count) insertAt = newParent.Options.Count;

            var moving = opts.ToList();
            // preserve original relative order
            var ordered = moving.OrderBy(o =>
            {
                var p = roots.FirstOrDefault(m => m.Options.Contains(o));
                return p != null ? p.Options.IndexOf(o) : int.MaxValue;
            }).ToList();

            foreach (var opt in ordered)
            {
                var oldParent = roots.FirstOrDefault(m => m.Options.Contains(opt)); if (oldParent == null) continue;
                if (oldParent != newParent)
                {
                    SafeMoveDir(OptionDir(profileRoot, oldParent.Name, opt.Name), OptionDir(profileRoot, newParent.Name, opt.Name), log);
                    opt.Image = StripPrefix(opt.Image, opt.Name); opt.IconPath = StripPrefix(opt.IconPath, opt.Name);
                    oldParent.Options.Remove(opt);
                }
                else
                {
                    // same parent but triggered via cross-parent path shouldn't happen, still remove before inserting
                    oldParent.Options.Remove(opt);
                }
                if (insertAt > newParent.Options.Count) insertAt = newParent.Options.Count;
                newParent.Options.Insert(insertAt++, opt);
                RebuildOption(profileRoot, newParent, opt, log);
            }
            log("移动 选项 -> ? Main ?????λ??");
        }

        private static void MoveSubsToOption(string profileRoot, IEnumerable<SubOptionItem> subs, OptionItem newParentOpt, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = subs.ToList(); if (list.Count == 0) return;
            var parentMain = roots.First(m => m.Options.Contains(newParentOpt));
            foreach (var sub in list)
            {
                var oldOpt = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub)); if (oldOpt == null) continue;
                var oldMain = roots.First(m => m.Options.Contains(oldOpt));
                oldOpt.SubOptions.Remove(sub);
                if (oldOpt != newParentOpt)
                {
                    SafeMoveDir(SubDir(profileRoot, oldMain.Name, oldOpt.Name, sub.Name), SubDir(profileRoot, parentMain.Name, newParentOpt.Name, sub.Name), log);
                    sub.Image = AddPrefix(StripPrefix(sub.Image, oldOpt.Name + "/" + sub.Name), newParentOpt.Name + "/" + sub.Name);
                    sub.IconPath = AddPrefix(StripPrefix(sub.IconPath, oldOpt.Name + "/" + sub.Name), newParentOpt.Name + "/" + sub.Name);
                }
                newParentOpt.SubOptions.Add(sub);
                RebuildSub(profileRoot, parentMain, newParentOpt, sub, log);
            }
            log($"移动 子选项 -> {newParentOpt.Name}");
        }

        private static void PromoteSubsToOptions(string profileRoot, IEnumerable<SubOptionItem> subs, MainModItem newParent, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = subs.ToList(); if (list.Count == 0) return;
            foreach (var sub in list)
            {
                var oldOpt = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub)); if (oldOpt == null) continue;
                var oldMain = roots.First(m => m.Options.Contains(oldOpt));
                oldOpt.SubOptions.Remove(sub);
                SafeMoveDir(SubDir(profileRoot, oldMain.Name, oldOpt.Name, sub.Name), OptionDir(profileRoot, newParent.Name, sub.Name), log);
                var opt = new OptionItem
                {
                    Name = sub.Name,
                    Description = sub.Description,
                    Image = StripPrefix(sub.Image, oldOpt.Name + "/" + sub.Name),
                    IconPath = StripPrefix(sub.IconPath, oldOpt.Name + "/" + sub.Name),
                    FileGroups = sub.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>()
                };
                newParent.Options.Add(opt);
                RebuildOption(profileRoot, newParent, opt, log);
            }
            log($"子选项提升为 选项 -> {newParent.Name}");
        }

        private static void DemoteOptionsToSub(string profileRoot, IEnumerable<OptionItem> opts, OptionItem targetOption, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = opts.ToList(); if (list.Count == 0) return;
            var parentMain = roots.First(m => m.Options.Contains(targetOption));
            foreach (var opt in list)
            {
                var oldParent = roots.FirstOrDefault(m => m.Options.Contains(opt)); if (oldParent == null) continue;
                oldParent.Options.Remove(opt);
                SafeMoveDir(OptionDir(profileRoot, oldParent.Name, opt.Name), SubDir(profileRoot, parentMain.Name, targetOption.Name, opt.Name), log);
                var sub = new SubOptionItem
                {
                    Name = opt.Name,
                    Description = opt.Description,
                    Image = AddPrefix(opt.Image, targetOption.Name + "/" + opt.Name),
                    IconPath = AddPrefix(opt.IconPath, targetOption.Name + "/" + opt.Name),
                    FileGroups = opt.FileGroups?.ToList() ?? new List<ModFileGroup>()
                };
                if (opt.SubOptions.Any()) log($"注意: 转为子选项时丢弃原 SubOptions: {opt.Name}");
                targetOption.SubOptions.Add(sub);
                RebuildSub(profileRoot, parentMain, targetOption, sub, log);
            }
            log($"选项降为 子选项 -> {targetOption.Name}");
        }

        private static void DemoteMainsIntoMain(string profileRoot, IEnumerable<MainModItem> mains, MainModItem targetMain, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = mains.Where(m => m != targetMain).ToList(); if (list.Count == 0) return;
            foreach (var main in list)
            {
                roots.Remove(main);
                SafeMoveDir(MainDir(profileRoot, main.Name), OptionDir(profileRoot, targetMain.Name, main.Name), log);
                var newOpt = new OptionItem
                {
                    Name = main.Name,
                    Description = main.Description,
                    IconPath = string.IsNullOrWhiteSpace(main.IconPath) ? string.Empty : AddPrefix(main.IconPath, main.Name),
                    Image = string.IsNullOrWhiteSpace(main.Image) ? string.Empty : AddPrefix(main.Image, main.Name),
                    FileGroups = main.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>()
                };
                foreach (var oldOpt in main.Options)
                {
                    var sub = new SubOptionItem
                    {
                        Name = oldOpt.Name,
                        Description = oldOpt.Description,
                        IconPath = AddPrefix(oldOpt.IconPath, main.Name + "/" + oldOpt.Name),
                        Image = AddPrefix(oldOpt.Image, main.Name + "/" + oldOpt.Name),
                        FileGroups = oldOpt.FileGroups?.ToList() ?? new List<ModFileGroup>()
                    };
                    newOpt.SubOptions.Add(sub);
                }
                targetMain.Options.Add(newOpt);
                RebuildOption(profileRoot, targetMain, newOpt, log);
            }
            log($"Mod 降为 选项 -> {targetMain.Name}");
        }

        // 新增：Sub 在 Option 的 Before/After -> 晋升为同 Main 下的 Option，并插入到指定位置
        private static void PromoteSubsToSiblingOptions(string profileRoot, IEnumerable<SubOptionItem> subs, ObservableCollection<MainModItem> roots, OptionItem anchorOpt, TreePlacement placement, Action<string> log)
        {
            var parentMain = roots.First(m => m.Options.Contains(anchorOpt));
            int insertIndex = parentMain.Options.IndexOf(anchorOpt);
            if (placement == TreePlacement.After) insertIndex++;
            foreach (var sub in subs.ToList())
            {
                var oldOpt = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub)); if (oldOpt == null) continue;
                var oldMain = roots.First(m => m.Options.Contains(oldOpt));
                oldOpt.SubOptions.Remove(sub);
                // 移动物理目录到新 Main 下的 Option 目录
                SafeMoveDir(SubDir(profileRoot, oldMain.Name, oldOpt.Name, sub.Name), OptionDir(profileRoot, parentMain.Name, sub.Name), log);
                var newOpt = new OptionItem
                {
                    Name = sub.Name,
                    Description = sub.Description,
                    IconPath = StripPrefix(sub.IconPath, oldOpt.Name + "/" + sub.Name),
                    Image = StripPrefix(sub.Image, oldOpt.Name + "/" + sub.Name),
                    FileGroups = sub.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>()
                };
                if (insertIndex < 0 || insertIndex > parentMain.Options.Count) insertIndex = parentMain.Options.Count;
                parentMain.Options.Insert(insertIndex++, newOpt);
                RebuildOption(profileRoot, parentMain, newOpt, log);
            }
            log($"子选项提升为 选项 (同 Main) -> {parentMain.Name}");
        }

        // 新增：Sub 在 Sub 的 Before/After -> 作为目标 Sub 所在 Option 的子选项并按位置插入（可跨 Main/Option）
        private static void MoveSubsToSiblingPlacement(string profileRoot, IEnumerable<SubOptionItem> subs, SubOptionItem anchorSub, TreePlacement placement, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var newParentOpt = roots.SelectMany(m => m.Options).First(o => o.SubOptions.Contains(anchorSub));
            var parentMain = roots.First(m => m.Options.Contains(newParentOpt));
            int anchorIndex = newParentOpt.SubOptions.IndexOf(anchorSub);
            int insertAt = placement == TreePlacement.After ? anchorIndex + 1 : anchorIndex;
            if (insertAt < 0) insertAt = 0; if (insertAt > newParentOpt.SubOptions.Count) insertAt = newParentOpt.SubOptions.Count;

            // 保持原始顺序
            var ordered = subs.OrderBy(s =>
            {
                var p = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(s));
                return p != null ? p.SubOptions.IndexOf(s) : int.MaxValue;
            }).ToList();

            foreach (var sub in ordered)
            {
                var oldOpt = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub)); if (oldOpt == null) continue;
                var oldMain = roots.First(m => m.Options.Contains(oldOpt));
                oldOpt.SubOptions.Remove(sub);
                if (!ReferenceEquals(oldOpt, newParentOpt))
                {
                    SafeMoveDir(SubDir(profileRoot, oldMain.Name, oldOpt.Name, sub.Name), SubDir(profileRoot, parentMain.Name, newParentOpt.Name, sub.Name), log);
                    sub.Image = AddPrefix(StripPrefix(sub.Image, oldOpt.Name + "/" + sub.Name), newParentOpt.Name + "/" + sub.Name);
                    sub.IconPath = AddPrefix(StripPrefix(sub.IconPath, oldOpt.Name + "/" + sub.Name), newParentOpt.Name + "/" + sub.Name);
                }
                if (insertAt > newParentOpt.SubOptions.Count) insertAt = newParentOpt.SubOptions.Count;
                newParentOpt.SubOptions.Insert(insertAt++, sub);
                RebuildSub(profileRoot, parentMain, newParentOpt, sub, log);
            }
            log($"子选项移动到目标子选项位置 -> {parentMain.Name}/{newParentOpt.Name}");
        }

        // Main 拖入 Option 内部 -> 作为该 Option 的 SubOption
        private static void DemoteMainsIntoOptionSub(string profileRoot, IEnumerable<MainModItem> mains, OptionItem targetOpt, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var parentMain = roots.First(m => m.Options.Contains(targetOpt));
            foreach (var main in mains.ToList())
            {
                roots.Remove(main);
                SafeMoveDir(MainDir(profileRoot, main.Name), SubDir(profileRoot, parentMain.Name, targetOpt.Name, main.Name), log);
                var sub = new SubOptionItem
                {
                    Name = main.Name,
                    Description = main.Description,
                    IconPath = string.IsNullOrWhiteSpace(main.IconPath) ? string.Empty : AddPrefix(main.IconPath, targetOpt.Name + "/" + main.Name),
                    Image = string.IsNullOrWhiteSpace(main.Image) ? string.Empty : AddPrefix(main.Image, targetOpt.Name + "/" + main.Name),
                    FileGroups = main.FileGroups?.ToList() ?? new List<ModFileGroup>()
                };
                targetOpt.SubOptions.Add(sub);
                RebuildSub(profileRoot, parentMain, targetOpt, sub, log);
            }
            log($"Mod 降为 子选项 -> {parentMain.Name}/{targetOpt.Name}");
        }

        // Option/Sub 晋升为 Main
        private static void PromoteOptionsToMains(string profileRoot, IEnumerable<OptionItem> opts, ObservableCollection<MainModItem> roots, MainModItem anchorMain, TreePlacement placement, Action<string> log)
        {
            int insertIndex = roots.IndexOf(anchorMain); if (placement == TreePlacement.After) insertIndex++;
            var list = opts.ToList(); foreach (var opt in list)
            {
                var parent = roots.FirstOrDefault(m => m.Options.Contains(opt)); if (parent == null) continue;
                parent.Options.Remove(opt);
                SafeMoveDir(OptionDir(profileRoot, parent.Name, opt.Name), MainDir(profileRoot, opt.Name), log);
                var newMain = new MainModItem
                {
                    Name = opt.Name,
                    Description = opt.Description,
                    IconPath = StripPrefix(opt.IconPath, opt.Name),
                    Image = StripPrefix(opt.Image, opt.Name),
                    FileGroups = opt.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    Options = new System.Collections.ObjectModel.ObservableCollection<OptionItem>()
                };
                foreach (var sub in opt.SubOptions)
                {
                    SafeMoveDir(SubDir(profileRoot, parent.Name, opt.Name, sub.Name), OptionDir(profileRoot, opt.Name, sub.Name), log);
                    var newOpt = new OptionItem
                    {
                        Name = sub.Name,
                        Description = sub.Description,
                        IconPath = StripPrefix(sub.IconPath, opt.Name + "/" + sub.Name),
                        Image = StripPrefix(sub.Image, opt.Name + "/" + sub.Name),
                        FileGroups = sub.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                        SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>()
                    };
                    newMain.Options.Add(newOpt);
                }
                if (insertIndex < 0 || insertIndex > roots.Count) insertIndex = roots.Count;
                roots.Insert(insertIndex++, newMain);
                RebuildMain(profileRoot, newMain, log);
            }
            log("选项提升为 Mod 结构");
        }

        private static void PromoteSubsToMains(string profileRoot, IEnumerable<SubOptionItem> subs, ObservableCollection<MainModItem> roots, MainModItem anchorMain, TreePlacement placement, Action<string> log)
        {
            int insertIndex = roots.IndexOf(anchorMain); if (placement == TreePlacement.After) insertIndex++;
            var list = subs.ToList(); foreach (var sub in list)
            {
                var opt = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub)); if (opt == null) continue;
                var main = roots.First(m => m.Options.Contains(opt));
                opt.SubOptions.Remove(sub);
                SafeMoveDir(SubDir(profileRoot, main.Name, opt.Name, sub.Name), MainDir(profileRoot, sub.Name), log);
                var newMain = new MainModItem
                {
                    Name = sub.Name,
                    Description = sub.Description,
                    IconPath = StripPrefix(sub.IconPath, opt.Name + "/" + sub.Name),
                    Image = StripPrefix(sub.Image, opt.Name + "/" + sub.Name),
                    FileGroups = sub.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    Options = new System.Collections.ObjectModel.ObservableCollection<OptionItem>()
                };
                if (insertIndex < 0 || insertIndex > roots.Count) insertIndex = roots.Count;
                roots.Insert(insertIndex++, newMain);
                RebuildMain(profileRoot, newMain, log);
            }
            log("子选项提升为 Mod 结构");
        }
        #endregion
    }
}

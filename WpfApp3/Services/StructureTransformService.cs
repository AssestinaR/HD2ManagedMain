using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using LiberTeaManager.UI.Drag;

namespace LiberTeaManager.Services
{
    /// <summary>
    /// Unified structure transformation service: reorder, move, promote, demote across Main / Option / SubOption levels.
    /// </summary>
    internal interface IStructureTransformService
    {
        StructureOpResult Execute(IEnumerable<object> dragged,
            object? target,
            TreeDragManager.TreePlacement placement,
            ObservableCollection<MainModItem> roots,
            Action<string> log);
    }

    internal enum StructureOpResult { None, Reordered, Moved, Promoted, Demoted }

    internal sealed class StructureTransformService : IStructureTransformService
    {
        public StructureOpResult Execute(IEnumerable<object> dragged,
            object? target,
            TreeDragManager.TreePlacement placement,
            ObservableCollection<MainModItem> roots,
            Action<string> log)
        {
            var list = dragged?.Distinct().ToList() ?? new List<object>();
            if (list.Count == 0) return StructureOpResult.None;
            if (list.Select(GetKind).Distinct().Count() > 1) return StructureOpResult.None; // 不混合类型
            var kind = GetKind(list.First());
            var targetKind = GetKind(target);
            // 取消判定
            if (target != null)
            {
                if (list.Contains(target)) return StructureOpResult.None;
                foreach (var d in list)
                    if (IsAncestorOf(d, target, roots)) return StructureOpResult.None;
            }
            if (target == null)
            {
                switch (kind)
                {
                    case NodeKind.Option:
                        PromoteOptionsToMains(list.Cast<OptionItem>(), roots, roots.Count, log);
                        return StructureOpResult.Promoted;
                    case NodeKind.Sub:
                        PromoteSubsToMains(list.Cast<SubOptionItem>(), roots, roots.Count, log);
                        return StructureOpResult.Promoted;
                }
                return StructureOpResult.None;
            }
            if (placement is TreeDragManager.TreePlacement.Before or TreeDragManager.TreePlacement.After && kind == targetKind)
            {
                if (kind == NodeKind.Option)
                {
                    var targetParent = GetParentOptionContainer(target, roots);
                    if (targetParent != null)
                    {
                        MoveOptionsAndInsert(list.Cast<OptionItem>(), targetParent, (OptionItem)target, placement, roots, log);
                        foreach (var o in list.Cast<OptionItem>()) RebuildFileGroups(targetParent, o, log);
                        return StructureOpResult.Moved;
                    }
                }
                if (kind == NodeKind.Sub)
                {
                    var targetOpt = GetParentSubContainer(target, roots);
                    if (targetOpt != null)
                    {
                        ReorderSameLevel(list, target, placement, roots);
                        log("已重排子选项");
                        return StructureOpResult.Reordered;
                    }
                }
                if (kind == NodeKind.Main)
                {
                    ReorderSameLevel(list, target, placement, roots);
                    log("已重排主Mod");
                    return StructureOpResult.Reordered;
                }
            }
            if (placement == TreeDragManager.TreePlacement.Inside)
            {
                switch (kind)
                {
                    case NodeKind.Option:
                        if (target is MainModItem mainT)
                        {
                            var res = MoveOptionsToMain(list.Cast<OptionItem>(), mainT, roots, log);
                            foreach (var opt in list.Cast<OptionItem>()) RebuildFileGroups(mainT, opt, log);
                            return res;
                        }
                        if (target is OptionItem optT)
                        {
                            DemoteOptionsToSub(list.Cast<OptionItem>(), optT, roots, log);
                            var parentMain = roots.First(m => m.Options.Contains(optT));
                            foreach (var sub in optT.SubOptions) if (list.OfType<OptionItem>().Any(o => o.Name == sub.Name)) RebuildFileGroups(parentMain, optT, sub, log);
                            return StructureOpResult.Demoted;
                        }
                        break;
                    case NodeKind.Sub:
                        if (target is OptionItem optTarget)
                        {
                            var res = MoveSubsToOption(list.Cast<SubOptionItem>(), optTarget, roots, log);
                            var main = roots.First(m => m.Options.Contains(optTarget));
                            foreach (var s in list.Cast<SubOptionItem>()) RebuildFileGroups(main, optTarget, s, log);
                            return res;
                        }
                        if (target is MainModItem mainForPromote)
                        {
                            var res = PromoteSubsToOptions(list.Cast<SubOptionItem>(), mainForPromote, roots, log);
                            foreach (var opt in mainForPromote.Options) if (list.OfType<SubOptionItem>().Any(s => s.Name == opt.Name)) RebuildFileGroups(mainForPromote, opt, log);
                            return res;
                        }
                        break;
                    case NodeKind.Main:
                        if (target is MainModItem otherMain)
                        {
                            var res = DemoteMainsIntoMain(list.Cast<MainModItem>(), otherMain, roots, log);
                            foreach (var opt in otherMain.Options) if (list.OfType<MainModItem>().Any(m => m.Name == opt.Name)) RebuildFileGroups(otherMain, opt, log);
                            return res;
                        }
                        if (target is OptionItem optInside)
                        {
                            DemoteMainsToSub(list.Cast<MainModItem>(), optInside, roots, log);
                            var main = roots.First(m => m.Options.Contains(optInside));
                            foreach (var sub in optInside.SubOptions) if (list.OfType<MainModItem>().Any(m => m.Name == sub.Name)) RebuildFileGroups(main, optInside, sub, log);
                            return StructureOpResult.Demoted;
                        }
                        break;
                }
            }
            if (kind == NodeKind.Option && target is MainModItem mainTarget && placement is TreeDragManager.TreePlacement.Before or TreeDragManager.TreePlacement.After)
            {
                int insertIndex = roots.IndexOf(mainTarget);
                if (placement == TreeDragManager.TreePlacement.After) insertIndex++;
                PromoteOptionsToMains(list.Cast<OptionItem>(), roots, insertIndex, log);
                foreach (var main in list.Cast<OptionItem>())
                {
                    var newMain = roots.FirstOrDefault(r => r.Name == main.Name);
                    if (newMain != null) RebuildFileGroups(newMain, log);
                }
                return StructureOpResult.Promoted;
            }
            if (kind == NodeKind.Sub && target is MainModItem mainTarget2 && placement is TreeDragManager.TreePlacement.Before or TreeDragManager.TreePlacement.After)
            {
                int insertIndex = roots.IndexOf(mainTarget2);
                if (placement == TreeDragManager.TreePlacement.After) insertIndex++;
                PromoteSubsToMains(list.Cast<SubOptionItem>(), roots, insertIndex, log);
                foreach (var sub in list.Cast<SubOptionItem>())
                {
                    var newMain = roots.FirstOrDefault(r => r.Name == sub.Name);
                    if (newMain != null) RebuildFileGroups(newMain, log);
                }
                return StructureOpResult.Promoted;
            }
            return StructureOpResult.None;
        }

        #region Kind / Parent helpers
        private enum NodeKind { Unknown, Main, Option, Sub }
        private static NodeKind GetKind(object? o) => o switch
        {
            MainModItem => NodeKind.Main,
            OptionItem => NodeKind.Option,
            SubOptionItem => NodeKind.Sub,
            _ => NodeKind.Unknown
        };

        private static object? GetParent(object item, ObservableCollection<MainModItem> roots)
        {
            if (item is MainModItem) return null;
            if (item is OptionItem opt) return roots.FirstOrDefault(m => m.Options.Contains(opt));
            if (item is SubOptionItem sub) return roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub));
            return null;
        }
        private static MainModItem? GetParentOptionContainer(object? opt, ObservableCollection<MainModItem> roots) => opt is OptionItem o ? roots.FirstOrDefault(m => m.Options.Contains(o)) : null;
        private static OptionItem? GetParentSubContainer(object? sub, ObservableCollection<MainModItem> roots) => sub is SubOptionItem s ? roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(s)) : null;
        #endregion

        #region Reorder
        private static void ReorderSameLevel(List<object> moving, object target, TreeDragManager.TreePlacement placement, ObservableCollection<MainModItem> roots)
        {
            var parent = GetParent(target, roots);
            if (target is MainModItem)
            {
                var snapshot = roots.Cast<object>().ToList();
                ApplyReorder(snapshot, moving, target, placement == TreeDragManager.TreePlacement.After);
                roots.Clear(); foreach (var m in snapshot.Cast<MainModItem>()) roots.Add(m);
            }
            else if (target is OptionItem tOpt && parent is MainModItem pm)
            {
                var snapshot = pm.Options.Cast<object>().ToList();
                ApplyReorder(snapshot, moving, target, placement == TreeDragManager.TreePlacement.After);
                pm.Options.Clear(); foreach (var o in snapshot.Cast<OptionItem>()) pm.Options.Add(o);
            }
            else if (target is SubOptionItem tSub && parent is OptionItem po)
            {
                var snapshot = po.SubOptions.Cast<object>().ToList();
                ApplyReorder(snapshot, moving, target, placement == TreeDragManager.TreePlacement.After);
                po.SubOptions.Clear(); foreach (var s in snapshot.Cast<SubOptionItem>()) po.SubOptions.Add(s);
            }
        }

        private static void ApplyReorder(List<object> snapshot, List<object> moving, object target, bool after)
        {
            moving = moving.OrderBy(i => snapshot.IndexOf(i)).ToList();
            foreach (var m in moving) snapshot.Remove(m);
            int ti = snapshot.IndexOf(target);
            if (ti < 0) return;
            int insertAt = after ? ti + 1 : ti;
            if (insertAt > snapshot.Count) insertAt = snapshot.Count;
            snapshot.InsertRange(insertAt, moving);
        }
        #endregion

        #region FS Helpers
        private static string ModRoot => SettingsContext.ModFolder;
        private static string MainDir(string main) => Path.Combine(ModRoot, main);
        private static string OptionDir(string main, string opt) => Path.Combine(ModRoot, main, opt);
        private static string SubDir(string main, string opt, string sub) => Path.Combine(ModRoot, main, opt, sub);

        private static void SafeMoveDir(string src, string dest, Action<string> log)
        {
            try
            {
                if (!Directory.Exists(src)) return;
                if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (Directory.Exists(dest))
                {
                    // merge contents
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
                log($"FS移动: {src} -> {dest}");
            }
            catch (Exception ex) { log("FS移动失败: " + ex.Message); }
        }

        private static string StripPrefix(string value, string prefix)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            if (value.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return value.Substring(prefix.Length + 1);
            return value;
        }
        private static string AddPrefix(string value, string prefix)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            if (value.Contains('/')) return prefix + "/" + value.Split('/', 2)[1];
            return prefix + "/" + value;
        }

        private static void RebuildFileGroups(MainModItem main, Action<string> log)
        {
            try
            {
                var root = MainDir(main.Name);
                if (Directory.Exists(root))
                    main.FileGroups = ManifestGenerator.GetModFileGroups(root, root);
            }
            catch (Exception ex) { log("重建主Mod文件组失败: " + ex.Message); }
        }
        private static void RebuildFileGroups(MainModItem main, OptionItem opt, Action<string> log)
        {
            try
            {
                var root = MainDir(main.Name);
                var optDir = OptionDir(main.Name, opt.Name);
                if (Directory.Exists(optDir))
                    opt.FileGroups = ManifestGenerator.GetModFileGroups(root, optDir);
            }
            catch (Exception ex) { log("重建选项文件组失败: " + ex.Message); }
        }
        private static void RebuildFileGroups(MainModItem main, OptionItem opt, SubOptionItem sub, Action<string> log)
        {
            try
            {
                var root = MainDir(main.Name);
                var subDir = SubDir(main.Name, opt.Name, sub.Name);
                if (Directory.Exists(subDir))
                    sub.FileGroups = ManifestGenerator.GetModFileGroups(root, subDir);
            }
            catch (Exception ex) { log("重建子选项文件组失败: " + ex.Message); }
        }
        #endregion

        #region Move / Promote / Demote (with FS sync)
        private static StructureOpResult MoveOptionsToMain(IEnumerable<OptionItem> opts, MainModItem newParent, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = opts.ToList(); if (list.Count == 0) return StructureOpResult.None;
            foreach (var opt in list)
            {
                var oldParent = roots.FirstOrDefault(m => m.Options.Contains(opt));
                if (oldParent == null || oldParent == newParent) continue;
                oldParent.Options.Remove(opt);
                SafeMoveDir(OptionDir(oldParent.Name, opt.Name), OptionDir(newParent.Name, opt.Name), log);
                opt.RootModName = newParent.Name;
                newParent.Options.Add(opt);
            }
            log($"移动选项 -> {newParent.Name}");
            return StructureOpResult.Moved;
        }

        private static StructureOpResult MoveSubsToOption(IEnumerable<SubOptionItem> subs, OptionItem newParentOpt, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = subs.ToList(); if (list.Count == 0) return StructureOpResult.None;
            var parentMain = roots.FirstOrDefault(m => m.Options.Contains(newParentOpt));
            foreach (var sub in list)
            {
                var oldOpt = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub));
                if (oldOpt == null || oldOpt == newParentOpt) continue;
                var main = roots.First(m => m.Options.Contains(oldOpt));
                oldOpt.SubOptions.Remove(sub);
                SafeMoveDir(SubDir(main.Name, oldOpt.Name, sub.Name), SubDir(main.Name, newParentOpt.Name, sub.Name), log);
                sub.RootModName = main.Name;
                newParentOpt.SubOptions.Add(sub);
            }
            log($"移动子选项 -> {newParentOpt.Name}");
            return StructureOpResult.Moved;
        }

        private static StructureOpResult PromoteSubsToOptions(IEnumerable<SubOptionItem> subs, MainModItem newParent, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = subs.ToList(); if (list.Count == 0) return StructureOpResult.None;
            foreach (var sub in list)
            {
                var oldOpt = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub));
                if (oldOpt == null) continue;
                var main = roots.First(m => m.Options.Contains(oldOpt));
                oldOpt.SubOptions.Remove(sub);
                SafeMoveDir(SubDir(main.Name, oldOpt.Name, sub.Name), OptionDir(main.Name, sub.Name), log);
                var opt = new OptionItem
                {
                    Name = sub.Name,
                    Description = sub.Description,
                    Image = StripPrefix(sub.Image, oldOpt.Name),
                    IconPath = StripPrefix(sub.IconPath, oldOpt.Name),
                    IsSelected = false,
                    Include = sub.Include?.ToList() ?? new List<string>(),
                    SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>(),
                    FileGroups = sub.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    RootModName = newParent.Name
                };
                newParent.Options.Add(opt);
            }
            log($"子选项->选项 {list.Count}");
            return StructureOpResult.Promoted;
        }

        private static StructureOpResult DemoteMainsIntoMain(IEnumerable<MainModItem> mains, MainModItem targetMain, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = mains.Where(m => m != targetMain).ToList(); if (list.Count == 0) return StructureOpResult.None;
            foreach (var main in list)
            {
                roots.Remove(main);
                SafeMoveDir(MainDir(main.Name), OptionDir(targetMain.Name, main.Name), log);
                var newOpt = new OptionItem
                {
                    Name = main.Name,
                    Description = main.Description,
                    IconPath = string.IsNullOrWhiteSpace(main.IconPath) ? "" : AddPrefix(main.IconPath, main.Name),
                    Image = string.IsNullOrWhiteSpace(main.Image) ? "" : AddPrefix(main.Image, main.Name),
                    Include = new List<string> { main.Name },
                    SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>(),
                    FileGroups = main.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    RootModName = targetMain.Name
                };
                foreach (var oldOpt in main.Options)
                {
                    var sub = new SubOptionItem
                    {
                        Name = oldOpt.Name,
                        Description = oldOpt.Description,
                        IconPath = AddPrefix(oldOpt.IconPath, main.Name),
                        Image = AddPrefix(oldOpt.Image, main.Name),
                        Include = oldOpt.Include?.ToList() ?? new List<string>(),
                        FileGroups = oldOpt.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                        RootModName = targetMain.Name
                    };
                    newOpt.SubOptions.Add(sub);
                    if (oldOpt.SubOptions.Any()) log("注意: 深层子选项被丢弃: " + oldOpt.Name);
                }
                targetMain.Options.Add(newOpt);
            }
            log($"主Mod降级->{targetMain.Name} {list.Count}");
            return StructureOpResult.Demoted;
        }

        private static void DemoteMainsToSub(IEnumerable<MainModItem> mains, OptionItem targetOption, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var parentMain = roots.First(m => m.Options.Contains(targetOption));
            var list = mains.Where(m => roots.Contains(m)).ToList(); if (list.Count == 0) return;
            foreach (var main in list)
            {
                roots.Remove(main);
                SafeMoveDir(MainDir(main.Name), SubDir(parentMain.Name, targetOption.Name, main.Name), log);
                var sub = new SubOptionItem
                {
                    Name = main.Name,
                    Description = main.Description,
                    IconPath = AddPrefix(main.IconPath, targetOption.Name + "/" + main.Name),
                    Image = AddPrefix(main.Image, targetOption.Name + "/" + main.Name),
                    Include = new List<string> { main.Name },
                    FileGroups = main.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    RootModName = parentMain.Name
                };
                if (main.Options.Any()) log("注意: 降级为子选项时丢弃原Option层: " + main.Name);
                targetOption.SubOptions.Add(sub);
            }
            log($"主Mod降级为子选项 -> {targetOption.Name} {list.Count}");
        }

        private static void DemoteOptionsToSub(IEnumerable<OptionItem> opts, OptionItem targetOption, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var list = opts.ToList(); if (list.Count == 0) return;
            var parentMain = roots.First(m => m.Options.Contains(targetOption));
            foreach (var opt in list)
            {
                var parent = roots.FirstOrDefault(m => m.Options.Contains(opt));
                if (parent == null) continue;
                parent.Options.Remove(opt);
                SafeMoveDir(OptionDir(parent.Name, opt.Name), SubDir(parent.Name, targetOption.Name, opt.Name), log);
                var sub = new SubOptionItem
                {
                    Name = opt.Name,
                    Description = opt.Description,
                    IconPath = AddPrefix(opt.IconPath, targetOption.Name + "/" + opt.Name),
                    Image = AddPrefix(opt.Image, targetOption.Name + "/" + opt.Name),
                    Include = opt.Include?.ToList() ?? new List<string>(),
                    FileGroups = opt.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    RootModName = parent.Name
                };
                if (opt.SubOptions.Any()) log("注意: 降级为子选项时丢弃原 SubOptions: " + opt.Name);
                targetOption.SubOptions.Add(sub);
            }
            log($"选项降级为子选项 -> {targetOption.Name} {list.Count}");
        }

        private static void PromoteOptionsToMains(IEnumerable<OptionItem> opts, ObservableCollection<MainModItem> roots, int insertIndex, Action<string> log)
        {
            var list = opts.ToList(); if (list.Count == 0) return;
            foreach (var opt in list)
            {
                var parent = roots.FirstOrDefault(m => m.Options.Contains(opt));
                if (parent == null) continue;
                parent.Options.Remove(opt);
                SafeMoveDir(OptionDir(parent.Name, opt.Name), MainDir(opt.Name), log);
                var newMain = new MainModItem
                {
                    Name = opt.Name,
                    Description = opt.Description,
                    IconPath = StripPrefix(opt.IconPath, opt.Name),
                    Image = StripPrefix(opt.Image, opt.Name),
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Enabled = EnabledState.Disabled,
                    FileGroups = opt.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    Options = new ObservableCollection<OptionItem>(),
                    RootModName = opt.Name
                };
                foreach (var sub in opt.SubOptions)
                {
                    // suboption becomes option; move folder
                    SafeMoveDir(SubDir(parent.Name, opt.Name, sub.Name), OptionDir(opt.Name, sub.Name), log);
                    var newOpt = new OptionItem
                    {
                        Name = sub.Name,
                        Description = sub.Description,
                        IconPath = StripPrefix(sub.IconPath, opt.Name + "/" + sub.Name),
                        Image = StripPrefix(sub.Image, opt.Name + "/" + sub.Name),
                        IsSelected = false,
                        Include = sub.Include?.ToList() ?? new List<string>(),
                        SubOptions = new ObservableCollection<SubOptionItem>(),
                        FileGroups = sub.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                        RootModName = newMain.Name
                    };
                    newMain.Options.Add(newOpt);
                }
                if (insertIndex < 0 || insertIndex > roots.Count) insertIndex = roots.Count;
                roots.Insert(insertIndex++, newMain);
            }
            log($"选项提升为主Mod {list.Count}");
        }

        private static void PromoteSubsToMains(IEnumerable<SubOptionItem> subs, ObservableCollection<MainModItem> roots, int insertIndex, Action<string> log)
        {
            var list = subs.ToList(); if (list.Count == 0) return;
            foreach (var sub in list)
            {
                var opt = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub));
                if (opt == null) continue;
                var main = roots.First(m => m.Options.Contains(opt));
                opt.SubOptions.Remove(sub);
                SafeMoveDir(SubDir(main.Name, opt.Name, sub.Name), MainDir(sub.Name), log);
                var newMain = new MainModItem
                {
                    Name = sub.Name,
                    Description = sub.Description,
                    IconPath = StripPrefix(sub.IconPath, opt.Name + "/" + sub.Name),
                    Image = StripPrefix(sub.Image, opt.Name + "/" + sub.Name),
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Enabled = EnabledState.Disabled,
                    FileGroups = sub.FileGroups?.ToList() ?? new List<ModFileGroup>(),
                    Options = new ObservableCollection<OptionItem>(),
                    RootModName = sub.Name
                };
                if (insertIndex < 0 || insertIndex > roots.Count) insertIndex = roots.Count;
                roots.Insert(insertIndex++, newMain);
            }
            log($"子选项提升为主Mod {list.Count}");
        }

        private static void MoveOptionsAndInsert(IEnumerable<OptionItem> opts, MainModItem newParent, OptionItem anchor, TreeDragManager.TreePlacement placement, ObservableCollection<MainModItem> roots, Action<string> log)
        {
            var moving = opts.ToList(); if (moving.Count == 0) return;
            var ordered = moving.OrderBy(o => newParent.Options.IndexOf(o)).ToList();
            foreach (var o in ordered)
            {
                var p = roots.FirstOrDefault(m => m.Options.Contains(o));
                if (p != null)
                {
                    if (p != newParent)
                        SafeMoveDir(OptionDir(p.Name, o.Name), OptionDir(newParent.Name, o.Name), log);
                    p.Options.Remove(o);
                }
            }
            int anchorIndex = newParent.Options.IndexOf(anchor);
            if (anchorIndex < 0) anchorIndex = newParent.Options.Count;
            int insertAt = placement == TreeDragManager.TreePlacement.After ? anchorIndex + 1 : anchorIndex;
            if (insertAt > newParent.Options.Count) insertAt = newParent.Options.Count;
            foreach (var o in ordered)
            {
                o.RootModName = newParent.Name;
                newParent.Options.Insert(insertAt++, o);
            }
            log("跨父级移动/排序选项");
        }
        #endregion

        // ancestor check helper (added for cancel detection)
        private static bool IsAncestorOf(object possibleAncestor, object node, ObservableCollection<MainModItem> roots)
        {
            if (possibleAncestor == node) return true;
            if (possibleAncestor is MainModItem m)
            {
                if (node is OptionItem o && m.Options.Contains(o)) return true;
                if (node is SubOptionItem s && m.Options.Any(o2 => o2.SubOptions.Contains(s))) return true;
            }
            if (possibleAncestor is OptionItem opt)
            {
                if (node is SubOptionItem s2 && opt.SubOptions.Contains(s2)) return true;
            }
            return false;
        }
    }
}

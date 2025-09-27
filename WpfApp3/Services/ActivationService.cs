using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace LiberTeaManager.Services
{
    internal sealed class ActivationService : IActivationService
    {
        private readonly ObservableCollection<MainModItem> _mods;
        private readonly IPatchLinkService _patch;
        private readonly ILogService _log;

        public ActivationService(ObservableCollection<MainModItem> mods, IPatchLinkService patchService, ILogService log)
        {
            _mods = mods; _patch = patchService; _log = log;
        }

        private IEnumerable<(MainModItem Mod, object Node)> GetSelectedNodes()
        {
            foreach (var m in _mods.Where(m => m.IsSelected)) yield return (m, (object)m);
            foreach (var mod in _mods)
            {
                foreach (var opt in mod.Options.Where(o => o.IsSelected)) yield return (mod, (object)opt);
                foreach (var opt in mod.Options)
                    foreach (var sub in opt.SubOptions.Where(s => s.IsSelected)) yield return (mod, (object)sub);
            }
        }

        private void SetNodeAndDescendantsEnabled(object node, EnabledState value)
        { switch (node) { case MainModItem m: m.Enabled = value; foreach (var o in m.Options) SetNodeAndDescendantsEnabled(o, value); break; case OptionItem o: o.Enabled = value; foreach (var s in o.SubOptions) SetNodeAndDescendantsEnabled(s, value); break; case SubOptionItem s: s.Enabled = value; break; } }

        private void UpdateAggregateEnabledStates()
        {
            foreach (var mod in _mods)
            {
                foreach (var opt in mod.Options)
                {
                    bool anySubEnabled = opt.SubOptions.Any(s => s.Enabled == EnabledState.Enabled);
                    bool allSubEnabled = opt.SubOptions.Any() && opt.SubOptions.All(s => s.Enabled == EnabledState.Enabled);
                    bool selfEnabled = opt.Enabled == EnabledState.Enabled;
                    if (selfEnabled && allSubEnabled) opt.Enabled = EnabledState.Enabled;
                    else if (selfEnabled && anySubEnabled && !allSubEnabled) opt.Enabled = EnabledState.Partial;
                    else if (!selfEnabled && anySubEnabled) opt.Enabled = EnabledState.Partial;
                    else if (!selfEnabled && !anySubEnabled) opt.Enabled = EnabledState.Disabled;
                }
                bool anyOptEnabled = mod.Options.Any(o => o.Enabled == EnabledState.Enabled);
                bool anyOptPartial = mod.Options.Any(o => o.Enabled == EnabledState.Partial);
                bool allOptEnabled = mod.Options.Any() && mod.Options.All(o => o.Enabled == EnabledState.Enabled);
                bool selfEnabledMod = mod.Enabled == EnabledState.Enabled;
                if ((selfEnabledMod && allOptEnabled) || (selfEnabledMod && !mod.Options.Any())) mod.Enabled = EnabledState.Enabled;
                else if (selfEnabledMod && (anyOptEnabled || anyOptPartial) && !allOptEnabled) mod.Enabled = EnabledState.Partial;
                else if (!selfEnabledMod && (anyOptEnabled || anyOptPartial)) mod.Enabled = EnabledState.Partial;
                else if (!selfEnabledMod && !anyOptEnabled && !anyOptPartial) mod.Enabled = EnabledState.Disabled;
            }
        }

        public async Task<(int hard, int sym, int copy)> EnableSelectedAsync(bool logPerGroup)
        {
            _log.Log("开始启用...");
            await Task.Run(() =>
            {
                var selected = GetSelectedNodes().ToList();
                foreach (var (mod, node) in selected)
                {
                    var state = node switch { MainModItem mm => mm.Enabled, OptionItem oo => oo.Enabled, SubOptionItem ss => ss.Enabled, _ => EnabledState.Enabled };
                    if (state == EnabledState.Enabled) continue;
                    if (state == EnabledState.Partial)
                    {
                        switch (node)
                        {
                            case MainModItem mm:
                                foreach (var opt in mm.Options) if (opt.Enabled != EnabledState.Enabled) SetNodeAndDescendantsEnabled(opt, EnabledState.Enabled);
                                SetNodeAndDescendantsEnabled(mm, EnabledState.Enabled);
                                break;
                            case OptionItem oo:
                                foreach (var sub in oo.SubOptions) if (sub.Enabled != EnabledState.Enabled) SetNodeAndDescendantsEnabled(sub, EnabledState.Enabled);
                                SetNodeAndDescendantsEnabled(oo, EnabledState.Enabled);
                                break;
                            case SubOptionItem ss:
                                SetNodeAndDescendantsEnabled(ss, EnabledState.Enabled);
                                break;
                        }
                    }
                    else if (state == EnabledState.Disabled)
                    {
                        SetNodeAndDescendantsEnabled(node, EnabledState.Enabled);
                    }
                }
            });
            UpdateAggregateEnabledStates();
            await _patch.ReorderAndLinkAsync(fullRebuild: true, logPerGroup: logPerGroup);
            _log.Log($"启用完成 - 硬链接: {_patch.HardLinkCount}  符号链接: {_patch.SymLinkCount}  复制: {_patch.CopyCount}");
            return (_patch.HardLinkCount, _patch.SymLinkCount, _patch.CopyCount);
        }

        public async Task DisableSelectedAsync()
        {
            _log.Log("开始禁用...");
            await Task.Run(() =>
            {
                var queue = new Queue<(MainModItem Mod, object Node)>();
                foreach (var pair in GetSelectedNodes()) queue.Enqueue(pair);
                while (queue.Count > 0)
                {
                    var (mod, node) = queue.Dequeue();
                    var state = node switch { MainModItem mm => mm.Enabled, OptionItem oo => oo.Enabled, SubOptionItem ss => ss.Enabled, _ => EnabledState.Disabled };
                    if (state == EnabledState.Disabled) continue;
                    if (state == EnabledState.Partial)
                    {
                        switch (node)
                        {
                            case MainModItem mm:
                                foreach (var opt in mm.Options) queue.Enqueue((mm, opt));
                                break;
                            case OptionItem oo:
                                foreach (var sub in oo.SubOptions) queue.Enqueue((mod, sub));
                                break;
                            case SubOptionItem:
                                SetNodeAndDescendantsEnabled(node, EnabledState.Disabled);
                                break;
                        }
                        continue;
                    }
                    if (state == EnabledState.Enabled)
                    {
                        SetNodeAndDescendantsEnabled(node, EnabledState.Disabled);
                    }
                }
            });
            UpdateAggregateEnabledStates();
            await _patch.ReorderAndLinkAsync(fullRebuild: true, logPerGroup: false);
            _log.Log("禁用完成");
        }

        public async Task DeleteSelectedAsync()
        {
            _log.Log("开始删除选中项...");
            // 拍快照（避免遍历过程中结构变化）
            var selectedMain = _mods.Where(m => m.IsSelected).ToList();
            var selectedOptions = _mods.SelectMany(m => m.Options.Where(o => o.IsSelected && !m.IsSelected)).ToList();
            var selectedSubs = _mods.SelectMany(m => m.Options.SelectMany(o => o.SubOptions.Where(s => s.IsSelected && !m.IsSelected && !o.IsSelected))).ToList();
            if (!selectedMain.Any() && !selectedOptions.Any() && !selectedSubs.Any()) { _log.Log("未选择任何可删除节点"); return; }

            // 先删除文件系统（按子->父顺序）
            await Task.Run(() =>
            {
                foreach (var sub in selectedSubs)
                {
                    try
                    {
                        var parentOpt = _mods.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub));
                        var parentMain = _mods.FirstOrDefault(m => m.Options.Contains(parentOpt!));
                        if (parentOpt != null && parentMain != null)
                        {
                            var dir = Path.Combine(SettingsContext.ModFolder, parentMain.Name, parentOpt.Name, sub.Name);
                            if (Directory.Exists(dir)) Directory.Delete(dir, true);
                        }
                    }
                    catch (Exception ex) { _log.Log($"删除子选项目录失败 {sub.Name}: {ex.Message}"); }
                }
                foreach (var opt in selectedOptions)
                {
                    try
                    {
                        var parentMain = _mods.FirstOrDefault(m => m.Options.Contains(opt));
                        if (parentMain != null)
                        {
                            var dir = Path.Combine(SettingsContext.ModFolder, parentMain.Name, opt.Name);
                            if (Directory.Exists(dir)) Directory.Delete(dir, true);
                        }
                    }
                    catch (Exception ex) { _log.Log($"删除选项目录失败 {opt.Name}: {ex.Message}"); }
                }
                foreach (var main in selectedMain)
                {
                    try
                    {
                        var dir = Path.Combine(SettingsContext.ModFolder, main.Name);
                        if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    }
                    catch (Exception ex) { _log.Log($"删除主Mod目录失败 {main.Name}: {ex.Message}"); }
                }
            });

            // 内存结构移除（父级集合）
            foreach (var sub in selectedSubs)
            {
                var parentOpt = _mods.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub));
                parentOpt?.SubOptions.Remove(sub);
            }
            foreach (var opt in selectedOptions)
            {
                var parentMain = _mods.FirstOrDefault(m => m.Options.Contains(opt));
                parentMain?.Options.Remove(opt);
            }
            foreach (var main in selectedMain)
            {
                _mods.Remove(main);
            }

            // 保存与补丁重建
            ModListManager.SaveModList(_mods);
            UpdateAggregateEnabledStates();
            await _patch.ReorderAndLinkAsync(fullRebuild: true, logPerGroup: false);
            _log.Log("删除完成");
        }
    }
}

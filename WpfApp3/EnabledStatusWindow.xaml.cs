using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace LiberTeaManager
{
    public partial class EnabledStatusWindow : Window
    {
        private readonly IEnumerable<MainModItem> _mods;
        private readonly string _gameFolder;
        private readonly string _modFolder;
        private readonly Regex _patchRegex = new("^([a-fA-F0-9]{16})\\.patch_(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public ObservableCollection<FileGroupStatus> Items { get; } = new();
        public ObservableCollection<FileGroupStatus> ViewItems { get; } = new();

        public EnabledStatusWindow(IEnumerable<MainModItem> mods, string gameFolder, string modFolder)
        {
            InitializeComponent();
            _mods = mods;
            _gameFolder = gameFolder ?? string.Empty;
            _modFolder = modFolder ?? string.Empty;
            DataContext = this;
            FilterBox.TextChanged += FilterBox_TextChanged;
            RefreshData();
        }

        private void RefreshData()
        {
            Items.Clear();
            ViewItems.Clear();
            if (string.IsNullOrWhiteSpace(_gameFolder) || !Directory.Exists(_gameFolder)) return;
            foreach (var mod in _mods)
            {
                if (mod.Enabled == EnabledState.Enabled) AddGroups(mod.FileGroups, mod, null, null);
                foreach (var opt in mod.Options)
                {
                    if (opt.Enabled == EnabledState.Enabled) AddGroups(opt.FileGroups, mod, opt, null);
                    foreach (var sub in opt.SubOptions)
                        if (sub.Enabled == EnabledState.Enabled) AddGroups(sub.FileGroups, mod, opt, sub);
                }
            }
            foreach (var it in Items) ViewItems.Add(it);
            ApplyFilter();
        }

        private void AddGroups(List<ModFileGroup>? groups, MainModItem mod, OptionItem? opt, SubOptionItem? sub)
        {
            if (groups == null) return;
            foreach (var g in groups)
            {
                if (g == null) continue;
                int gamePatchN = -1; string gameFileMatch = string.Empty;
                foreach (var f in Directory.GetFiles(_gameFolder, g.HexPrefix + ".patch_*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(f)!; var m = _patchRegex.Match(name);
                    if (!m.Success) continue; if (!string.Equals(m.Groups[1].Value, g.HexPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    int pn = int.TryParse(m.Groups[2].Value, out var pnv) ? pnv : -1;
                    if (pn == g.PatchN) { gamePatchN = pn; gameFileMatch = name; break; }
                    if (gamePatchN == -1) { gamePatchN = pn; gameFileMatch = name; }
                }
                bool filesAllExist = true; bool existsInGame = true; int fileCount = g.Files?.Count ?? 0;
                if (g.Files != null)
                {
                    foreach (var rel in g.Files)
                    {
                        var fileName = Path.GetFileName(rel); if (string.IsNullOrEmpty(fileName)) continue;
                        var dest = Path.Combine(_gameFolder, fileName); if (!File.Exists(dest)) { filesAllExist = false; existsInGame = false; break; }
                    }
                }
                string owner = mod.Name; if (opt != null) owner += "/" + opt.Name; if (sub != null) owner += "/" + sub.Name;
                string linkType = "Unknown";
                if (!string.IsNullOrEmpty(gameFileMatch))
                {
                    try
                    {
                        string destFull = Path.Combine(_gameFolder, gameFileMatch);
                        var attr = File.GetAttributes(destFull);
                        linkType = (attr & FileAttributes.ReparsePoint) != 0 ? "Sym" : "Hard/Copy";
                    }
                    catch { }
                }
                Items.Add(new FileGroupStatus
                {
                    OwnerDisplay = owner,
                    HexPrefix = g.HexPrefix,
                    PatchN_ModList = g.PatchN,
                    PatchN_Game = gamePatchN,
                    GameFileName = gameFileMatch,
                    ExistsInGame = existsInGame,
                    FilesAllLinked = filesAllExist,
                    FileCount = fileCount,
                    LinkType = linkType,
                    Tooltip = (existsInGame ? "存在" : "缺失") + (g.Files != null ? $" | 文件数:{g.Files.Count}" : string.Empty)
                });
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshData();
        private void OnlyIssuesCheck_Changed(object sender, RoutedEventArgs e) => ApplyFilter();
        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var text = FilterBox.Text?.Trim() ?? string.Empty;
            bool onlyIssues = OnlyIssuesCheck.IsChecked == true;
            ViewItems.Clear();
            foreach (var it in Items)
            {
                if (!string.IsNullOrEmpty(text) && !it.HexPrefix.Contains(text, StringComparison.OrdinalIgnoreCase) && !it.OwnerDisplay.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
                if (onlyIssues && it.ExistsInGame && !it.Mismatch && it.FilesAllLinked) continue;
                ViewItems.Add(it);
            }
        }

        private void StatusGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (StatusGrid.SelectedItem is FileGroupStatus st && !string.IsNullOrEmpty(st.GameFileName))
            {
                try
                {
                    var full = Path.Combine(_gameFolder, st.GameFileName);
                    if (File.Exists(full)) { Clipboard.SetText(full); MessageBox.Show("已复制: " + full); }
                }
                catch { }
            }
        }
    }

    public class FileGroupStatus
    {
        public string OwnerDisplay { get; set; } = string.Empty;
        public string HexPrefix { get; set; } = string.Empty;
        public int PatchN_ModList { get; set; }
        public int PatchN_Game { get; set; } = -1;
        public string GameFileName { get; set; } = string.Empty;
        public bool ExistsInGame { get; set; }
        public bool FilesAllLinked { get; set; }
        public int FileCount { get; set; }
        public string LinkType { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
        public bool Mismatch => PatchN_Game >= 0 && PatchN_ModList != PatchN_Game;
    }
}

using System.Windows;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Diagnostics;
using System.Windows.Input;
using System.IO;
using System.Globalization;
using LiberTeaManager.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;

namespace LiberTeaManager
{
    public partial class SettingsWindow : Window
    {
        private readonly ISettingsService _settings;
        private readonly ObservableCollection<string> _profileNames = new();

        public SettingsWindow(ISettingsService settings)
        {
            _settings = settings;
            InitializeComponent();

            GameFolderTextBox.Text = _settings.GameFolder;
            MainWidthTextBox.Text = _settings.MainWindowWidth.ToString(CultureInfo.InvariantCulture);
            MainHeightTextBox.Text = _settings.MainWindowHeight.ToString(CultureInfo.InvariantCulture);

            // 初始化配置下拉
            ProfilesCombo.ItemsSource = _profileNames;
            ProfilesCombo.SelectionChanged += ProfilesCombo_SelectionChanged;
            RefreshProfilesUI();

            // 尺寸交互
            this.Closed += SettingsWindow_Closed;
            MainWidthTextBox.KeyDown += MainSizeTextBox_KeyDown;
            MainHeightTextBox.KeyDown += MainSizeTextBox_KeyDown;
            MainWidthTextBox.LostFocus += MainSizeTextBox_LostFocus;
            MainHeightTextBox.LostFocus += MainSizeTextBox_LostFocus;
        }

        private void RefreshProfilesUI()
        {
            _profileNames.Clear();
            if (_settings is SettingsService s)
            {
                foreach (var name in s.ProfileModFolders.Keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().OrderBy(n => n.Equals("default", System.StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(n => n))
                {
                    _profileNames.Add(name);
                }
                if (!_profileNames.Contains(s.CurrentProfile)) _profileNames.Insert(0, s.CurrentProfile);
                ProfilesCombo.SelectedItem = s.CurrentProfile;
                ProfilePathTextBox.Text = s.ProfileModFolders.TryGetValue(s.CurrentProfile, out var p) ? p : s.ModFolder;
            }
        }

        private void ProfilesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settings is not SettingsService s) return;
            var name = ProfilesCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name)) return;
            ProfilePathTextBox.Text = s.ProfileModFolders.TryGetValue(name, out var p) ? p : string.Empty;
        }

        private void SettingsWindow_Closed(object? sender, System.EventArgs e)
        {
            if (Owner is MainWindow mw)
            {
                mw.Width = _settings.MainWindowWidth;
                mw.Height = _settings.MainWindowHeight;
                mw.RefreshModList();
                mw.RefreshProfiles();
            }
        }

        private void SaveGameFolderFromTextBox()
        {
            var path = GameFolderTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(path)) { _settings.GameFolder = path; _settings.Save(); }
        }

        private void GameFolderTextBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { SaveGameFolderFromTextBox(); e.Handled = true; Close(); } }
        private void GameFolderTextBox_LostFocus(object sender, RoutedEventArgs e) => SaveGameFolderFromTextBox();

        private void BtnBrowseGameFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                GameFolderTextBox.Text = dlg.FileName;
                _settings.GameFolder = dlg.FileName;
                _settings.Save();
            }
        }

        private void BtnOpenGameFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = GameFolderTextBox.Text;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            else
                MessageBox.Show("路径不存在或无效。");
        }

        private void SaveMainWindowSizeFromBoxes()
        {
            if (double.TryParse(MainWidthTextBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var w) && w > 100)
                _settings.MainWindowWidth = w;
            if (double.TryParse(MainHeightTextBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var h) && h > 100)
                _settings.MainWindowHeight = h;
            _settings.Save();
        }
        private void MainSizeTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveMainWindowSizeFromBoxes();
                if (Owner is MainWindow mw)
                {
                    mw.Width = _settings.MainWindowWidth;
                    mw.Height = _settings.MainWindowHeight;
                }
                e.Handled = true;
                Close();
            }
        }
        private void MainSizeTextBox_LostFocus(object? sender, RoutedEventArgs e)
        { SaveMainWindowSizeFromBoxes(); }

        private void BtnUseCurrentSize_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow mw)
            {
                _settings.MainWindowWidth = mw.Width;
                _settings.MainWindowHeight = mw.Height;
                _settings.Save();
                MainWidthTextBox.Text = mw.Width.ToString("F0");
                MainHeightTextBox.Text = mw.Height.ToString("F0");
            }
        }
        private void BtnResetSize_Click(object sender, RoutedEventArgs e)
        {
            _settings.MainWindowWidth = 1100;
            _settings.MainWindowHeight = 800;
            _settings.Save();
            MainWidthTextBox.Text = "1100";
            MainHeightTextBox.Text = "800";
            if (Owner is MainWindow mw)
            {
                mw.Width = 1100;
                mw.Height = 800;
            }
        }

        private void BtnNewProfile_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is not MainWindow mw) return;
            var dialog = new SingleInputWindow("新配置文件", "输入新配置文件名称", "profile") { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var name = dialog.ResultText?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            string newModRoot = dlg.SelectedPath;
            try
            {
                var set = mw.GetSettingsService();
                set.CurrentProfile = name;
                set.ModFolder = newModRoot;
                set.ProfileModFolders[name] = newModRoot;
                string listPath = System.IO.Path.Combine(newModRoot, "modlist.json");
                if (!File.Exists(listPath)) File.WriteAllText(listPath, "[]");
                set.Save();
                SettingsContext.Initialize(set);
                mw.RefreshProfiles();
                mw.CurrentProfile = name;
                mw.RefreshModList();
                RefreshProfilesUI();
            }
            catch { }
        }

        private async void BtnCopyProfile_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is not MainWindow mw) return;
            var src = mw.CurrentProfile;
            var dialog = new SingleInputWindow("复制配置文件", $"从 {src} 复制为:", src + "_copy") { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var name = dialog.ResultText?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            var folderDlg = new System.Windows.Forms.FolderBrowserDialog();
            if (folderDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            string newModRoot = folderDlg.SelectedPath;
            (Owner as MainWindow)?.AppendExternalLog($"开始复制配置: {src} -> {name} 目标目录: {newModRoot}");
            try
            {
                var set = mw.GetSettingsService();
                string srcRoot = set.ModFolder;
                int lastPercent = -1;
                await ProfileCloneService.CloneModRootAsync(srcRoot, newModRoot, msg => (Owner as MainWindow)?.AppendExternalLog(msg), (i, t) =>
                {
                    if (t > 0)
                    {
                        int p = (int)(i * 100.0 / t);
                        if (p != lastPercent && (p % 10 == 0 || p == 100)) { lastPercent = p; (Owner as MainWindow)?.AppendExternalLog($"进度 {p}% ({i}/{t})"); }
                    }
                });
                string srcList = System.IO.Path.Combine(srcRoot, "modlist.json");
                string dstList = System.IO.Path.Combine(newModRoot, "modlist.json");
                if (File.Exists(srcList) && !File.Exists(dstList)) File.Copy(srcList, dstList, false);
                if (!File.Exists(dstList)) File.WriteAllText(dstList, "[]");
                set.CurrentProfile = name;
                set.ModFolder = newModRoot;
                set.ProfileModFolders[name] = newModRoot;
                set.Save();
                SettingsContext.Initialize(set);
                (Owner as MainWindow)?.RefreshProfiles();
                (Owner as MainWindow)!.CurrentProfile = name;
                (Owner as MainWindow)?.RefreshModList();
                (Owner as MainWindow)?.AppendExternalLog($"配置复制完成: {name}");
                RefreshProfilesUI();
            }
            catch (System.Exception ex) { (Owner as MainWindow)?.AppendExternalLog("复制配置失败: " + ex.Message); }
        }

        private void BtnSetCurrentProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is not SettingsService s) return;
            var name = ProfilesCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name)) return;
            var path = ProfilePathTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                var dlg = new CommonOpenFileDialog { IsFolderPicker = true };
                if (dlg.ShowDialog() != CommonFileDialogResult.Ok) return;
                path = dlg.FileName;
            }
            s.ProfileModFolders[name] = path!;
            s.CurrentProfile = name;
            s.ModFolder = path!;
            s.Save();
            SettingsContext.Initialize(s);
            RefreshProfilesUI();
        }

        private void BtnBrowseProfilePath_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is not SettingsService s) return;
            var name = ProfilesCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name)) return;
            var dlg = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dlg.ShowDialog() != CommonFileDialogResult.Ok) return;
            var dir = dlg.FileName;
            s.ProfileModFolders[name] = dir;
            if (s.CurrentProfile == name) s.ModFolder = dir;
            s.Save(); SettingsContext.Initialize(s);
            ProfilePathTextBox.Text = dir;
        }

        private void BtnOpenProfilePath_Click(object sender, RoutedEventArgs e)
        {
            var path = ProfilePathTextBox.Text;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            }
            else MessageBox.Show("路径不存在或无效。");
        }

        // 删除配置（仅移除映射，不删除磁盘目录）
        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is not SettingsService s) return;
            var name = ProfilesCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("请先选择要删除的配置。"); return; }
            if (name.Equals(s.CurrentProfile, System.StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("无法删除当前正在使用的配置，请先切换到其他配置后再删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!s.ProfileModFolders.ContainsKey(name))
            {
                MessageBox.Show("该配置不存在映射。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var result = MessageBox.Show($"确定要删除配置“{name}”吗？此操作不会删除磁盘目录。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            s.ProfileModFolders.Remove(name);
            s.Save();
            SettingsContext.Initialize(s);
            RefreshProfilesUI();
            if (Owner is MainWindow mw)
            {
                mw.RefreshProfiles();
            }
        }

        private void BtnRescanMods_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is not SettingsService s) return;
            var modRoot = s.ModFolder;
            if (string.IsNullOrWhiteSpace(modRoot) || !Directory.Exists(modRoot))
            {
                MessageBox.Show("Mod 目录不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var mods = new ObservableCollection<MainModItem>();
            foreach (var mainDir in Directory.GetDirectories(modRoot))
            {
                var name = new DirectoryInfo(mainDir).Name;
                // 使用合并策略：保留原备注/图片/Url/Guid 等已有信息
                var item = ManifestGenerator.EnsureManifestWithFileGroups(name, mainDir);
                mods.Add(item);
            }

            ModListManager.SaveModList(mods);
            if (Owner is MainWindow mw)
            {
                mw.RefreshModList();
            }
            MessageBox.Show("扫描完成并已刷新列表。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
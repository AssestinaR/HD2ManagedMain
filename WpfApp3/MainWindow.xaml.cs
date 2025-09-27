using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Threading.Tasks;
using LiberTeaManager.Services;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Windows.Media;
using LiberTeaManager.UI.Drag;
using LiberTeaManager.Controls;
using System.Windows.Navigation; // added
using System.Text;

namespace LiberTeaManager
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        #endregion

        #region Public Bindings
        public ObservableCollection<MainModItem> ModItems { get; set; } = new();
        private object? _detailItem;
        public object? DetailItem { get => _detailItem; private set { if (_detailItem != value) { _detailItem = value; OnPropertyChanged(nameof(DetailItem)); } } }

        private int _allFileGroupCount;
        public int AllFileGroupCount { get => _allFileGroupCount; private set { if (_allFileGroupCount != value) { _allFileGroupCount = value; OnPropertyChanged(nameof(AllFileGroupCount)); } } }
        private int _allEnabledFileGroupCount;
        public int AllEnabledFileGroupCount { get => _allEnabledFileGroupCount; private set { if (_allEnabledFileGroupCount != value) { _allEnabledFileGroupCount = value; OnPropertyChanged(nameof(AllEnabledFileGroupCount)); } } }

        private bool _busy;
        public bool Busy { get => _busy; private set { if (_busy != value) { _busy = value; OnPropertyChanged(nameof(Busy)); } } }

        private string _dropHintText = string.Empty;
        public string DropHintText { get => _dropHintText; set { if (_dropHintText != value) { _dropHintText = value; OnPropertyChanged(nameof(DropHintText)); } } }

        public ObservableCollection<string> Profiles { get; } = new();
        private string _currentProfile = "default";
        public string CurrentProfile { get => _currentProfile; set { if (_currentProfile != value) { _currentProfile = value; OnPropertyChanged(nameof(CurrentProfile)); } } }
        #endregion

        #region Services & State
        private IPatchLinkService _patchService;
        private IImportService _importService;
        private ILogService _logService;
        private BufferedLogService _bufferedLog;
        private DispatcherTimer _logFlushTimer;
        private ISelectionService _selectionService;
        private IActivationService _activationService;
        private ISettingsService _settingsService;
        private IRenameService _renameService;
        private IStructureTransformService _structureService = new StructureTransformService();

        private Point _dragStartPoint;
        private bool _dragCandidate;
        private List<object> _lastShiftRange = new();

        private TreeDragManager? _dragManager;

        private Border? _overlay; // 全局拖拽覆盖层
        private ModManagerControl? _manager; // 如果后续再用控件

        // 记录上次已应用到系统的配置名，避免刷新 Profiles 时误触发
        private string? _lastAppliedProfile;
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded;
            ModFileHelper.AppendLog = AddLog;

            _logService = new UiLogService(AddLog);
            _settingsService = new SettingsService(_logService); _settingsService.Load();
            SettingsContext.Initialize(_settingsService);
            _patchService = new PatchLinkService(ModItems, _logService, () => _settingsService.GameFolder, () => _settingsService.ModFolder);
            _importService = new ImportService(ModItems, _logService, _settingsService);
            _renameService = new RenameService(_logService);
            _selectionService = new SelectionService();
            _activationService = new ActivationService(ModItems, _patchService, _logService);
            _bufferedLog = new BufferedLogService(48, s => AppendLogImmediate(s));
            _logFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _logFlushTimer.Tick += (s, e) => _bufferedLog.Flush(AppendLogImmediate);
            _logFlushTimer.Start();

            // 初始化已应用配置名
            if (_settingsService is SettingsService ssvc && !string.IsNullOrWhiteSpace(ssvc.CurrentProfile))
                _lastAppliedProfile = ssvc.CurrentProfile;
            else
                _lastAppliedProfile = CurrentProfile;

            AutoInitGameAndModFolders();
            RefreshModList();
            RecalcAllFileGroupCount();
            AddLog("提示: Shift 连选, Ctrl 单切换, 双击展开/折叠, 拖拽可重排。");

            if (_settingsService.MainWindowWidth > 0) Width = _settingsService.MainWindowWidth;
            if (_settingsService.MainWindowHeight > 0) Height = _settingsService.MainWindowHeight;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _dragManager = new TreeDragManager(ModTreeView,
                () => _selectionService.GetAllSelected(ModItems),
                OnInternalDrop,
                _ => { });
            RefreshProfiles();
        }

        #region Drag Reorder Implementation
        private void OnInternalDrop(object? targetCtx, TreeDragManager.TreePlacement placement, List<object> dragged)
        {
            var result = _structureService.Execute(dragged, targetCtx, placement, ModItems, msg => AddLog(msg));
            if (result != StructureOpResult.None)
            {
                RecalcAllFileGroupCount();
                ModListManager.SaveModList(ModItems);
            }
        }
        #endregion

        #region Init Helpers
        private void AutoInitGameAndModFolders()
        {
            try
            {
                bool needGameDetect = string.IsNullOrWhiteSpace(_settingsService.GameFolder) || !Directory.Exists(_settingsService.GameFolder) || !Directory.EnumerateFiles(_settingsService.GameFolder, "*.patch_*", SearchOption.TopDirectoryOnly).Any();
                if (needGameDetect)
                {
                    var found = SteamLocator.TryFindHelldivers2Data();
                    if (!string.IsNullOrEmpty(found)) { _settingsService.GameFolder = found; _settingsService.Save(); AddLog("自动检测游戏目录: " + found); }
                }
                if (string.IsNullOrWhiteSpace(_settingsService.ModFolder) || !Directory.Exists(_settingsService.ModFolder))
                {
                    var def = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod");
                    Directory.CreateDirectory(def);
                    _settingsService.ModFolder = def; _settingsService.Save();
                }
            }
            catch (Exception ex) { AddLog("初始化目录失败: " + ex.Message); }
        }
        #endregion

        #region Logging
        private void AddLog(string msg) => _bufferedLog.Log(msg);
        private void AppendLogImmediate(string msg)
        {
            if (!Dispatcher.CheckAccess()) { try { Dispatcher.Invoke(() => AppendLogImmediate(msg)); } catch { } return; }
            var box = this.FindName("LogTextBox") as TextBox;
            if (box == null) return;
            box.AppendText(msg.EndsWith('\n') ? msg : msg + '\n');
            box.ScrollToEnd();
        }
        #endregion

        #region Mod List
        public void RefreshModList()
        {
            var loaded = ModListManager.LoadModList();
            ModItems.Clear(); foreach (var m in loaded) ModItems.Add(m);
            RecalcAllFileGroupCount();
        }
        private void RecalcAllFileGroupCount()
        {
            int total = 0, enabled = 0;
            foreach (var m in ModItems)
            {
                int mg = m.FileGroups?.Count ?? 0; total += mg; if (m.Enabled == EnabledState.Enabled) enabled += mg;
                foreach (var o in m.Options)
                {
                    int og = o.FileGroups?.Count ?? 0; total += og; if (o.Enabled == EnabledState.Enabled) enabled += og;
                    foreach (var s in o.SubOptions)
                    {
                        int sg = s.FileGroups?.Count ?? 0; total += sg; if (s.Enabled == EnabledState.Enabled) enabled += sg;
                    }
                }
            }
            AllFileGroupCount = total; AllEnabledFileGroupCount = enabled;
        }
        #endregion

        #region Include Normalization
        private void NormalizeIncludes()
        {
            foreach (var m in ModItems)
                foreach (var o in m.Options)
                {
                    if (o.Include == null || o.Include.Count == 0) o.Include = new List<string> { o.Name };
                    foreach (var s in o.SubOptions)
                        if (s.Include == null || s.Include.Count == 0) s.Include = new List<string> { o.Name + "/" + s.Name };
                }
        }
        #endregion

        #region External Drag Overlay + Drop Import
        private bool IsArchive(string p) => new[] { ".zip", ".7z", ".rar" }.Contains(Path.GetExtension(p).ToLowerInvariant());
        private bool IsImage(string p) => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(p).ToLowerInvariant());
        private bool IsPatchFile(string p) => Regex.IsMatch(Path.GetFileName(p) ?? string.Empty, @"^[a-fA-F0-9]{16}\.patch_\d+(?:\.stream|\.gpu_resources)?$");
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (Busy) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (paths.Any(IsArchive)) { DropHintText = "释放以导入压缩包"; e.Effects = DragDropEffects.Copy; }
                else if (paths.Any(Directory.Exists)) { DropHintText = "释放以导入文件夹"; e.Effects = DragDropEffects.Copy; }
                else if (paths.Any(IsImage)) { DropHintText = "释放以更新图片"; e.Effects = DragDropEffects.Copy; }
                else if (paths.Any(IsPatchFile)) { DropHintText = "释放以添加文件组"; e.Effects = DragDropEffects.Copy; }
                else { DropHintText = "不支持的拖拽类型"; e.Effects = DragDropEffects.None; }
                _overlay ??= GlobalDropOverlay;
                if (_overlay != null) _overlay.Visibility = Visibility.Visible;
            }
            else { if (_overlay != null) _overlay.Visibility = Visibility.Collapsed; e.Effects = DragDropEffects.None; }
            e.Handled = true;
        }
        private void Window_DragLeave(object sender, DragEventArgs e) { if (_overlay != null) _overlay.Visibility = Visibility.Collapsed; }
        private void Window_Drop(object sender, DragEventArgs e) { if (_overlay != null) _overlay.Visibility = Visibility.Collapsed; _ = HandleWindowDropAsync(e); }
        private async Task HandleWindowDropAsync(DragEventArgs e)
        {
            if (Busy) { AddLog("请稍等..."); return; }
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            var archives = paths.Where(IsArchive).ToList();
            var dirs = paths.Where(Directory.Exists).ToList();
            var images = paths.Where(IsImage).ToList();
            var patchFiles = paths.Where(IsPatchFile).ToList();
            if (archives.Any()) { Busy = true; AddLog("导入压缩包..."); await _importService.ImportArchivesAsync(archives); RefreshModList(); Busy = false; AddLog("完成"); }
            else if (dirs.Any()) { Busy = true; foreach (var d in dirs) { AddLog("导入目录: " + d); await Task.Run(() => _importService.ImportDirectory(d)); } RefreshModList(); Busy = false; AddLog("完成"); }
            else if (images.Any())
            {
                var target = _selectionService.GetAllSelected(ModItems).FirstOrDefault();
                if (target == null) { AddLog("未选择目标"); return; }
                ApplyImageToTarget(target, images.First()); ModListManager.SaveModList(ModItems); AddLog("图片已更新");
            }
            else if (patchFiles.Any())
            {
                var target = _selectionService.GetAllSelected(ModItems).FirstOrDefault(); if (target == null) { AddLog("未选择目标"); return; }
                AddPatchFilesAsGroup(target, patchFiles); ModListManager.SaveModList(ModItems); AddLog("已添加文件组");
            }
        }
        #endregion

        #region Patch Group Add + Image Apply
        private void AddPatchFilesAsGroup(object target, IEnumerable<string> patchFiles)
        {
            var regex = new Regex(@"([a-fA-F0-9]{16})\.patch_(\d+)(?:\.stream|\.gpu_resources)?$", RegexOptions.Compiled);
            var groups = new Dictionary<string, ModFileGroup>();
            foreach (var f in patchFiles)
            {
                var name = Path.GetFileName(f); var m = regex.Match(name); if (!m.Success) continue; var hex = m.Groups[1].Value; int pn = int.Parse(m.Groups[2].Value);
                var key = hex + "." + pn;
                if (!groups.TryGetValue(key, out var g)) { g = new ModFileGroup { HexPrefix = hex, PatchN = pn, RelativePath = hex, Files = new List<string>() }; groups[key] = g; }
                g.Files.Add(name);
            }
            if (!groups.Any()) { AddLog("未识别 patch 文件"); return; }
            if (target is MainModItem mm) foreach (var g in groups.Values) mm.FileGroups.Add(g);
            else if (target is OptionItem oo) foreach (var g in groups.Values) oo.FileGroups.Add(g);
            else if (target is SubOptionItem ss) foreach (var g in groups.Values) ss.FileGroups.Add(g);
        }
        private void ApplyImageToTarget(object target, string file)
        {
            try
            {
                if (target is MainModItem m)
                {
                    var dest = Path.Combine(_settingsService.ModFolder, m.Name, Path.GetFileName(file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); m.IconPath = Path.GetFileName(file); m.Image = m.IconPath;
                }
                else if (target is OptionItem o)
                {
                    var parent = ModItems.FirstOrDefault(x => x.Options.Contains(o)); if (parent == null) return; var dest = Path.Combine(_settingsService.ModFolder, parent.Name, o.Name, Path.GetFileName(file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); o.IconPath = o.Name + "/" + Path.GetFileName(file); o.Image = o.IconPath;
                }
                else if (target is SubOptionItem s)
                {
                    foreach (var mod in ModItems)
                    {
                        var opt = mod.Options.FirstOrDefault(op => op.SubOptions.Contains(s)); if (opt != null) { var dest = Path.Combine(_settingsService.ModFolder, mod.Name, opt.Name, s.Name, Path.GetFileName(file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); s.IconPath = opt.Name + "/" + s.Name + "/" + Path.GetFileName(file); s.Image = s.IconPath; break; }
                    }
                }
            }
            catch (Exception ex) { AddLog("设置图片失败: " + ex.Message); }
        }
        #endregion

        #region Buttons
        private async void BtnEnable_Click(object sender, RoutedEventArgs e) { if (Busy) { AddLog("请稍等..."); return; } Busy = true; await _activationService.EnableSelectedAsync(true); Busy = false; RecalcAllFileGroupCount(); }
        private async void BtnDisable_Click(object sender, RoutedEventArgs e) { if (Busy) { AddLog("请稍等..."); return; } Busy = true; await _activationService.DisableSelectedAsync(); Busy = false; RecalcAllFileGroupCount(); }
        private async void BtnDelete_Click(object sender, RoutedEventArgs e) { if (Busy) { AddLog("请稍等..."); return; } Busy = true; await _activationService.DeleteSelectedAsync(); Busy = false; RefreshModList(); RecalcAllFileGroupCount(); }
        private void BtnInvertSelection_Click(object sender, RoutedEventArgs e) { if (Busy) { AddLog("请稍等..."); return; } foreach (var m in ModItems) { m.IsSelected = !m.IsSelected; foreach (var o in m.Options) { o.IsSelected = !o.IsSelected; foreach (var s in o.SubOptions) s.IsSelected = !s.IsSelected; } } }
        private void BtnRename_Click(object sender, RoutedEventArgs e)
        {
            if (Busy) { AddLog("请稍等..."); return; }
            if (sender is not Button btn || btn.Tag is not object target) return;
            string? oldName = target switch { MainModItem m => m.Name, OptionItem o => o.Name, SubOptionItem s => s.Name, _ => null }; if (oldName == null) return;
            var win = new SingleInputWindow("修改名称", $"请输入新名称 (原: {oldName})", oldName) { Owner = this };
            if (win.ShowDialog() == true)
            { var newVal = win.ResultText?.Trim(); if (string.IsNullOrWhiteSpace(newVal) || newVal == oldName) return; if (_renameService.TryRename(target, newVal!, ModItems)) { ModListManager.SaveModList(ModItems); } }
        }
        private void BtnEditDescription_Click(object sender, RoutedEventArgs e)
        { if (Busy) { AddLog("请稍等..."); return; } if (sender is not Button btn || btn.Tag is not object target) return; string old = target switch { MainModItem m => m.Description, OptionItem o => o.Description, SubOptionItem s => s.Description, _ => string.Empty } ?? string.Empty; var win = new SingleInputWindow("修改备注", "输入备注 (关闭或回车保存)", old) { Owner = this }; if (win.ShowDialog() == true) { var nv = win.ResultText ?? string.Empty; switch (target) { case MainModItem m: m.Description = nv; break; case OptionItem o: o.Description = nv; break; case SubOptionItem s: s.Description = nv; break; } ModListManager.SaveModList(ModItems); } }
        private void BtnChangeImage_Click(object sender, RoutedEventArgs e)
        { if (Busy) { AddLog("请稍等..."); return; } if (sender is not Button btn || btn.Tag is not object target) return; var ofd = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp|全部|*.*" }; if (ofd.ShowDialog() == true) { ApplyImageToTarget(target, ofd.FileName); ModListManager.SaveModList(ModItems); AddLog("图片已更新"); } }
        private void BtnOpenInExplorer_Click(object sender, RoutedEventArgs e)
        { if (Busy) { AddLog("请稍等..."); return; } if (sender is not Button btn || btn.Tag is not object target) return; string? path = target switch { MainModItem m => Path.Combine(_settingsService.ModFolder, m.Name), OptionItem o => GetOptionPath(o), SubOptionItem s => GetSubPath(s), _ => null }; if (path != null && Directory.Exists(path)) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); } catch (Exception ex) { AddLog("打开失败: " + ex.Message); } else AddLog("路径不存在"); }
        private string? GetOptionPath(OptionItem o) { var parent = ModItems.FirstOrDefault(m => m.Options.Contains(o)); return parent == null ? null : Path.Combine(_settingsService.ModFolder, parent.Name, o.Name); }
        private string? GetSubPath(SubOptionItem s) { foreach (var m in ModItems) { var opt = m.Options.FirstOrDefault(x => x.SubOptions.Contains(s)); if (opt != null) return Path.Combine(_settingsService.ModFolder, m.Name, opt.Name, s.Name); } return null; }
        private async void BtnLoadMod_Click(object sender, RoutedEventArgs e) { if (Busy) { AddLog("请稍等..."); return; } var ofd = new OpenFileDialog { Filter = "Mod压缩包|*.zip;*.7z;*.rar", Multiselect = true }; if (ofd.ShowDialog() == true) { Busy = true; AddLog("导入压缩包..."); await _importService.ImportArchivesAsync(ofd.FileNames); RefreshModList(); Busy = false; AddLog("完成"); } }
        private async void BtnLoadExtractedMod_Click(object sender, RoutedEventArgs e) { if (Busy) { AddLog("请稍等..."); return; } var dlg = new System.Windows.Forms.FolderBrowserDialog(); if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; Busy = true; AddLog("导入目录: " + dlg.SelectedPath); await Task.Run(() => _importService.ImportDirectory(dlg.SelectedPath)); RefreshModList(); Busy = false; AddLog("完成"); }
        private async void BtnExportMod_Click(object sender, RoutedEventArgs e) { if (Busy) { AddLog("请稍等..."); return; } var selected = ModFileHelper.GetSelectedMods(ModItems); if (!selected.Any()) { AddLog("请选择要导出的 Mod"); return; } var dlg = new System.Windows.Forms.FolderBrowserDialog(); if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; Busy = true; AddLog("导出中..."); await ModFileHelper.ExportModsAsync(selected, dlg.SelectedPath); Busy = false; AddLog("导出完成"); }
        private void BtnSettings_Click(object sender, RoutedEventArgs e) { if (Busy) { AddLog("请稍等..."); return; } var win = new SettingsWindow(_settingsService) { Owner = this }; win.ShowDialog(); }
        private void BtnNewEmptyMod_Click(object sender, RoutedEventArgs e)
        {
            if (Busy) { AddLog("请稍等..."); return; }
            var win = new SingleInputWindow("新建空 Mod", "请输入 Mod 名称", "NewMod") { Owner = this };
            if (win.ShowDialog() != true) return;
            var name = win.ResultText?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (ModItems.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                AddLog($"已存在同名 Mod: {name}");
                return;
            }
            var item = new MainModItem
            {
                Name = name,
                Description = string.Empty,
                Guid = Guid.NewGuid(),
                Enabled = EnabledState.Disabled,
                Image = string.Empty,
                IconPath = string.Empty,
                RootModName = name,
            };
            ModItems.Add(item);
            try
            {
                // 在 Mod 根目录下创建对应文件夹
                var dir = System.IO.Path.Combine(_settingsService.ModFolder, name);
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                AddLog("创建目录失败: " + ex.Message);
            }
            ModListManager.SaveModList(ModItems);
            RecalcAllFileGroupCount();
            AddLog($"已新建空 Mod: {name}");
        }
        private void BtnShowEnabledStatus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new EnabledStatusWindow(ModItems, _settingsService.GameFolder, _settingsService.ModFolder) { Owner = this };
                win.Show();
            }
            catch (Exception ex)
            {
                AddLog("打开启用状态窗口失败: " + ex.Message);
            }
        }
        #endregion

        #region Window / Helper
        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                _bufferedLog.Flush(AppendLogImmediate);
                _settingsService.MainWindowWidth = Width; _settingsService.MainWindowHeight = Height; _settingsService.Save();
                ModListManager.SaveModList(ModItems);
                ModListManager.Flush(); // 确保延迟保存的数据落盘
            }
            catch (Exception ex) { AppendLogImmediate("关闭保存失败: " + ex.Message); }
        }
        private object? GetDataContextFromTreeViewItem(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                var tvi = dep as TreeViewItem;
                while (tvi == null && dep != null) { dep = VisualTreeHelper.GetParent(dep); tvi = dep as TreeViewItem; }
                return tvi?.DataContext;
            }
            return (sender as TreeViewItem)?.DataContext;
        }
        #endregion

        #region TreeView Interaction Handlers
        public void TreeViewItem_DragOver(object sender, DragEventArgs e) => _dragManager?.HandleDragOver(e);
        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Busy) return;
            // 仅处理选择与拖拽候选，不做展开/折叠（双击展开由 TreeViewExpandBehavior 负责）
            var data = GetDataContextFromTreeViewItem(sender, e); if (data == null) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (_selectionService.HandleMouseDown(data, ctrl, shift, ModItems, ref _lastShiftRange, out _dragCandidate))
            { DetailItem = data; _dragStartPoint = e.GetPosition(ModTreeView); }
            e.Handled = true;
        }

        private void TreeViewItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragCandidate || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(ModTreeView);
            if (Math.Abs(pos.X - _dragStartPoint.X) > 6 || Math.Abs(pos.Y - _dragStartPoint.Y) > 6)
            {
                var selected = _selectionService.GetAllSelected(ModItems).ToList(); if (!selected.Any()) return;
                try { DragDrop.DoDragDrop(ModTreeView, new DataObject(typeof(List<object>), selected), DragDropEffects.Move); } catch { }
                _dragCandidate = false;
            }
        }
        private void TreeViewItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragCandidate = false;
        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) { if (sender is TreeViewItem tvi) DetailItem = tvi.DataContext; }
        private void TreeViewItem_Drop(object sender, DragEventArgs e) => _dragManager?.HandleDrop(e);
        #endregion

        public void RefreshProfiles()
        {
            Profiles.Clear();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 来自 SettingsService 映射
            if (_settingsService is SettingsService concrete)
            {
                foreach (var kv in concrete.ProfileModFolders)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key)) names.Add(kv.Key.Trim());
                }
                if (!string.IsNullOrWhiteSpace(concrete.CurrentProfile)) names.Add(concrete.CurrentProfile.Trim());
            }
            // 兼容旧 profiles 目录（若仍存在）
            foreach (var p in ModListManager.GetAllProfiles()) names.Add(p);
            // 排序：default 优先，其余字母序
            foreach (var n in names.OrderBy(n => string.Equals(n, "default", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
                Profiles.Add(n);
            // 确保当前选中
            var desired = (_settingsService as SettingsService)?.CurrentProfile ?? CurrentProfile;
            if (Profiles.Contains(desired)) CurrentProfile = desired; else if (Profiles.Any()) CurrentProfile = Profiles.First();
        }

        private void ProfileCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 若未实际变更配置名，则忽略（避免刷新 Profiles 或打开设置窗口导致的重复触发）
            if (!string.IsNullOrWhiteSpace(_lastAppliedProfile) &&
                string.Equals(_lastAppliedProfile, CurrentProfile, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentProfile)) return;
            if (_settingsService is SettingsService concrete)
            {
                if (!concrete.ProfileModFolders.TryGetValue(CurrentProfile, out var folder) || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    // 需要让用户指定该配置的 mod 目录
                    var dlg = new System.Windows.Forms.FolderBrowserDialog();
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    {
                        // 用户取消，回退到之前的 profile（若存在）
                        RefreshProfiles();
                        return;
                    }
                    folder = dlg.SelectedPath;
                    concrete.ProfileModFolders[CurrentProfile] = folder;
                }
                _settingsService.ModFolder = folder;
                concrete.CurrentProfile = CurrentProfile;
                concrete.Save();
                SettingsContext.Initialize(_settingsService);
            }
            RefreshModList();
            RecalcAllFileGroupCount();
            AddLog($"已切换配置: {CurrentProfile} -> Mod目录: {_settingsService.ModFolder}");

            // 标记为已应用，后续同名触发将被忽略
            _lastAppliedProfile = CurrentProfile;

            // 异步重建链接（不阻塞事件处理器签名）
            _ = RebuildLinksForCurrentProfileAsync();
        }

        private async Task RebuildLinksForCurrentProfileAsync()
        {
            try
            {
                Busy = true;
                await _patchService.ReorderAndLinkAsync(fullRebuild: true, logPerGroup: false);
                AddLog("已根据当前配置重新链接到游戏目录");
            }
            catch (Exception ex)
            {
                AddLog("重新链接失败: " + ex.Message);
            }
            finally { Busy = false; }
        }

        private void BtnEditUrl_Click(object sender, RoutedEventArgs e)
        {
            if (Busy) { AddLog("请稍等..."); return; }
            if (sender is not Button btn) return;
            var target = btn.Tag ?? DetailItem;
            string current = target switch
            {
                MainModItem mm => mm.Url ?? string.Empty,
                OptionItem oo => oo.Url ?? string.Empty,
                SubOptionItem ss => ss.Url ?? string.Empty,
                _ => string.Empty
            };
            var win = new SingleInputWindow("修改网址", "输入网址 (以 http/https 开头)", current) { Owner = this };
            if (win.ShowDialog() != true) return;
            var url = win.ResultText?.Trim() ?? string.Empty;
            switch (target)
            {
                case MainModItem mm: mm.Url = url; break;
                case OptionItem oo: oo.Url = url; break;
                case SubOptionItem ss: ss.Url = url; break;
                default: return;
            }
            ModListManager.SaveModList(ModItems);
            // 触发详情刷新
            DetailItem = target;
            AddLog("已更新网址");
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                var uri = e.Uri?.ToString();
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
                }
            }
            catch (Exception ex) { AddLog("打开网址失败: " + ex.Message); }
            e.Handled = true;
        }

        public ISettingsService GetSettingsService() => _settingsService;
        public void AppendExternalLog(string message) => AddLog(message);
    }

    internal static class DependencyObjectExtensions
    {
        public static IEnumerable<DependencyObject> GetAncestors(this DependencyObject obj)
        {
            for (var current = VisualTreeHelper.GetParent(obj); current != null; current = VisualTreeHelper.GetParent(current))
            {
                yield return current;
            }
        }
    }
}
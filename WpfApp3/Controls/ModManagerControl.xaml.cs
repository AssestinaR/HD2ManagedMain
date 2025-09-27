using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using Microsoft.Win32;
using LiberTeaManager.Services;
using System.IO;
using System.Windows.Media;
using LiberTeaManager.UI.Drag;

namespace LiberTeaManager.Controls
{
    public partial class ModManagerControl : UserControl
    {
        public ObservableCollection<MainModItem> ModItems
        {
            get => (ObservableCollection<MainModItem>)GetValue(ModItemsProperty);
            set => SetValue(ModItemsProperty, value);
        }
        public static readonly DependencyProperty ModItemsProperty =
            DependencyProperty.Register(nameof(ModItems), typeof(ObservableCollection<MainModItem>), typeof(ModManagerControl), new PropertyMetadata(new ObservableCollection<MainModItem>()));

        public object DetailItem
        {
            get => GetValue(DetailItemProperty);
            set => SetValue(DetailItemProperty, value);
        }
        public static readonly DependencyProperty DetailItemProperty =
            DependencyProperty.Register(nameof(DetailItem), typeof(object), typeof(ModManagerControl));

        public string DragPreviewText
        {
            get => (string)GetValue(DragPreviewTextProperty);
            set => SetValue(DragPreviewTextProperty, value);
        }
        public static readonly DependencyProperty DragPreviewTextProperty =
            DependencyProperty.Register(nameof(DragPreviewText), typeof(string), typeof(ModManagerControl), new PropertyMetadata(""));

        public ISelectionService SelectionService { get; set; }
        public IActivationService ActivationService { get; set; }
        public IPatchLinkService PatchService { get; set; }
        public IImportService ImportService { get; set; }
        public IRenameService RenameService { get; set; }
        public ISettingsService SettingsService { get; set; }
        public Action<string> Log { get; set; } = _ => { };
        public Func<bool> IsBusyGetter { get; set; } = () => false;
        public Action<bool> SetBusy { get; set; } = _ => { };
        public Action RecalcAllFileGroupCount { get; set; } = () => { };

        private Point _dragStart;
        private bool _dragCandidate;
        private TreeDragManager _dragManager;
        private List<object> _shiftRange = new();

        public ModManagerControl()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _dragManager = new TreeDragManager(ModTreeView, () => SelectionService.GetAllSelected(ModItems), OnInternalDrop, ctx => { });
        }

        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dc = GetDataContextFromTreeViewItem(sender, e); if (dc == null) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (SelectionService.HandleMouseDown(dc, ctrl, shift, ModItems, ref _shiftRange, out var cand))
            {
                _dragStart = e.GetPosition(null);
                _dragCandidate = cand;
                DetailItem = dc;
            }
        }
        private void TreeViewItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragCandidate || e.LeftButton != MouseButtonState.Pressed) return;
            var cur = e.GetPosition(null);
            if (Math.Abs(cur.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(cur.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var sel = SelectionService.GetAllSelected(ModItems).ToList(); if (!sel.Any()) return;
                _dragCandidate = false;
                DragPreviewText = string.Join(", ", sel.Select(s => s switch { MainModItem m => "主:"+m.Name, OptionItem o => "选:"+o.Name, SubOptionItem so => "子:"+so.Name, _ => s.ToString() }));
                DragPreviewPanel.Visibility = Visibility.Visible;
                var data = new DataObject(); data.SetData(typeof(List<object>), sel);
                DragDrop.DoDragDrop(ModTreeView, data, DragDropEffects.Move);
                DragPreviewPanel.Visibility = Visibility.Collapsed;
                DragPreviewText = string.Empty;
            }
        }
        private void TreeViewItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragCandidate = false;
        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        { var dc = GetDataContextFromTreeViewItem(sender, e); if (dc != null) DetailItem = dc; }
        public void TreeViewItem_DragOver(object sender, DragEventArgs e) => _dragManager?.HandleDragOver(e);
        public void TreeViewItem_Drop(object sender, DragEventArgs e) => _dragManager?.HandleDrop(e);

        private void OnInternalDrop(object targetCtx, TreeDragManager.TreePlacement placement, List<object> dragged)
        {
            DragPreviewPanel.Visibility = Visibility.Collapsed;
            DragPreviewText = string.Empty;
            // 回调外部(目前主窗口未订阅，可后续扩展)
        }

        private object GetDataContextFromTreeViewItem(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                var tvi = dep as TreeViewItem;
                while (tvi == null && dep != null)
                { dep = VisualTreeHelper.GetParent(dep); tvi = dep as TreeViewItem; }
                return tvi?.DataContext;
            }
            return (sender as TreeViewItem)?.DataContext;
        }

        private async void BtnEnable_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } SetBusy(true); await ActivationService.EnableSelectedAsync(true); SetBusy(false); RecalcAllFileGroupCount(); }
        private async void BtnDisable_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } SetBusy(true); await ActivationService.DisableSelectedAsync(); SetBusy(false); RecalcAllFileGroupCount(); }
        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } var sel = SelectionService.GetAllSelected(ModItems).ToList(); if (!sel.Any()) { Log("未选择"); return; } if (MessageBox.Show($"确认删除 {sel.Count} 个项?", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; SetBusy(true); await ActivationService.DeleteSelectedAsync(); SetBusy(false); RecalcAllFileGroupCount(); }
        private void BtnInvertSelection_Click(object sender, RoutedEventArgs e)
        { foreach (var m in ModItems) { m.IsSelected = !m.IsSelected; foreach (var o in m.Options) { o.IsSelected = !o.IsSelected; foreach (var s in o.SubOptions) s.IsSelected = !s.IsSelected; } } }
        private void BtnRename_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } if (sender is not Button btn || btn.Tag is not object target) return; var oldName = target switch { MainModItem m => m.Name, OptionItem o => o.Name, SubOptionItem so => so.Name, _ => null }; if (oldName == null) return; var win = new SingleInputWindow("修改名称", $"请输入新名称 (原: {oldName})", oldName) { Owner = Window.GetWindow(this) }; if (win.ShowDialog() == true) { var newVal = win.ResultText?.Trim(); if (!string.IsNullOrWhiteSpace(newVal) && newVal != oldName && RenameService.TryRename(target, newVal!, ModItems)) { Log("已重命名"); } } }
        private void BtnEditDescription_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } if (sender is not Button btn || btn.Tag is not object target) return; string old = target switch { MainModItem m => m.Description, OptionItem o => o.Description, SubOptionItem s => s.Description, _ => string.Empty } ?? string.Empty; var win = new SingleInputWindow("修改备注", "输入备注 (关闭或回车保存)", old) { Owner = Window.GetWindow(this) }; if (win.ShowDialog() == true) { var nv = win.ResultText ?? string.Empty; switch (target) { case MainModItem m: m.Description = nv; break; case OptionItem o: o.Description = nv; break; case SubOptionItem s: s.Description = nv; break; } Log("已更新备注"); } }
        private void BtnChangeImage_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } if (sender is not Button btn || btn.Tag is not object target) return; var ofd = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp|全部|*.*" }; if (ofd.ShowDialog() == true) { ApplyImageToTarget(target, ofd.FileName); Log("图片已更新"); } }
        private void BtnOpenInExplorer_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } if (sender is not Button btn || btn.Tag is not object target) return; string? path = GetObjectFolder(target); if (path != null && Directory.Exists(path)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); }
        private async void BtnLoadMod_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } var ofd = new OpenFileDialog { Filter = "Mod压缩包|*.zip;*.7z;*.rar", Multiselect = true }; if (ofd.ShowDialog() == true) { if (MessageBox.Show($"确认导入 {ofd.FileNames.Length} 个压缩包?", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { SetBusy(true); await ImportService.ImportArchivesAsync(ofd.FileNames); SetBusy(false); Log("导入完成"); } } }
        private async void BtnLoadExtractedMod_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } var dlg = new System.Windows.Forms.FolderBrowserDialog(); if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; if (MessageBox.Show("确认导入该目录?", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; SetBusy(true); await Task.Run(() => ImportService.ImportDirectory(dlg.SelectedPath)); SetBusy(false); Log("导入完成"); }
        private async void BtnExportMod_Click(object sender, RoutedEventArgs e)
        { if (IsBusyGetter()) { Log("请稍等..."); return; } var selected = ModFileHelper.GetSelectedMods(ModItems); if (!selected.Any()) { Log("请选择要导出的 Mod"); return; } if (MessageBox.Show($"确认导出 {selected.Count} 个 Mod?", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; var dlg = new System.Windows.Forms.FolderBrowserDialog(); if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; SetBusy(true); await ModFileHelper.ExportModsAsync(selected, dlg.SelectedPath); SetBusy(false); Log("导出完成"); }
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(SettingsService) { Owner = Window.GetWindow(this) }.ShowDialog();

        private void ApplyImageToTarget(object target, string file)
        {
            try
            {
                if (target is MainModItem m)
                {
                    var dest = Path.Combine(SettingsService.ModFolder, m.Name, Path.GetFileName(file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); m.IconPath = Path.GetFileName(file); m.Image = m.IconPath;
                }
                else if (target is OptionItem o)
                {
                    var parent = ModItems.FirstOrDefault(x => x.Options.Contains(o)); if (parent == null) return; var dest = Path.Combine(SettingsService.ModFolder, parent.Name, o.Name, Path.GetFileName(file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); o.IconPath = o.Name + "/" + Path.GetFileName(file); o.Image = o.IconPath;
                }
                else if (target is SubOptionItem s)
                {
                    foreach (var mod in ModItems)
                    {
                        var opt = mod.Options.FirstOrDefault(op => op.SubOptions.Contains(s)); if (opt != null) { var dest = Path.Combine(SettingsService.ModFolder, mod.Name, opt.Name, s.Name, Path.GetFileName(file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); s.IconPath = opt.Name + "/" + s.Name + "/" + Path.GetFileName(file); s.Image = s.IconPath; break; }
                    }
                }
            }
            catch (Exception ex) { Log("设置图片失败: " + ex.Message); }
        }
        private string? GetObjectFolder(object target)
        {
            if (target is MainModItem m) return Path.Combine(SettingsService.ModFolder, m.Name);
            if (target is OptionItem o)
            {
                var parent = ModItems.FirstOrDefault(mm => mm.Options.Contains(o)); if (parent == null) return null; return Path.Combine(SettingsService.ModFolder, parent.Name, o.Name);
            }
            if (target is SubOptionItem s)
            {
                foreach (var mod in ModItems)
                {
                    var opt = mod.Options.FirstOrDefault(op => op.SubOptions.Contains(s)); if (opt != null) return Path.Combine(SettingsService.ModFolder, mod.Name, opt.Name, s.Name);
                }
            }
            return null;
        }
    }
}

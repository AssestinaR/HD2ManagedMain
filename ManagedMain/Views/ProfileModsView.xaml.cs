using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows;
using System.Collections.Specialized;
using System.Windows.Media.Animation;
using System.Windows.Media; // for TranslateTransform and brushes
using System.Windows.Input; // for Keyboard, focus
using System.Collections.Generic;
using ManagedMain.Models;
using System.Windows.Threading; // for DispatcherTimer
using System.IO;
using System.Text.RegularExpressions;
using DragEventArgsWpf = System.Windows.DragEventArgs;
using ManagedMain.UI.Drag;
using System.Windows.Interop; // for HwndSource hook
using System.Threading.Tasks; // async
using WpfApp = System.Windows.Application; // alias to resolve ambiguity

namespace ManagedMain.Views
{
    public partial class ProfileModsView : System.Windows.Controls.UserControl
    {
        private DispatcherTimer? _gamePingTimer;
        private System.Windows.Point _dragStart;
        private bool _isDragging;
        private bool _dragActive; // true only during DoDragDrop
        private TreeViewItem? _lastPressedItem;
        private List<object> _lastShiftRange = new();
        private TreeDragManager? _dragManager;
        private HwndSource? _hwndSrc;

        // Auto-scroll while dragging near edges
        private readonly DispatcherTimer _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
        private System.Windows.Point _lastMousePos;
        private const double EdgeZone = 48; // px from top/bottom that triggers auto-scroll
        private const double AutoScrollMaxStep = 48; // max px per tick

        public ProfileModsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            _autoScrollTimer.Tick += AutoScrollTimer_Tick;
            ModsTree.PreviewMouseRightButtonDown += ModsTree_PreviewMouseRightButtonDown;
        }

        private void ModsTree_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var dep = e.OriginalSource as DependencyObject;
                while (dep != null && dep is not TreeViewItem) dep = VisualTreeHelper.GetParent(dep);
                if (dep is TreeViewItem item)
                {
                    // cancel selection
                    if (item.DataContext is MainModItem m) m.IsSelected = false;
                    else if (item.DataContext is OptionItem o) o.IsSelected = false;
                    else if (item.DataContext is SubOptionItem s) s.IsSelected = false;
                    e.Handled = true;
                }
            }
            catch { }
        }

        private void ModsTree_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not TreeViewItem) dep = VisualTreeHelper.GetParent(dep);
            var item = dep as TreeViewItem;
            if (item == null)
            {
                _lastPressedItem = null;
                return;
            }
            _lastPressedItem = item;
            _dragStart = e.GetPosition(ModsTree);

            var data = item.DataContext;
            var vm = DataContext as ManagedMain.ViewModels.ProfileModsViewModel; if (vm == null) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (!ctrl && !shift)
            {
                // If clicking an already-selected item, keep current multi-selection for drag
                if (!IsSelected(data))
                {
                    ClearAllSelections(vm);
                    SetSelected(data, true);
                }
                _lastShiftRange = new List<object> { data };
            }
            else if (ctrl)
            {
                ToggleSelected(data);
                _lastShiftRange = new List<object> { data };
            }
            else if (shift)
            {
                RangeSelect(data, vm);
            }
        }

        private void ModsTree_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_lastPressedItem == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { _isDragging = false; return; }
            var pos = e.GetPosition(ModsTree);
            if (!_isDragging && (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                _isDragging = true;
                var dragged = GetSelectedItems();
                if (dragged.Count == 0)
                {
                    dragged.Add(_lastPressedItem.DataContext);
                    SetSelected(_lastPressedItem.DataContext, true);
                }
                var dataObj = new System.Windows.DataObject(typeof(List<object>), dragged);
                try
                {
                    _dragActive = true;
                    _autoScrollTimer.Start();
                    System.Windows.DragDrop.DoDragDrop(ModsTree, dataObj, System.Windows.DragDropEffects.Move);
                }
                finally
                {
                    _dragActive = false;
                    _autoScrollTimer.Stop();
                }
            }
        }

        private void ModsTree_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var viewer = FindVisualChild<ScrollViewer>(ModsTree);
            if (viewer == null) return;
            // Increase base step to 96 for very fast scrolling and scale with wheel delta magnitude
            double baseStep = 96;
            int ticks = Math.Max(1, Math.Abs(e.Delta) / 120);
            double step = baseStep * ticks;
            if (e.Delta > 0) viewer.ScrollToVerticalOffset(viewer.VerticalOffset - step);
            else if (e.Delta < 0) viewer.ScrollToVerticalOffset(viewer.VerticalOffset + step);
            e.Handled = true;
        }

        private void AutoScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (!_dragActive) { _autoScrollTimer.Stop(); return; }
            if (ModsTree == null) return;
            var viewer = FindVisualChild<ScrollViewer>(ModsTree); if (viewer == null) return;
            // Use last mouse pos captured during DragOver
            var height = ModsTree.ActualHeight;
            if (height <= 0) return;
            double step = 0;
            if (_lastMousePos.Y < EdgeZone)
            {
                double ratio = (EdgeZone - _lastMousePos.Y) / EdgeZone; // 0..1
                step = AutoScrollMaxStep * ratio;
                viewer.ScrollToVerticalOffset(Math.Max(0, viewer.VerticalOffset - step));
            }
            else if (_lastMousePos.Y > height - EdgeZone)
            {
                double ratio = (_lastMousePos.Y - (height - EdgeZone)) / EdgeZone; // 0..1
                step = AutoScrollMaxStep * ratio;
                viewer.ScrollToVerticalOffset(Math.Min(viewer.ScrollableHeight, viewer.VerticalOffset + step));
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void ModsTree_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            _lastMousePos = e.GetPosition(ModsTree);
            // If this is an external file drop (FileDrop present), suppress internal reordering visuals
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                _dragManager ??= new TreeDragManager(
                    ModsTree,
                    () => GetSelectedItems(),
                    (ctx, placement, dragged) => ApplyDrop(ctx, ConvertPlacement(placement), dragged),
                    _ => { });
                _dragManager.Clear();
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = false; // allow Root overlay handler to show import UI
                return;
            }

            // Internal drag (our custom List<object> payload)
            if (e.Data.GetDataPresent(typeof(List<object>)))
            {
                _dragManager ??= new TreeDragManager(
                    ModsTree,
                    () => GetSelectedItems(),
                    (ctx, placement, dragged) => ApplyDrop(ctx, ConvertPlacement(placement), dragged),
                    _ => { });
                _dragManager.HandleDragOver(e);
                return;
            }

            // Unknown drag type
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void ModsTree_Drop(object sender, System.Windows.DragEventArgs e)
        {
            _autoScrollTimer.Stop();
            // External import should not trigger internal drop
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                _dragManager?.Clear();
                return; // Root_Drop will handle import
            }
            if (e.Data.GetDataPresent(typeof(List<object>)))
            {
                _dragManager ??= new TreeDragManager(
                    ModsTree,
                    () => GetSelectedItems(),
                    (ctx, placement, dragged) => ApplyDrop(ctx, ConvertPlacement(placement), dragged),
                    _ => { });
                _dragManager.HandleDrop(e);
            }
        }

        private static ManagedMain.Services.TreeTransformPort.TreePlacement ConvertPlacement(TreeDragManager.TreePlacement p) => p switch
        {
            TreeDragManager.TreePlacement.Before => ManagedMain.Services.TreeTransformPort.TreePlacement.Before,
            TreeDragManager.TreePlacement.After => ManagedMain.Services.TreeTransformPort.TreePlacement.After,
            TreeDragManager.TreePlacement.Inside => ManagedMain.Services.TreeTransformPort.TreePlacement.Inside,
            _ => ManagedMain.Services.TreeTransformPort.TreePlacement.None
        };

        private void ApplyDrop(object? target, ManagedMain.Services.TreeTransformPort.TreePlacement placement, List<object> dragged)
        {
            if (DataContext is not ManagedMain.ViewModels.ProfileModsViewModel vm) return;
            var service = new ManagedMain.Services.TreeTransformPort();
            var result = service.Execute(dragged, target, placement, vm.Mods, vm.Profile.RootPath, msg => vm.Log.Log(msg));
            if (result != ManagedMain.Services.TreeTransformPort.StructureOpResult.None)
            {
                // Recalculate enabled states after structural changes
                vm.RecalculateAllEnabledStates();
                vm.Save();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachMessageHook();
            StartGameMonitor();
        }

        private void StartGameMonitor()
        {
            _gamePingTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _gamePingTimer.Tick -= GamePingTimer_Tick;
            _gamePingTimer.Tick += GamePingTimer_Tick;
            _gamePingTimer.Start();
        }
        private void StopGameMonitor()
        {
            try { _gamePingTimer?.Stop(); } catch { }
        }
        private void GamePingTimer_Tick(object? sender, EventArgs e)
        {
            bool running = ManagedMain.Services.GameLauncher.IsHelldivers2Running();
            try { WaveEffect.SetIsSurging(this.BtnLaunchGame, running); } catch { }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachMessageHook();
            _autoScrollTimer.Stop();
            _dragActive = false;
        }

        private void AttachMessageHook()
        {
            try
            {
                if (_hwndSrc != null) return;
                var src = PresentationSource.FromVisual(this) as HwndSource;
                if (src != null)
                {
                    _hwndSrc = src;
                    _hwndSrc.AddHook(WndProc);
                }
            }
            catch { }
        }

        private void DetachMessageHook()
        {
            try
            {
                if (_hwndSrc != null)
                {
                    _hwndSrc.RemoveHook(WndProc);
                    _hwndSrc = null;
                }
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (msg == WM_MOUSEWHEEL && _dragActive)
            {
                // HIWORD of wParam is wheel delta (signed short)
                int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                var viewer = FindVisualChild<ScrollViewer>(ModsTree);
                if (viewer != null)
                {
                    double baseStep = 96; // keep consistent with Preview handler
                    int ticks = Math.Max(1, Math.Abs(delta) / 120);
                    double step = baseStep * ticks;
                    if (delta > 0) viewer.ScrollToVerticalOffset(viewer.VerticalOffset - step);
                    else if (delta < 0) viewer.ScrollToVerticalOffset(viewer.VerticalOffset + step);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void ClearAllSelections(ManagedMain.ViewModels.ProfileModsViewModel vm)
        {
            foreach (var m in vm.Mods)
            {
                m.IsSelected = false;
                foreach (var o in m.Options)
                {
                    o.IsSelected = false;
                    foreach (var s in o.SubOptions) s.IsSelected = false;
                }
            }
        }

        private static bool IsSelected(object data) => data switch
        {
            MainModItem m => m.IsSelected,
            OptionItem o => o.IsSelected,
            SubOptionItem s => s.IsSelected,
            _ => false
        };

        private void ToggleSelected(object data)
        {
            switch (data)
            {
                case MainModItem m: m.IsSelected = !m.IsSelected; break;
                case OptionItem o: o.IsSelected = !o.IsSelected; break;
                case SubOptionItem s: s.IsSelected = !s.IsSelected; break;
            }
        }
        private void SetSelected(object data, bool sel)
        {
            switch (data)
            {
                case MainModItem m: m.IsSelected = sel; break;
                case OptionItem o: o.IsSelected = sel; break;
                case SubOptionItem s: s.IsSelected = sel; break;
            }
        }
        private List<object> GetSelectedItems()
        {
            var list = new List<object>();
            if (DataContext is not ManagedMain.ViewModels.ProfileModsViewModel vm) return list;
            foreach (var m in vm.Mods)
            {
                if (m.IsSelected) list.Add(m);
                foreach (var o in m.Options)
                {
                    if (o.IsSelected) list.Add(o);
                    foreach (var s in o.SubOptions) if (s.IsSelected) list.Add(s);
                }
            }
            return list;
        }
        private void RangeSelect(object data, ManagedMain.ViewModels.ProfileModsViewModel vm)
        {
            var siblings = new List<object>();
            if (data is MainModItem) siblings.AddRange(vm.Mods.Cast<object>());
            else if (data is OptionItem o)
            {
                var parent = vm.Mods.FirstOrDefault(m => m.Options.Contains(o));
                if (parent != null) siblings.AddRange(parent.Options.Cast<object>());
            }
            else if (data is SubOptionItem s)
            {
                var parentOpt = vm.Mods.SelectMany(m => m.Options).FirstOrDefault(x => x.SubOptions.Contains(s));
                if (parentOpt != null) siblings.AddRange(parentOpt.SubOptions.Cast<object>());
            }
            object anchor = _lastShiftRange.FirstOrDefault() ?? data;
            int i1 = siblings.IndexOf(anchor); int i2 = siblings.IndexOf(data);
            if (i1 >= 0 && i2 >= 0)
            {
                int from = Math.Min(i1, i2), to = Math.Max(i1, i2);
                for (int i = from; i <= to; i++) SetSelected(siblings[i], true);
                _lastShiftRange = siblings.Skip(from).Take(to - from + 1).ToList();
            }
        }

        private void ToggleLog_Click(object sender, RoutedEventArgs e)
        {
            // Existing log drawer toggle, kept
        }

        // Helper to manually toggle status drawer if needed
        private void BtnStatusToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Status toggle clicked");
                StatusDrawerHost.Toggle(this);
            }
            catch (System.Exception ex)
            {
                var vm = DataContext as ManagedMain.ViewModels.ProfileModsViewModel;
                vm?.Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_StatusDrawerToggleFailed, ex.Message));
            }
        }

        private void ModsTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ManagedMain.ViewModels.ProfileModsViewModel vm)
            {
                vm.SelectedItem = e.NewValue;
            }
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
            catch { }
            e.Handled = true;
        }

        private void StatusBox_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is not ManagedMain.ViewModels.ProfileModsViewModel vm) return;
            switch (vm.SelectedItem)
            {
                case ManagedMain.Models.MainModItem:
                case ManagedMain.Models.OptionItem:
                case ManagedMain.Models.SubOptionItem:
                    int state = GetCurrentEnabled(vm.SelectedItem);
                    if (state == 0)
                        vm.EnableSelectedCommand.Execute(null);
                    else
                        vm.DisableSelectedCommand.Execute(null);
                    break;
            }
            e.Handled = true;
        }

        private static int GetCurrentEnabled(object? item)
        {
            return item switch
            {
                ManagedMain.Models.MainModItem m => m.Enabled,
                ManagedMain.Models.OptionItem o => o.Enabled,
                ManagedMain.Models.SubOptionItem s => s.Enabled,
                _ => 0
            };
        }

        private static bool IsArchive(string p) => new[] { ".zip", ".7z", ".rar" }.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant());
        private static bool IsImage(string p) => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant());
        private static bool IsPatchFile(string p) => Regex.IsMatch(System.IO.Path.GetFileName(p) ?? string.Empty, @"^[a-fA-F0-9]{16}\.patch_\d+(?:\.stream|\.gpu_resources)?$");

        private void Root_DragOver(object sender, DragEventArgsWpf e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) { e.Effects = System.Windows.DragDropEffects.None; HideOverlay(); return; }
            var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (paths.Length == 0) { e.Effects = System.Windows.DragDropEffects.None; HideOverlay(); return; }

            bool anyArchive = paths.Any(IsArchive);
            bool anyDir = paths.Any(Directory.Exists);
            bool anyImg = paths.Any(IsImage);
            bool anyPatch = paths.Any(IsPatchFile);

            if (!(anyArchive || anyDir || anyImg || anyPatch)) { e.Effects = System.Windows.DragDropEffects.None; HideOverlay(); return; }

            ShowOverlay();
            e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true;
        }

        private void Root_DragLeave(object sender, DragEventArgsWpf e)
        { HideOverlay(); }

        private async void Root_Drop(object sender, DragEventArgsWpf e)
        {
            HideOverlay();
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (paths.Length == 0) return;

            var vm = DataContext as ManagedMain.ViewModels.ProfileModsViewModel; if (vm == null) return;

            var archives = paths.Where(IsArchive).ToList();
            var dirs = paths.Where(Directory.Exists).ToList();
            var images = paths.Where(IsImage).ToList();
            var patchFiles = paths.Where(IsPatchFile).ToList();

            try
            {
                if (archives.Any()) { await ImportArchivesAsync(vm, archives); return; }
                if (dirs.Any()) { await ImportFoldersAsync(vm, dirs); return; }
                if (images.Any()) { await ApplyImageToSelectedAsync(vm, images.First()); return; }
                if (patchFiles.Any()) { await AddPatchFilesToSelectedAsync(vm, patchFiles); return; }
            }
            catch (Exception ex)
            {
                vm.Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_DragImportFailed, ex.Message));
            }
        }

        private async Task ImportArchivesAsync(ManagedMain.ViewModels.ProfileModsViewModel vm, IEnumerable<string> archives)
        {
            vm.IsBusy = true;
            vm.IsImportArchiveRunning = true;
            try
            {
                var svc = new ManagedMain.Services.ImportService();
                var results = await Task.Run(() =>
                {
                    var list = new List<MainModItem>();
                    foreach (var a in archives)
                    {
                        var item = svc.ImportArchiveAsMod(vm.Profile.RootPath, a);
                        list.Add(item);
                    }
                    return list;
                });
                foreach (var item in results) vm.Mods.Add(item);
                vm.Save();
                vm.Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_ImportedArchives, archives.Count()));
            }
            finally { vm.IsImportArchiveRunning = false; vm.IsBusy = false; }
        }

        private async Task ImportFoldersAsync(ManagedMain.ViewModels.ProfileModsViewModel vm, IEnumerable<string> dirs)
        {
            vm.IsBusy = true;
            vm.IsImportFolderRunning = true;
            try
            {
                var svc = new ManagedMain.Services.ImportService();
                var results = await Task.Run(() =>
                {
                    var list = new List<MainModItem>();
                    foreach (var d in dirs)
                    {
                        var item = svc.ImportFolderAsMod(vm.Profile.RootPath, d);
                        list.Add(item);
                    }
                    return list;
                });
                foreach (var item in results) vm.Mods.Add(item);
                vm.Save();
                vm.Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_ImportedFolders, dirs.Count()));
            }
            finally { vm.IsImportFolderRunning = false; vm.IsBusy = false; }
        }

        private async Task ApplyImageToSelectedAsync(ManagedMain.ViewModels.ProfileModsViewModel vm, string imagePath)
        {
            if (vm.SelectedItem == null) { vm.Log.Log(ManagedMain.Resources.Strings.SR_Log_NoSelection); return; }
            vm.IsBusy = true;
            try
            {
                await Task.Run(() =>
                {
                    switch (vm.SelectedItem)
                    {
                        case MainModItem m:
                        {
                            var dest = Path.Combine(vm.Profile.RootPath, m.Name, Path.GetFileName(imagePath));
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            File.Copy(imagePath, dest, true);
                            WpfApp.Current.Dispatcher.Invoke(() => { m.IconPath = Path.GetFileName(imagePath); m.Image = m.IconPath; });
                            break;
                        }
                        case OptionItem o:
                        {
                            MainModItem? parent = null;
                            WpfApp.Current.Dispatcher.Invoke(() => { parent = vm.Mods.FirstOrDefault(x => x.Options.Contains(o)); });
                            if (parent == null) throw new InvalidOperationException(ManagedMain.Resources.Strings.SR_Log_NotFoundParentMain);
                            var dest = Path.Combine(vm.Profile.RootPath, parent!.Name, o.Name, Path.GetFileName(imagePath));
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            File.Copy(imagePath, dest, true);
                            WpfApp.Current.Dispatcher.Invoke(() => { o.IconPath = o.Name + "/" + Path.GetFileName(imagePath); o.Image = o.IconPath; });
                            break;
                        }
                        case SubOptionItem s:
                        {
                            MainModItem? parentMod = null; OptionItem? parentOpt = null;
                            WpfApp.Current.Dispatcher.Invoke(() =>
                            {
                                foreach (var mod in vm.Mods)
                                {
                                    var opt = mod.Options.FirstOrDefault(op => op.SubOptions.Contains(s));
                                    if (opt != null) { parentMod = mod; parentOpt = opt; break; }
                                }
                            });
                            if (parentMod == null || parentOpt == null) throw new InvalidOperationException(ManagedMain.Resources.Strings.SR_Log_NotFoundParentMainOption);
                            var dest = Path.Combine(vm.Profile.RootPath, parentMod!.Name, parentOpt!.Name, s.Name, Path.GetFileName(imagePath));
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            File.Copy(imagePath, dest, true);
                            WpfApp.Current.Dispatcher.Invoke(() => { s.IconPath = parentOpt!.Name + "/" + s.Name + "/" + Path.GetFileName(imagePath); s.Image = s.IconPath; });
                            break;
                        }
                    }
                });
                vm.Save();
                vm.Log.Log(ManagedMain.Resources.Strings.SR_Log_ImageUpdated);
            }
            catch (Exception ex) { vm.Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_SetImageFailed, ex.Message)); }
            finally { vm.IsBusy = false; }
        }

        private async Task AddPatchFilesToSelectedAsync(ManagedMain.ViewModels.ProfileModsViewModel vm, IEnumerable<string> patchFiles)
        {
            if (vm.SelectedItem == null) { vm.Log.Log(ManagedMain.Resources.Strings.SR_Log_NoSelection); return; }
            vm.IsBusy = true;
            var regex = new Regex(@"([a-fA-F0-9]{16})\.patch_(\d+)(?:\.stream|\.gpu_resources)?$", RegexOptions.Compiled);
            try
            {
                string baseDir = string.Empty;
                string relPrefix = string.Empty;
                MainModItem? mainParent = null;
                switch (vm.SelectedItem)
                {
                    case MainModItem mm:
                        baseDir = Path.Combine(vm.Profile.RootPath, mm.Name);
                        mainParent = mm;
                        break;
                    case OptionItem oo:
                        mainParent = vm.Mods.FirstOrDefault(m => m.Options.Contains(oo)); if (mainParent == null) { vm.Log.Log(ManagedMain.Resources.Strings.SR_Log_NotFoundParentMain); vm.IsBusy = false; return; }
                        baseDir = Path.Combine(vm.Profile.RootPath, mainParent.Name, oo.Name);
                        relPrefix = oo.Name + "/";
                        break;
                    case SubOptionItem ss:
                        foreach (var mod in vm.Mods)
                        {
                            var opt = mod.Options.FirstOrDefault(op => op.SubOptions.Contains(ss));
                            if (opt != null) { mainParent = mod; baseDir = Path.Combine(vm.Profile.RootPath, mod.Name, opt.Name, ss.Name); relPrefix = opt.Name + "/" + ss.Name + "/"; break; }
                        }
                        if (string.IsNullOrEmpty(baseDir)) { vm.Log.Log(ManagedMain.Resources.Strings.SR_Log_NotFoundParentMainOption); vm.IsBusy = false; return; }
                        break;
                    default:
                        vm.Log.Log(ManagedMain.Resources.Strings.SR_Log_UnsupportedTarget); vm.IsBusy = false; return;
                }
                Directory.CreateDirectory(baseDir);

                var groups = await Task.Run(() =>
                {
                    var map = new Dictionary<string, ModFileGroup>();
                    foreach (var f in patchFiles)
                    {
                        var name = Path.GetFileName(f); var m = regex.Match(name); if (!m.Success) continue; var hex = m.Groups[1].Value; int pn = int.Parse(m.Groups[2].Value);
                        var key = hex + "." + pn;
                        if (!map.TryGetValue(key, out var g)) { g = new ModFileGroup { HexPrefix = hex, PatchN = pn, RelativePath = hex, Files = new List<string>() }; map[key] = g; }
                        var dest = Path.Combine(baseDir, name);
                        File.Copy(f, dest, true);
                        g.Files.Add(relPrefix + name);
                    }
                    return map.Values.ToList();
                });

                if (!groups.Any()) { vm.Log.Log(ManagedMain.Resources.Strings.SR_Log_UnrecognizedPatch); vm.IsBusy = false; return; }

                switch (vm.SelectedItem)
                {
                    case MainModItem mm:
                        foreach (var g in groups) mm.FileGroups.Add(g);
                        break;
                    case OptionItem oo:
                        foreach (var g in groups) oo.FileGroups.Add(g);
                        break;
                    case SubOptionItem ss:
                        ss.FileGroups.AddRange(groups);
                        break;
                }
                vm.Save();
                vm.Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_AddedFileGroups, groups.Count));
            }
            catch (Exception ex) { vm.Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_AddFileGroupsFailed, ex.Message)); }
            finally { vm.IsBusy = false; }
        }

        private void ShowOverlay() { if (DropOverlay != null) DropOverlay.Visibility = Visibility.Visible; }
        private void HideOverlay() { if (DropOverlay != null) DropOverlay.Visibility = Visibility.Collapsed; }

        private void BtnLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ManagedMain.ViewModels.ProfileModsViewModel vm)
            {
                vm.Log.Log(ManagedMain.Resources.Strings.SR_Log_ClickLaunchGame_ProfileModsView);
                ManagedMain.Services.GameLauncher.LaunchHelldivers2(vm.Log);
            }
            else
            {
                try
                {
                    var shell = System.Windows.Application.Current?.MainWindow?.DataContext as ManagedMain.ViewModels.ShellViewModel;
                    var mainTab = shell?.Tabs?.FirstOrDefault()?.Content as ManagedMain.Views.ManagedMainView;
                    if (mainTab?.DataContext is ManagedMain.ViewModels.ManagedMainViewModel mm)
                    {
                        mm.Log.Log(ManagedMain.Resources.Strings.SR_Log_ClickLaunchGame_Fallback);
                        ManagedMain.Services.GameLauncher.LaunchHelldivers2(mm.Log);
                    }
                }
                catch { }
            }
        }
    }
}
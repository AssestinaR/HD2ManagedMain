using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.IO;

namespace ManagedMain.Views
{
    public partial class ManagedMainView : System.Windows.Controls.UserControl
    {
        private Storyboard? _breathNew;
        private Storyboard? _breathImport;
        private DispatcherTimer? _gamePingTimer;

        public ManagedMainView()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TryStartGuidance();
            TryAutoDetectGameFolder();
            StartGameMonitor();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopGuidance();
            StopGameMonitor();
        }

        private void StartGameMonitor()
        {
            _gamePingTimer ??= new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(2) };
            _gamePingTimer.Tick -= GamePingTimer_Tick;
            _gamePingTimer.Tick += GamePingTimer_Tick;
            _gamePingTimer.Start();
        }
        private void StopGameMonitor()
        { try { _gamePingTimer?.Stop(); } catch { } }

        private void GamePingTimer_Tick(object? sender, System.EventArgs e)
        {
            bool running = ManagedMain.Services.GameLauncher.IsHelldivers2Running();
            try { WaveEffect.SetIsSurging(this.BtnLaunchGame_Main, running); } catch { }
            try
            {
                var mw = System.Windows.Application.Current?.MainWindow;
                if (mw?.DataContext is ManagedMain.ViewModels.ShellViewModel shell && shell.SelectedTab?.Content is System.Windows.Controls.UserControl uc)
                {
                    // Propagate GameFolder once detected
                    if (uc.DataContext is ManagedMain.ViewModels.ProfileModsViewModel pvm)
                    {
                        var gf = pvm.GameFolder;
                        if (string.IsNullOrWhiteSpace(gf))
                        {
                            var found = ManagedMain.Services.SteamLocator.TryFindHelldivers2Data();
                            if (!string.IsNullOrWhiteSpace(found))
                            {
                                pvm.GameFolder = found;
                            }
                        }
                    }
                    var modsView = FindChildByName<System.Windows.Controls.Button>(uc, "BtnLaunchGame");
                    if (modsView != null) WaveEffect.SetIsSurging(modsView, running);
                }
            }
            catch { }
        }

        private static T? FindChildByName<T>(System.Windows.DependencyObject parent, string name) where T : System.Windows.FrameworkElement
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == name) return fe;
                var res = FindChildByName<T>(child, name); if (res != null) return res;
            }
            return null;
        }

        private void BtnLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ManagedMain.ViewModels.ManagedMainViewModel vm)
            {
                vm.Log.Log(ManagedMain.Resources.Strings.SR_Log_ClickLaunchGame_Fallback);
                ManagedMain.Services.GameLauncher.LaunchHelldivers2(vm.Log);
            }
        }

        private void TryAutoDetectGameFolder()
        {
            try
            {
                if (DataContext is not ManagedMain.ViewModels.ManagedMainViewModel vm) return;
                var current = vm.Options?.GameFolder ?? string.Empty;
                bool missing = string.IsNullOrWhiteSpace(current) || !System.IO.Directory.Exists(current) || !System.IO.Directory.EnumerateFiles(current, "*.patch_*", System.IO.SearchOption.TopDirectoryOnly).Any();
                if (!missing) return;
                var found = ManagedMain.Services.SteamLocator.TryFindHelldivers2Data();
                if (!string.IsNullOrWhiteSpace(found))
                {
                    vm.Options.GameFolder = found!;
                    vm.Save();
                    vm.Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_AutoDetectGameFolder, found));
                }
            }
            catch { }
        }

        private void TryStartGuidance()
        {
            if (DataContext is not ManagedMain.ViewModels.ManagedMainViewModel vm) return;
            if (vm.Profiles.Count == 0)
            {
                StartBreathingOn(this.BtnNewProfile);
                StartBreathingOn(this.BtnImportProfile);
            }
            else
            {
                StopBreathingOn(this.BtnNewProfile);
                StopBreathingOn(this.BtnImportProfile);
            }
            vm.Profiles.CollectionChanged -= Profiles_CollectionChanged;
            vm.Profiles.CollectionChanged += Profiles_CollectionChanged;
        }

        private void Profiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (DataContext is not ManagedMain.ViewModels.ManagedMainViewModel vm) return;
            if (vm.Profiles.Count == 0)
            {
                StartBreathingOn(this.BtnNewProfile);
                StartBreathingOn(this.BtnImportProfile);
            }
            else
            {
                StopBreathingOn(this.BtnNewProfile);
                StopBreathingOn(this.BtnImportProfile);
            }
        }

        private void StartBreathingOn(System.Windows.Controls.Button btn)
        {
            try
            {
                var weak = (System.Windows.Application.Current?.Resources["ButtonAccentWeakBrush"] as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.SkyBlue;
                var accent = (System.Windows.Application.Current?.Resources["ButtonAccentBrush"] as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.DodgerBlue;
                var border = (System.Windows.Application.Current?.Resources["ButtonAccentMutedBrush"] as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.SteelBlue;

                var bg = new System.Windows.Media.SolidColorBrush(weak);
                var bd = new System.Windows.Media.SolidColorBrush(border);
                btn.Background = bg; btn.BorderBrush = bd;

                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                var bgAnim = new ColorAnimation { From = weak, To = accent, Duration = System.TimeSpan.FromMilliseconds(1100), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                var bdAnim = new ColorAnimation { From = border, To = accent, Duration = System.TimeSpan.FromMilliseconds(1100), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                bg.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, bgAnim);
                bd.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, bdAnim);
            }
            catch { }
        }

        private void StopBreathingOn(System.Windows.Controls.Button btn)
        {
            try
            {
                if (btn.Background is System.Windows.Media.SolidColorBrush bg) bg.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, null);
                if (btn.BorderBrush is System.Windows.Media.SolidColorBrush bd) bd.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, null);
                btn.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                btn.ClearValue(System.Windows.Controls.Button.BorderBrushProperty);
            }
            catch { }
        }

        private void StopGuidance()
        {
            StopBreathingOn(this.BtnNewProfile);
            StopBreathingOn(this.BtnImportProfile);
        }

        private void TreeItem_Expanded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.TreeViewItem tvi && tvi.DataContext is ManagedMain.Models.ProfileEntry p)
            {
                if (DataContext is ManagedMain.ViewModels.ManagedMainViewModel vm)
                {
                    vm.EnsureModsLoaded(p);
                }
            }
        }

        private static ManagedMain.Models.ProfileEntry? FindOwningProfile(object? dc)
        {
            if (dc == null) return null;
            if (dc is ManagedMain.Models.ProfileEntry p) return p;
            return null;
        }

        private void ProfilesTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ManagedMain.ViewModels.ManagedMainViewModel vm)
            {
                var selected = e.NewValue;
                // Resolve owning profile via walking parents of the container
                var container = ProfilesTree.ContainerFromItemRecursive(selected) as TreeViewItem;
                var owner = ResolveProfileFromContainer(container);
                if (owner != null)
                {
                    // Lazy-load when selecting a node within a profile
                    vm.EnsureModsLoaded(owner);
                    vm.SelectedProfile = owner;
                }
                else
                {
                    vm.SelectedProfile = selected as ManagedMain.Models.ProfileEntry;
                }
            }
        }

        private ManagedMain.Models.ProfileEntry? ResolveProfileFromContainer(TreeViewItem? item)
        {
            var current = item;
            while (current != null)
            {
                if (current.DataContext is ManagedMain.Models.ProfileEntry p) return p;
                current = ItemsControl.ItemsControlFromItemContainer(current) as TreeViewItem;
            }
            return null;
        }

        private void ProfilesTree_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as System.Windows.DependencyObject;
            while (element != null && element is not System.Windows.Controls.TreeViewItem)
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            if (element is System.Windows.Controls.TreeViewItem tvi)
            {
                // Find owning ProfileEntry for any level
                var owner = ResolveProfileFromContainer(tvi);
                if (owner != null && DataContext is ManagedMain.ViewModels.ManagedMainViewModel vm)
                {
                    // Ensure mods are loaded before opening tab
                    vm.EnsureModsLoaded(owner);
                    vm.OpenSelectedProfileCommand.Execute(owner);
                    e.Handled = true;
                }
            }
        }

        private void ProfilesTree_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Cancel selection on right click
            try
            {
                var element = e.OriginalSource as System.Windows.DependencyObject;
                while (element != null && element is not System.Windows.Controls.TreeViewItem)
                    element = System.Windows.Media.VisualTreeHelper.GetParent(element);
                if (element is System.Windows.Controls.TreeViewItem tvi)
                {
                    tvi.IsSelected = false;
                    e.Handled = true;
                }
            }
            catch { }
        }
    }

    internal static class TreeViewExtensions
    {
        public static System.Windows.DependencyObject? ContainerFromItemRecursive(this ItemsControl parent, object? item)
        {
            if (parent == null) return null;
            var direct = parent.ItemContainerGenerator.ContainerFromItem(item);
            if (direct != null) return direct;
            foreach (var child in parent.Items)
            {
                var childContainer = parent.ItemContainerGenerator.ContainerFromItem(child) as ItemsControl;
                if (childContainer == null) continue;
                var result = ContainerFromItemRecursive(childContainer, item);
                if (result != null) return result;
            }
            return null;
        }
    }
}

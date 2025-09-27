using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;
using ManagedMain.Models;
using ManagedMain.Views;
using System.Collections.Specialized;

namespace ManagedMain.ViewModels
{
    public class ShellViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TabItemViewModel> Tabs { get; } = new();
        private TabItemViewModel? _selectedTab;
        public TabItemViewModel? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab == value) return;
                _selectedTab = value; OnPropertyChanged();
                // Persist last focused profile id
                if (_mainVM != null)
                {
                    var pid = (_selectedTab?.ProfileId);
                    _mainVM.Options.LastFocusedProfileId = pid;
                    _mainVM.Save();
                }
            }
        }

        public ICommand CloseTabCommand { get; }
        private readonly ManagedMainViewModel _mainVM;

        public ShellViewModel()
        {
            CloseTabCommand = new RelayCommand(p => CloseTab(p as TabItemViewModel));

            var managedView = new ManagedMainView();
            _mainVM = new ManagedMainViewModel(OpenProfileTab);
            managedView.DataContext = _mainVM;
            var settingsTab = new TabItemViewModel
            {
                Header = "ManagedMain",
                IsClosable = false,
                Content = managedView,
            };
            Tabs.Add(settingsTab);
            SelectedTab = settingsTab;

            // Auto-close tabs if profiles get removed
            _mainVM.Profiles.CollectionChanged += Profiles_CollectionChanged;

            // Auto-open tabs from options
            foreach (var p in _mainVM.Profiles.Where(x => x.IsOpen))
            {
                OpenProfileTab(p);
            }
            // Restore focused tab
            if (_mainVM.Options.LastFocusedProfileId.HasValue)
            {
                var target = Tabs.FirstOrDefault(t => t.ProfileId == _mainVM.Options.LastFocusedProfileId);
                if (target != null) SelectedTab = target;
            }
        }

        private void Profiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Handle explicit removals
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is ProfileEntry removed)
                    {
                        var tab = Tabs.FirstOrDefault(t => t.ProfileId == removed.Id);
                        if (tab != null) CloseTab(tab);
                    }
                }
            }
            // Handle reset (e.g., reload) by closing tabs whose profile no longer exists
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                var existingIds = _mainVM.Profiles.Select(p => p.Id).ToHashSet();
                var toClose = Tabs.Where(t => t.IsClosable && t.ProfileId.HasValue && !existingIds.Contains(t.ProfileId.Value)).ToList();
                foreach (var tab in toClose) CloseTab(tab);
            }
        }

        public void OpenProfileTab(ProfileEntry profile)
        {
            foreach (var t in Tabs)
            {
                if (t.ProfileId == profile.Id || t.Header == profile.Name)
                { SelectedTab = t; return; }
            }
            var view = new ProfileModsView();
            view.DataContext = new ProfileModsViewModel(profile);
            var tab = new TabItemViewModel
            {
                Header = profile.Name,
                IsClosable = true,
                Content = view,
                ProfileId = profile.Id
            };
            Tabs.Add(tab);
            SelectedTab = tab;
        }

        public void CloseTab(TabItemViewModel? tab)
        {
            if (tab == null || !tab.IsClosable) return;
            // If the tab to be closed is selected, move selection to a neighbor (optional UX polish)
            var wasSelected = ReferenceEquals(SelectedTab, tab);
            int idx = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            if (wasSelected)
            {
                if (idx >= 0 && idx < Tabs.Count) SelectedTab = Tabs[idx];
                else if (Tabs.Count > 0) SelectedTab = Tabs.Last();
            }

            if (tab.ProfileId.HasValue)
            {
                var p = _mainVM.Profiles.FirstOrDefault(x => x.Id == tab.ProfileId.Value);
                if (p != null)
                {
                    p.IsOpen = false;
                    _mainVM.Save();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TabItemViewModel : INotifyPropertyChanged
    {
        public string Header { get; set; } = string.Empty;
        public bool IsClosable { get; set; } = true;
        public object? Content { get; set; }
        public System.Guid? ProfileId { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

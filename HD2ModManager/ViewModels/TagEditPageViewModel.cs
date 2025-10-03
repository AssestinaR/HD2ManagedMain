using System.Collections.ObjectModel;
using System.Linq;
using HD2ModManager.Models;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    public class TagEditPageViewModel : PageViewModel
    {
        private readonly ModLibraryService _library;
        private readonly TagCatalogService _catalog = TagCatalogService.Instance;

        public ModEntity Current { get; private set; }
        public ObservableCollection<string> Tags { get; } = new();
        public ObservableCollection<string> AvailableTags { get; } = new();
        private System.ComponentModel.ICollectionView? _availableView;
        private string _query = string.Empty;
        public string Query { get => _query; set { _query = value; _availableView?.Refresh(); } }

        public RelayCommand SaveAndNextCommand { get; }

        private readonly System.Collections.Generic.Queue<string> _queue = new();
        private readonly string _returnKey;

        public TagEditPageViewModel(ModLibraryService library, ModEntity first, System.Collections.Generic.IEnumerable<string> pendingGuids, string returnKey)
        {
            Title = HD2ModManager.Resources.Strings.TagEdit_Title;
            _library = library;
            _returnKey = returnKey;
            foreach (var g in pendingGuids) _queue.Enqueue(g);
            Load(first);
            SaveAndNextCommand = new RelayCommand(SaveAndNext);
        }

        private void Load(ModEntity mod)
        {
            Current = mod;
            Tags.Clear();
            AvailableTags.Clear();
            foreach (var t in mod.Tags) Tags.Add(t);
            foreach (var t in _catalog.GetAll())
            {
                var name = t.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!AvailableTags.Contains(name) && !Tags.Contains(name)) AvailableTags.Add(name);
            }
            _availableView = System.Windows.Data.CollectionViewSource.GetDefaultView(AvailableTags);
            if (_availableView != null) _availableView.Filter = FilterAvailable;
        }

        private bool FilterAvailable(object obj)
        {
            var s = obj as string; if (s == null) return false;
            var q = _query?.Trim(); if (string.IsNullOrWhiteSpace(q)) return true;
            return s.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SaveAndNext()
        {
            Current.Tags = Tags.ToList();
            _library.Add(Current);
            _library.Save();
            if (_queue.Count > 0)
            {
                var nextGuid = _queue.Dequeue();
                var next = _library.Get(nextGuid);
                if (next != null) Load(next);
                else SaveAndReturn();
            }
            else
            {
                SaveAndReturn();
            }
        }

        private void SaveAndReturn()
        {
            var shell = System.Windows.Application.Current?.MainWindow as HD2ModManager.MainWindow;
            var vm = shell?.DataContext as ShellViewModel;
            if (vm != null)
            {
                // go back one breadcrumb level to return page
                var targetIndex = vm.Breadcrumbs.Count - 2;
                if (targetIndex >= 0)
                    vm.GoBackToIndexCommand.Execute(targetIndex);
            }
        }
    }
}

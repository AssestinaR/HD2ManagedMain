using System.Collections.ObjectModel;
using HD2ModManager.Enums;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    public class ShellViewModel : BaseViewModel
    {
        private readonly INavigationService _nav;
        public ReadOnlyObservableCollection<string> Breadcrumbs => _nav.Breadcrumbs;

        private PageViewModel? _currentPage;
        public PageViewModel? CurrentPage { get => _currentPage; set => SetField(ref _currentPage, value); }

        private LayoutMode _currentLayout = LayoutMode.Simple;
        public LayoutMode CurrentLayout { get => _currentLayout; set => SetField(ref _currentLayout, value); }

        public RelayCommand ToggleLayoutCommand { get; }
        public RelayCommand ApplyChangesCommand { get; }
        public RelayCommand GoBackToIndexCommand { get; }
        public System.Collections.ObjectModel.ReadOnlyObservableCollection<Services.NotificationItem> Notifications => _notifications;
        private readonly System.Collections.ObjectModel.ReadOnlyObservableCollection<Services.NotificationItem> _notifications;
        private readonly Services.NotificationService _notificationService;

        private readonly ProfileService _profileService;
        private readonly Services.ModLibraryService _libraryService;
        private readonly Services.ImportQueueService _importQueue;
        private readonly System.Collections.ObjectModel.ObservableCollection<string> _tagQueue = new();
        private readonly Services.TagCsvSuggestionService _tagSuggest;
        private readonly Services.TagCatalogService _tagCatalog = Services.TagCatalogService.Instance;
        private System.Collections.Generic.List<string> _pendingTagEdit = new();

        public ShellViewModel()
        {
            _nav = new NavigationService();
            _nav.Navigated += OnNavigated;

            // Initial page: Home
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var profilesDir = System.IO.Path.Combine(baseDir, "config");
            if (!System.IO.Directory.Exists(profilesDir)) System.IO.Directory.CreateDirectory(profilesDir);
            _profileService = new ProfileService(profilesDir);
            _profileService.Load();

            var libraryPath = System.IO.Path.Combine(profilesDir, "library.json");
            _libraryService = new Services.ModLibraryService(libraryPath);
            _libraryService.Load();
            _importQueue = new Services.ImportQueueService();

            _notificationService = new Services.NotificationService();
            _notifications = _notificationService.Items;

            // Initialize tag suggestion & catalog; build tags.json from CSV on first run
            _tagSuggest = new Services.TagCsvSuggestionService(baseDir);
            var configDir = System.IO.Path.Combine(baseDir, "config");
            _tagCatalog.Load(configDir);
            if (_tagCatalog.GetAll().Count == 0)
            {
                _tagCatalog.RebuildFromCsv(baseDir);
                var ok = _tagCatalog.Save();
                _tagCatalog.Load(configDir);
                _notificationService.Show(ok ? $"Tags rebuilt: {_tagCatalog.GetAll().Count}" : "Failed to write tags.json", ok ? Services.NotificationLevel.Info : Services.NotificationLevel.Error);
            }
            else
            {
                // If tags loaded but file missing, write to ensure tags.json exists
                var tagsPath = System.IO.Path.Combine(configDir, "tags.json");
                if (!System.IO.File.Exists(tagsPath))
                {
                    var ok = _tagCatalog.Save();
                    _notificationService.Show(ok ? $"Tags saved: {_tagCatalog.GetAll().Count}" : "Failed to write tags.json", ok ? Services.NotificationLevel.Info : Services.NotificationLevel.Error);
                }
            }

            CurrentPage = new HomePageViewModel(key => _nav.GoTo(key), _profileService, _libraryService, _importQueue);

            // Startup integrity check and auto-fix
            try
            {
                if (Services.SettingsService.GetAutoCleanup())
                {
                    var integrity = new Services.IntegrityService(_libraryService, _notificationService, System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config"));
                    integrity.CheckAndFix();
                }
                // auto-detect game data folder if not set
                if (string.IsNullOrWhiteSpace(Services.SettingsService.GetGameDataFolder()))
                {
                    var detected = Services.SettingsService.TryDetectAndSetGameDataFolder();
                    if (!string.IsNullOrWhiteSpace(detected))
                    {
                        _notificationService.Show($"已检测到游戏数据目录: {detected}", NotificationLevel.Info, TimeSpan.FromSeconds(4));
                    }
                }
            }
            catch { }

            ToggleLayoutCommand = new RelayCommand(() =>
            {
                CurrentLayout = CurrentLayout == LayoutMode.Simple ? LayoutMode.Full : LayoutMode.Simple;
            });

            ApplyChangesCommand = new RelayCommand(() =>
            {
                // TODO: hook to activation service later
            });

            GoBackToIndexCommand = new RelayCommand(param =>
            {
                if (param is int idx) _nav.GoBackTo(idx);
                else if (param is string s && int.TryParse(s, out var i)) _nav.GoBackTo(i);
            });
        }

        private void OnNavigated(string key)
        {
            switch (key)
            {
                case "home":
                    CurrentPage = new HomePageViewModel(k => _nav.GoTo(k), _profileService, _libraryService, _importQueue);
                    break;
                case "settings":
                    CurrentPage = new SettingsPageViewModel();
                    break;
                case "library":
                    CurrentPage = new LibraryPageViewModel(_libraryService);
                    break;
                case "tagedit":
                    {
                        var firstGuid = _pendingTagEdit.FirstOrDefault();
                        var first = string.IsNullOrEmpty(firstGuid) ? null : _libraryService.Get(firstGuid);
                        var rest = _pendingTagEdit.Skip(1).ToList();
                        if (first != null)
                        {
                            CurrentPage = new TagEditPageViewModel(_libraryService, first, rest, returnKey: "library");
                        }
                        else
                        {
                            // fallback to library
                            CurrentPage = new LibraryPageViewModel(_libraryService);
                        }
                    }
                    break;
                // TODO: add profiles/import and others later
                default:
                    CurrentPage = new HomePageViewModel(k => _nav.GoTo(k), _profileService, _libraryService, _importQueue);
                    break;
            }
        }

        private void ShowQueueSummary()
        {
            try
            {
                var total = _importQueue.Tasks.Count;
                var imported = _importQueue.CountDone;
                var pending = _importQueue.CountQueued + _importQueue.CountRunning;
                _notificationService.Show($"导入队列 - 总数：{total}，已导入：{imported}，待处理：{pending}");
            }
            catch { }
        }

        // Expose services for future wiring
        public Services.ModLibraryService LibraryService => _libraryService;
        public Services.ProfileService ProfileService => _profileService;
        public Services.ImportQueueService ImportQueue => _importQueue;
        public System.Collections.ObjectModel.ReadOnlyObservableCollection<string> TagQueue => new(_tagQueue);

        public async System.Threading.Tasks.Task ProcessImportQueueAsync(string[] paths)
        {
            _importQueue.Enqueue(paths);
            ShowQueueSummary();
            // simple sequential consumption for now; can be parallelized later
            foreach (var item in _importQueue.Tasks)
            {
                if (item.Status != Services.ImportTaskStatus.Queued) continue;
                _importQueue.MarkRunning(item);
                var import = new Services.ImportService(_libraryService, onInfo: null, onError: (err) =>
                {
                    _importQueue.MarkFailed(item, err);
                });
                try
                {
                    var created = await import.ImportPathAsync(item.Path, new System.Threading.CancellationToken());
                    Services.LogService.Info($"Import created {created.Count} mods from {item.Path}");
                    _libraryService.Save();
                    _importQueue.MarkDone(item);
                    foreach (var g in created) _tagQueue.Add(g);
                    RefreshHomeLibraryStatus();
                    if (CurrentPage is LibraryPageViewModel lib)
                    {
                        try
                        {
                            Services.LogService.Info("Refreshing library page after import...");
                            System.Windows.Application.Current?.Dispatcher.Invoke(() => { lib.Refresh(); Services.LogService.Info("Library page refreshed."); });
                        }
                        catch { lib.Refresh(); }
                    }
                    _notificationService.Show(string.Format(HD2ModManager.Resources.Strings.Notification_ImportComplete, item.Path));
                    // refresh queue status card on home
                    RefreshHomeLibraryStatus();

                    // Navigate to tag edit page if there are new mods and setting enabled
                    if (created.Count > 0 && Services.SettingsService.GetAutoOpenTagEdit())
                    {
                        _pendingTagEdit = created.ToList();
                        _nav.GoTo("tagedit");
                    }
                }
                catch (System.Exception ex)
                {
                    _importQueue.MarkFailed(item, ex.Message);
                    System.Windows.MessageBox.Show($"导入失败: {item.Path}\n{ex.Message}", "Import", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    _notificationService.Show(string.Format(HD2ModManager.Resources.Strings.Notification_ImportFailed, item.Path), Services.NotificationLevel.Error);
                }
            }
            // Legacy popup tag edit removed; routed page handles tag edits
        }

        public void RefreshHomeLibraryStatus()
        {
            if (CurrentPage is HomePageViewModel home)
            {
                home.RefreshLibraryStatus();
            }
        }
    }
}

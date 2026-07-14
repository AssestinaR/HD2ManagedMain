using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using HD2ModCore.Infrastructure;
using HD2ModManager.Enums;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    public sealed class ShellViewModel : BaseViewModel
    {
        private readonly ProfileService _profileService;
        private readonly ModLibraryService _libraryService;
        private readonly ImportQueueService _importQueue;
        private readonly BackgroundTaskService _backgroundTasks;
        private readonly ApplyStatusService _applyStatus;
        private readonly NotificationService _notificationService;
        private readonly TagCatalogService _tagCatalog = TagCatalogService.Instance;
        private readonly SelectionCoordinator _selection = new();
        private readonly ObservableCollection<string> _tagQueue = new();
        private readonly SemaphoreSlim _importProcessGate = new(1, 1);
        private System.Collections.Generic.List<string> _pendingTagEdit = new();

        private PageViewModel? _leftPage;
        private PageViewModel? _rightPage;
        private WorkspaceMode _currentMode;
        private WorkspacePageType _leftPageType;
        private WorkspacePageType _rightPageType;
        private string? _selectedModId;

        public PageViewModel? CurrentPage => LeftPage;
        public PageViewModel? LeftPage
        {
            get => _leftPage;
            private set
            {
                if (ReferenceEquals(_leftPage, value)) return;
                var previous = _leftPage;
                if (SetField(ref _leftPage, value))
                {
                    if (!ReferenceEquals(previous, _rightPage)) (previous as IDisposable)?.Dispose();
                    OnPropertyChanged(nameof(CurrentPage));
                    RaiseActionFlags();
                }
            }
        }
        public PageViewModel? RightPage
        {
            get => _rightPage;
            private set
            {
                if (ReferenceEquals(_rightPage, value)) return;
                var previous = _rightPage;
                if (SetField(ref _rightPage, value))
                {
                    if (!ReferenceEquals(previous, _leftPage)) (previous as IDisposable)?.Dispose();
                    RaiseActionFlags();
                }
            }
        }
        public WorkspaceMode CurrentMode { get => _currentMode; private set { if (SetField(ref _currentMode, value)) RaiseModeFlags(); } }
        public WorkspacePageType LeftPageType { get => _leftPageType; private set { if (SetField(ref _leftPageType, value)) RaiseSlotFlags(); } }
        public WorkspacePageType RightPageType { get => _rightPageType; private set { if (SetField(ref _rightPageType, value)) RaiseSlotFlags(); } }
        public string? SelectedModId { get => _selectedModId; private set => SetField(ref _selectedModId, value); }
        public ReadOnlyObservableCollection<NotificationItem> Notifications { get; }
        public ReadOnlyObservableCollection<string> TagQueue => new(_tagQueue);

        public RelayCommand ShowHomeCommand { get; }
        public RelayCommand ShowStatusCommand { get; }
        public RelayCommand ShowProfileCommand { get; }
        public RelayCommand ShowLibraryCommand { get; }
        public RelayCommand ShowSplitCommand { get; }
        public RelayCommand ShowSettingsCommand { get; }
        public RelayCommand ApplyChangesCommand { get; }
        public RelayCommand ImportCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand CancelSelectionCommand { get; }
        public RelayCommand SelectionPrimaryCommand { get; }
        public RelayCommand SelectionSecondaryCommand { get; }
        public RelayCommand SelectionDeleteCommand { get; }

        public bool IsHomeActive => CurrentMode == WorkspaceMode.Home;
        public bool IsStatusActive => CurrentMode == WorkspaceMode.Status;
        public bool IsProfileActive => CurrentMode == WorkspaceMode.ProfileOnly;
        public bool IsLibraryActive => CurrentMode == WorkspaceMode.LibraryOnly;
        public bool IsSplitActive => CurrentMode == WorkspaceMode.ProfileLibrarySplit;
        public bool IsSettingsActive => CurrentMode == WorkspaceMode.Settings;
        public bool IsSplitView => LeftPageType != RightPageType;
        public bool ShowRightSlot => IsSplitView;
        public bool IsLeftSlotLibrary => LeftPageType == WorkspacePageType.Library;
        public bool IsRightSlotLibrary => IsSplitView && RightPageType == WorkspacePageType.Library;
        public bool IsFullScreenLibrary => !IsSplitView && LeftPageType == WorkspacePageType.Library;
        public bool IsSplitLeftSlotLibrary => IsSplitView && LeftPageType == WorkspacePageType.Library;
        public ObservableCollection<PageActionViewModel>? LeftSlotActions => LeftPage?.PageActions;
        public ObservableCollection<PageActionViewModel>? RightSlotActions => RightPage?.PageActions;
        public bool HasLeftSlotActions => LeftSlotActions?.Count > 0;
        public bool HasRightSlotActions => RightSlotActions?.Count > 0;
        public bool ShowSplitLeftSlotActions => IsSplitView && HasLeftSlotActions;
        public bool ShowRightSlotActions => IsSplitView && HasRightSlotActions;
        public bool ShowFullScreenActions => !IsSplitView && HasLeftSlotActions;
        public string LeftSlotTitle => GetPageTitle(LeftPageType);
        public string RightSlotTitle => GetPageTitle(RightPageType);

        public ModLibraryService LibraryService => _libraryService;
        public ProfileService ProfileService => _profileService;
        public ImportQueueService ImportQueue => _importQueue;
        public BackgroundTaskService BackgroundTasks => _backgroundTasks;
        public SelectionCoordinator Selection => _selection;
        public bool HasSelection => _selection.HasSelection;
        public string SelectionSummary => _selection.Summary;
        public string SelectionPrimaryText => string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase) ? "启用" : "加入配置";
        public string SelectionSecondaryText => string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase) ? "禁用" : "编辑标签";
        public string SelectionDeleteText => string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase) ? "移除" : "删除";

        public ShellViewModel()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configDir = System.IO.Path.Combine(baseDir, "config");
            System.IO.Directory.CreateDirectory(configDir);

            _profileService = new ProfileService(configDir);
            _profileService.Load();

            _libraryService = new ModLibraryService(System.IO.Path.Combine(configDir, "library.json"));
            _libraryService.Load(buildDerivedData: false);
            _importQueue = new ImportQueueService();
            _backgroundTasks = new BackgroundTaskService();
            _applyStatus = new ApplyStatusService();
            _notificationService = new NotificationService();
            Notifications = _notificationService.Items;

            InitializeTagCatalog(baseDir, configDir);
            RunStartupChecks(configDir);

            ShowHomeCommand = new RelayCommand(() => Navigate(WorkspaceMode.Home));
            ShowStatusCommand = new RelayCommand(() => Navigate(WorkspaceMode.Status));
            ShowProfileCommand = new RelayCommand(() => Navigate(WorkspaceMode.ProfileOnly));
            ShowLibraryCommand = new RelayCommand(() => Navigate(WorkspaceMode.LibraryOnly));
            ShowSplitCommand = new RelayCommand(() => Navigate(WorkspaceMode.ProfileLibrarySplit));
            ShowSettingsCommand = new RelayCommand(() => Navigate(WorkspaceMode.Settings));
            ApplyChangesCommand = new RelayCommand(ApplyActiveProfile);
            ImportCommand = new RelayCommand(BrowseAndImport);
            RefreshCommand = new RelayCommand(RefreshCurrentPage);
            CancelSelectionCommand = new RelayCommand(_selection.Clear);
            SelectionPrimaryCommand = new RelayCommand(ExecuteSelectionPrimary);
            SelectionSecondaryCommand = new RelayCommand(ExecuteSelectionSecondary);
            SelectionDeleteCommand = new RelayCommand(ExecuteSelectionDelete);
            _selection.SelectionChanged += (_, _) => RaiseSelectionFlags();

            Navigate(WorkspaceMode.Home);
            _ = RefreshLibraryDerivedDataAfterStartupAsync();
        }

        private async Task RefreshLibraryDerivedDataAfterStartupAsync()
        {
            var task = _backgroundTasks.Enqueue(BackgroundTaskKind.RefreshLibrary, "刷新模组派生数据", "启动缓存");
            try
            {
                task.MarkRunning("正在扫描模组文件");
                var dirtyCount = await _libraryService.RefreshDirtyDerivedDataAsync();
                task.UpdateStage($"已识别 {dirtyCount} 个需要刷新的 Mod");
                task.MarkCompleted();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(RefreshCurrentPage);
            }
            catch (Exception ex)
            {
                task.MarkFailed(ex.Message);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => _notificationService.Show($"资产派生数据刷新失败：{ex.Message}", NotificationLevel.Error, TimeSpan.FromSeconds(6)));
            }
        }

        public void Navigate(WorkspaceMode mode)
        {
            _selection.Clear();
            CurrentMode = mode;
            switch (mode)
            {
                case WorkspaceMode.ProfileLibrarySplit:
                    OpenPage(WorkspacePageType.Profile, leftSlot: true);
                    OpenPage(WorkspacePageType.Library, leftSlot: false);
                    break;
                case WorkspaceMode.ProfileOnly:
                    OpenSinglePage(WorkspacePageType.Profile);
                    break;
                case WorkspaceMode.LibraryOnly:
                    OpenSinglePage(WorkspacePageType.Library);
                    break;
                case WorkspaceMode.Status:
                    OpenSinglePage(WorkspacePageType.Status);
                    break;
                case WorkspaceMode.Settings:
                    OpenSinglePage(WorkspacePageType.Settings);
                    break;
                case WorkspaceMode.TagEdit:
                    OpenSinglePage(WorkspacePageType.TagEdit);
                    break;
                case WorkspaceMode.Home:
                default:
                    OpenSinglePage(WorkspacePageType.Home);
                    break;
            }
        }

        public void OpenSinglePage(WorkspacePageType pageType)
        {
            LeftPageType = pageType;
            RightPageType = pageType;
            LeftPage = CreatePage(pageType);
            RightPage = null;
            UpdateModeFromSlots();
            RaiseSlotFlags();
        }

        public void OpenPage(WorkspacePageType pageType, bool leftSlot)
        {
            if (leftSlot)
            {
                LeftPageType = pageType;
                LeftPage = CreatePage(pageType);
            }
            else
            {
                RightPageType = pageType;
                RightPage = CreatePage(pageType);
            }

            UpdateModeFromSlots();
            RaiseSlotFlags();
        }

        public void OpenModDetails(string modId, bool preferRightSlot = true)
        {
            if (string.IsNullOrWhiteSpace(modId)) return;
            SelectedModId = modId;
            OpenSecondaryPage(WorkspacePageType.ModDetails, preferRightSlot ? LeftPage : RightPage);
        }

        public void OpenModDetailsFromPage(PageViewModel? sourcePage, string modId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return;
            SelectedModId = modId;
            OpenSecondaryPage(WorkspacePageType.ModDetails, sourcePage);
        }

        private void OpenSecondaryPage(WorkspacePageType pageType, PageViewModel? sourcePage)
        {
            var targetPage = CreatePage(pageType);
            if (targetPage.RequiresSingleSlot)
            {
                OpenSinglePage(pageType);
                return;
            }

            if (!IsSplitView)
            {
                RightPageType = pageType;
                RightPage = targetPage;
            }
            else if (ReferenceEquals(sourcePage, RightPage))
            {
                LeftPageType = pageType;
                LeftPage = targetPage;
            }
            else
            {
                RightPageType = pageType;
                RightPage = targetPage;
            }

            UpdateModeFromSlots();
            RaiseSlotFlags();
        }

        public void OpenTagEdit(System.Collections.Generic.IEnumerable<string> guids)
        {
            _pendingTagEdit = guids.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
            var firstGuid = _pendingTagEdit.FirstOrDefault();
            var first = string.IsNullOrWhiteSpace(firstGuid) ? null : _libraryService.Get(firstGuid);
            if (first == null) return;
            CurrentMode = WorkspaceMode.TagEdit;
            LeftPageType = WorkspacePageType.TagEdit;
            RightPageType = WorkspacePageType.TagEdit;
            LeftPage = new TagEditPageViewModel(_libraryService, first, _pendingTagEdit.Skip(1), returnKey: "library");
            RightPage = null;
            RaiseSlotFlags();
        }

        public async System.Threading.Tasks.Task ProcessImportQueueAsync(string[] paths)
        {
            _importQueue.Enqueue(paths);
            ShowQueueSummary();

            await _importProcessGate.WaitAsync();
            try
            {
                ImportTaskItem? item;
                while ((item = _importQueue.DequeueNextQueued()) != null)
                {
                    var task = _backgroundTasks.Enqueue(BackgroundTaskKind.Import, $"导入 {System.IO.Path.GetFileName(item.Path)}", item.Path);
                    var import = new ImportService(_libraryService, onInfo: null, onError: err => _importQueue.MarkFailed(item, err));
                    try
                    {
                        task.MarkRunning("正在解压缩");
                        var created = await import.ImportPathAsync(item.Path, task.CancellationToken);
                        task.UpdateStage("正在保存模组库");
                        LogService.Info($"Import created {created.Count} mods from {item.Path}");
                        _libraryService.Save();
                        _importQueue.MarkDone(item);
                        task.MarkCompleted();
                        foreach (var guid in created) _tagQueue.Add(guid);
                        RefreshCurrentPage();
                        _notificationService.Show(string.Format(HD2ModManager.Resources.Strings.Notification_ImportComplete, item.Path));

                        if (created.Count > 0 && SettingsService.GetAutoOpenTagEdit())
                        {
                            OpenTagEdit(created);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _importQueue.MarkFailed(item, "任务已取消");
                        task.MarkCanceled();
                        _notificationService.Show($"导入已取消：{item.Path}", NotificationLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        _importQueue.MarkFailed(item, ex.Message);
                        task.MarkFailed(ex.Message);
                        System.Windows.MessageBox.Show($"导入失败: {item.Path}\n{ex.Message}", "Import", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        _notificationService.Show(string.Format(HD2ModManager.Resources.Strings.Notification_ImportFailed, item.Path), NotificationLevel.Error);
                    }
                }
            }
            finally
            {
                _importProcessGate.Release();
            }
        }

        private void InitializeTagCatalog(string baseDir, string configDir)
        {
            _ = new TagCsvSuggestionService(baseDir);
            _tagCatalog.Load(configDir);
            if (_tagCatalog.GetAll().Count == 0)
            {
                _tagCatalog.RebuildFromCsv(baseDir);
                var ok = _tagCatalog.Save();
                _tagCatalog.Load(configDir);
                _notificationService.Show(ok ? $"Tags rebuilt: {_tagCatalog.GetAll().Count}" : "Failed to write tags.json", ok ? NotificationLevel.Info : NotificationLevel.Error);
            }
            else
            {
                var tagsPath = System.IO.Path.Combine(configDir, "tags.json");
                if (!System.IO.File.Exists(tagsPath))
                {
                    var ok = _tagCatalog.Save();
                    _notificationService.Show(ok ? $"Tags saved: {_tagCatalog.GetAll().Count}" : "Failed to write tags.json", ok ? NotificationLevel.Info : NotificationLevel.Error);
                }
            }
        }

        private void RunStartupChecks(string configDir)
        {
            try
            {
                if (SettingsService.GetAutoCleanup())
                {
                    new IntegrityService(_libraryService, _notificationService, configDir).CheckAndFix();
                }

                if (string.IsNullOrWhiteSpace(SettingsService.GetGameDataFolder()))
                {
                    var detected = SettingsService.TryDetectAndSetGameDataFolder();
                    if (!string.IsNullOrWhiteSpace(detected))
                    {
                        _notificationService.Show($"已检测到游戏数据目录: {detected}", NotificationLevel.Info, TimeSpan.FromSeconds(4));
                    }
                }

                if (SettingsService.GetAutoUpdateAssetMetadata())
                {
                    _ = UpdateAssetMetadataOnStartupAsync();
                }
            }
            catch { }
        }

        private async Task UpdateAssetMetadataOnStartupAsync()
        {
            var task = _backgroundTasks.Enqueue(BackgroundTaskKind.UpdateAssetMetadata, "更新资产元数据", "启动检查");
            try
            {
                task.MarkRunning("正在同步资产元数据");
                var paths = new StoragePaths(AppDomain.CurrentDomain.BaseDirectory);
                var sync = CoreServices.CreateAssetMetadataSyncService(paths);
                var result = await sync.SyncAsync(SettingsService.GetAssetMetadataRepository()).ConfigureAwait(false);
                if (result.Success)
                {
                    task.MarkCompleted();
                    System.Windows.Application.Current.Dispatcher.Invoke(() => _notificationService.Show("资产信息已自动更新", NotificationLevel.Info, TimeSpan.FromSeconds(4)));
                }
                else
                {
                    task.MarkFailed(result.ErrorMessage ?? "未知错误");
                    System.Windows.Application.Current.Dispatcher.Invoke(() => _notificationService.Show($"资产信息自动更新失败：{result.ErrorMessage}", NotificationLevel.Warning, TimeSpan.FromSeconds(6)));
                }
            }
            catch (Exception ex)
            {
                task.MarkFailed(ex.Message);
                System.Windows.Application.Current.Dispatcher.Invoke(() => _notificationService.Show($"资产信息自动更新失败：{ex.Message}", NotificationLevel.Warning, TimeSpan.FromSeconds(6)));
            }
        }

        private void ApplyActiveProfile()
        {
            try
            {
                var activation = new ActivationService(_libraryService, _profileService, _notificationService);
                var result = activation.ApplyActiveProfileDetailed();
                _applyStatus.Record(result);
                RefreshCurrentPage();
                if (!result.Success && result.CoreResult != null)
                {
                    var details = string.Join(Environment.NewLine, result.CoreResult.Issues.Take(8).Select(i => $"[{i.Severity}] {i.Code}: {i.Message}"));
                    if (!string.IsNullOrWhiteSpace(details))
                    {
                        System.Windows.MessageBox.Show(details, "Apply Issues", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                _notificationService.Show($"应用配置失败：{ex.Message}", NotificationLevel.Error, TimeSpan.FromSeconds(6));
                System.Windows.MessageBox.Show(ex.Message, "Apply", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async void BrowseAndImport()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择要导入的 Mod 压缩包",
                Filter = "Mod Archives|*.zip;*.rar;*.7z|All Files|*.*",
                Multiselect = true,
            };
            if (dialog.ShowDialog() == true)
            {
                await ProcessImportQueueAsync(dialog.FileNames);
            }
        }

        public void RefreshCurrentPage()
        {
            switch (CurrentPage)
            {
                case HomePageViewModel home:
                    home.Refresh();
                    break;
                case StatusPageViewModel status:
                    status.Refresh();
                    break;
                case ProfilePageViewModel profile:
                    profile.Refresh();
                    break;
                case LibraryPageViewModel library:
                    library.Refresh();
                    break;
            }

            if (IsSplitView && RightPage != CurrentPage)
            {
                RefreshPage(RightPage);
            }
        }

        private void RefreshPage(PageViewModel? page)
        {
            switch (page)
            {
                case HomePageViewModel home:
                    home.Refresh();
                    break;
                case StatusPageViewModel status:
                    status.Refresh();
                    break;
                case ProfilePageViewModel profile:
                    profile.Refresh();
                    break;
                case LibraryPageViewModel library:
                    library.Refresh();
                    break;
                case ModDetailsPageViewModel details:
                    details.Refresh();
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

        private void ExecuteSelectionPrimary(object? _)
        {
            if (!_selection.HasSelection) return;
            if (string.Equals(_selection.Scope, "Library", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var guid in _selection.SelectedIds.ToList()) _profileService.AddModToActive(guid);
                _notificationService.Show($"已加入当前配置：{_selection.SelectedIds.Count} 个 Mod");
                _selection.Clear();
                RefreshCurrentPage();
            }
            else if (string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase))
            {
                var count = _profileService.SetModsEnabledInActive(_selection.SelectedIds.ToList(), enabled: true);
                _notificationService.Show($"已启用：{count} 个 Mod");
                _selection.Clear();
                RefreshCurrentPage();
            }
        }

        private void ExecuteSelectionSecondary(object? _)
        {
            if (!_selection.HasSelection) return;
            if (string.Equals(_selection.Scope, "Library", StringComparison.OrdinalIgnoreCase))
            {
                OpenTagEdit(_selection.SelectedIds);
            }
            else if (string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase))
            {
                var count = _profileService.SetModsEnabledInActive(_selection.SelectedIds.ToList(), enabled: false);
                _notificationService.Show($"已禁用：{count} 个 Mod");
                _selection.Clear();
                RefreshCurrentPage();
            }
        }

        private void ExecuteSelectionDelete(object? _)
        {
            if (!_selection.HasSelection) return;
            var ids = _selection.SelectedIds.ToList();
            if (string.Equals(_selection.Scope, "Library", StringComparison.OrdinalIgnoreCase))
            {
                var confirm = System.Windows.MessageBox.Show($"确定删除选中的 {ids.Count} 个 Mod？\n这会同时删除库中的已存储文件。", "批量删除 Mod", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (confirm != System.Windows.MessageBoxResult.Yes) return;
                foreach (var guid in ids) _libraryService.Remove(guid);
                _libraryService.Save();
                _ = RefreshLibraryDerivedDataAfterStartupAsync();
                _notificationService.Show($"已删除：{ids.Count} 个 Mod");
            }
            else if (string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var guid in ids) _profileService.RemoveModFromActive(guid);
                _notificationService.Show($"已从配置移除：{ids.Count} 个 Mod");
            }

            _selection.Clear();
            RefreshCurrentPage();
        }

        private void RaiseSelectionFlags()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(SelectionPrimaryText));
            OnPropertyChanged(nameof(SelectionSecondaryText));
            OnPropertyChanged(nameof(SelectionDeleteText));
        }

        private PageViewModel CreatePage(WorkspacePageType pageType)
        {
            return pageType switch
            {
                WorkspacePageType.Home => new HomePageViewModel(_profileService, _libraryService, _importQueue, _applyStatus),
                WorkspacePageType.Status => new StatusPageViewModel(_profileService, _libraryService, _importQueue, _applyStatus, _backgroundTasks),
                WorkspacePageType.Profile => new ProfilePageViewModel(_profileService, _libraryService, _selection),
                WorkspacePageType.Library => CreateLibraryPage(),
                WorkspacePageType.Settings => new SettingsPageViewModel(_profileService, _libraryService),
                WorkspacePageType.ModDetails => new ModDetailsPageViewModel(_libraryService, _profileService, SelectedModId ?? string.Empty, _notificationService),
                WorkspacePageType.TagEdit => LeftPage ?? new HomePageViewModel(_profileService, _libraryService, _importQueue, _applyStatus),
                _ => new HomePageViewModel(_profileService, _libraryService, _importQueue, _applyStatus),
            };
        }

        private LibraryPageViewModel CreateLibraryPage()
        {
            var page = new LibraryPageViewModel(_libraryService, _selection, _profileService, _notificationService);
            RegisterLibraryActions(page);
            return page;
        }

        private void RegisterLibraryActions(PageViewModel page)
        {
            page.PageActions.Add(new PageActionViewModel("⟳", "刷新当前页", RefreshCommand, background: new SolidColorBrush(Color.FromRgb(94, 100, 112)), order: 10, kind: "Refresh"));
            page.PageActions.Add(new PageActionViewModel("＋", "导入 Mod", ImportCommand, order: 20, kind: "Import"));
            page.PageActions.Add(new PageActionViewModel("✓", "应用当前配置", ApplyChangesCommand, background: new SolidColorBrush(Color.FromRgb(46, 125, 50)), order: 30, kind: "Apply"));
        }

        private void UpdateModeFromSlots()
        {
            CurrentMode = (LeftPageType, RightPageType, IsSplitView) switch
            {
                (WorkspacePageType.Home, WorkspacePageType.Home, false) => WorkspaceMode.Home,
                (WorkspacePageType.Status, WorkspacePageType.Status, false) => WorkspaceMode.Status,
                (WorkspacePageType.Profile, WorkspacePageType.Profile, false) => WorkspaceMode.ProfileOnly,
                (WorkspacePageType.Library, WorkspacePageType.Library, false) => WorkspaceMode.LibraryOnly,
                (WorkspacePageType.Profile, WorkspacePageType.Library, true) => WorkspaceMode.ProfileLibrarySplit,
                (WorkspacePageType.Settings, WorkspacePageType.Settings, false) => WorkspaceMode.Settings,
                (WorkspacePageType.TagEdit, WorkspacePageType.TagEdit, false) => WorkspaceMode.TagEdit,
                _ => CurrentMode,
            };
        }

        private static string GetPageTitle(WorkspacePageType pageType) => pageType switch
        {
            WorkspacePageType.Home => "首页",
            WorkspacePageType.Status => "状态",
            WorkspacePageType.Profile => "配置页",
            WorkspacePageType.Library => "模组库",
            WorkspacePageType.Settings => "设置",
            WorkspacePageType.TagEdit => "标签编辑",
            WorkspacePageType.ModDetails => "Mod 详情",
            _ => "页面",
        };

        private void RaiseSlotFlags()
        {
            OnPropertyChanged(nameof(IsSplitView));
            OnPropertyChanged(nameof(ShowRightSlot));
            OnPropertyChanged(nameof(IsLeftSlotLibrary));
            OnPropertyChanged(nameof(IsRightSlotLibrary));
            OnPropertyChanged(nameof(IsFullScreenLibrary));
            OnPropertyChanged(nameof(IsSplitLeftSlotLibrary));
            OnPropertyChanged(nameof(LeftSlotTitle));
            OnPropertyChanged(nameof(RightSlotTitle));
            RaiseActionFlags();
        }

        private void RaiseActionFlags()
        {
            OnPropertyChanged(nameof(LeftSlotActions));
            OnPropertyChanged(nameof(RightSlotActions));
            OnPropertyChanged(nameof(HasLeftSlotActions));
            OnPropertyChanged(nameof(HasRightSlotActions));
            OnPropertyChanged(nameof(ShowSplitLeftSlotActions));
            OnPropertyChanged(nameof(ShowRightSlotActions));
            OnPropertyChanged(nameof(ShowFullScreenActions));
        }

        private void RaiseModeFlags()
        {
            OnPropertyChanged(nameof(IsHomeActive));
            OnPropertyChanged(nameof(IsStatusActive));
            OnPropertyChanged(nameof(IsProfileActive));
            OnPropertyChanged(nameof(IsLibraryActive));
            OnPropertyChanged(nameof(IsSplitActive));
            OnPropertyChanged(nameof(IsSettingsActive));
        }
    }
}

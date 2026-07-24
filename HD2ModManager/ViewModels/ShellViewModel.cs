using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using HD2ModCore.Application;
using HD2ModCore.Domain;
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
        private readonly DerivedStateCoordinator _derivedState;
        private readonly NotificationService _notificationService;
        private readonly MessageCenterService _messageCenter;
        private readonly IProfileDeploymentCoordinator _deploymentCoordinator;
        private BackgroundTaskItem? _deploymentTask;
        private readonly SelectionCoordinator _selection = new();
        private readonly BottomBarCoordinator _bottomBar;
        private readonly SemaphoreSlim _importProcessGate = new(1, 1);
        private int _pageRefreshQueued;

        private PageViewModel? _leftPage;
        private PageViewModel? _rightPage;
        private WorkspaceMode _currentMode;
        private WorkspacePageType _leftPageType;
        private WorkspacePageType _rightPageType;
        private string? _selectedModId;
        private bool _isMessagePanelOpen;
        private bool _isMessagePreviewOpen;
        private System.Threading.CancellationTokenSource? _messagePreviewCancellation;
        private int _bottomBarStateVersion;

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
        public bool IsMessagePanelOpen { get => _isMessagePanelOpen; private set => SetField(ref _isMessagePanelOpen, value); }
        public ReadOnlyObservableCollection<MessageCenterItem> MessageItems => _messageCenter.Items;
        public MessageCenterItem? LatestMessageItem => _messageCenter.Items.LastOrDefault();
        public int ActiveTaskCount => _backgroundTasks.CountQueued + _backgroundTasks.CountRunning;
        public bool HasUnreadTaskHubEvents => false;
        public bool IsMessagePreviewOpen { get => _isMessagePreviewOpen; private set => SetField(ref _isMessagePreviewOpen, value); }

        public RelayCommand ShowHomeCommand { get; }
        public RelayCommand ShowProfileCommand { get; }
        public RelayCommand ShowLibraryCommand { get; }
        public RelayCommand LaunchGameCommand { get; }
        public RelayCommand ShowSplitCommand { get; }
        public RelayCommand ShowSettingsCommand { get; }
        public RelayCommand ApplyChangesCommand { get; }
        public RelayCommand ImportCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand CancelSelectionCommand { get; }
        public RelayCommand SelectionPrimaryCommand { get; }
        public RelayCommand SelectionDeleteCommand { get; }
        public RelayCommand ToggleMessagePanelCommand { get; }
        public RelayCommand CancelTaskCommand { get; }
        public RelayCommand RetryTaskCommand { get; }
        public RelayCommand CopyMessageCommand { get; }
        public RelayCommand CopySelectedMessagesCommand { get; }

        public bool IsHomeActive => CurrentMode == WorkspaceMode.Home;
        public bool ShowHomeTitle => IsHomeActive;
        public bool ShowLaunchGameButton => !IsHomeActive;
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
        public BottomBarCoordinator BottomBar => _bottomBar;
        public int BottomBarStateVersion => _bottomBarStateVersion;
        public bool HasSelection => _selection.HasSelection;
        public string SelectionSummary => _selection.Summary;
        public string SelectionPrimaryText => string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase) ? "移除" : "加入配置";
        public string SelectionDeleteText => string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase) ? "移除" : "删除";

        public ShellViewModel()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configDir = System.IO.Path.Combine(baseDir, "config");
            System.IO.Directory.CreateDirectory(configDir);

            if (string.IsNullOrWhiteSpace(SettingsService.GetGameDataFolder())) SettingsService.TryDetectAndSetGameDataFolder();
            SettingsService.EnsureDefaultModLibraryFolder();

            _profileService = new ProfileService(configDir);
            _profileService.Load();

            var informationCenter = CoreServices.CreateModInformationCenter(SettingsService.CreateStoragePaths());
            _libraryService = new ModLibraryService(System.IO.Path.Combine(configDir, "library.json"), informationCenter);
            // 启动必须先展示 UI；稳定 facts 的投影在后台恢复，任何异常都不能阻止管理器启动。
            _libraryService.Load(buildDerivedData: false);
            _profileService.ReloadFromLibrary();
            _derivedState = new DerivedStateCoordinator(_libraryService, _profileService, informationCenter);
            _importQueue = new ImportQueueService();
            _backgroundTasks = new BackgroundTaskService();
            _applyStatus = new ApplyStatusService();
            _notificationService = new NotificationService();
            _messageCenter = new MessageCenterService(_notificationService, _backgroundTasks);
            _bottomBar = new BottomBarCoordinator(_selection, _libraryService, _notificationService, RefreshCurrentPage);
            _bottomBar.PropertyChanged += (_, _) =>
            {
                _bottomBarStateVersion++;
                OnPropertyChanged(nameof(BottomBarStateVersion));
            };
            _deploymentCoordinator = CoreServices.CreateProfileDeploymentCoordinator(
                SettingsService.CreateStoragePaths(),
                SettingsService.GetGameDataFolder);
            _deploymentCoordinator.StatusChanged += OnDeploymentStatusChanged;
            _profileService.ActiveProfileDeploymentRequired += (_, _) => _deploymentCoordinator.NotifyActiveProfileChanged();
            _profileService.ActiveProfileDeactivationRequired += (_, _) => _ = _deploymentCoordinator.DeactivateAsync();
            _libraryService.ModContentFactsChanged += (_, change) =>
            {
                var active = _profileService.ActiveProfile;
                var affectsActiveProfile = active is not null && change.NodeIds.Any(nodeId => active.Entries.Any(entry => entry.NodeId == nodeId));
                LogService.Info($"部署触发检查：库内容{change.Kind}，节点数={change.NodeIds.Count}，影响活动配置={affectsActiveProfile}。");
                if (affectsActiveProfile)
                {
                    _profileService.NotifyActiveModContentChanged();
                }
            };
            _libraryService.SnapshotChanged += (_, _) => RefreshOnUiThread(HandleLibrarySnapshotChanged);
            _profileService.Changed += (_, _) => QueueCurrentPageRefresh("配置变更");
            _derivedState.SnapshotChanged += (_, _) => QueueCurrentPageRefresh("派生状态变更");
            _backgroundTasks.Changed += (_, _) => RefreshOnUiThread(() =>
            {
                RefreshTaskHubState();
                ShowMessagePreview();
            });
            _notificationService.Changed += (_, _) => RefreshOnUiThread(RefreshTaskHubState);
            _messageCenter.Changed += (_, _) => RefreshOnUiThread(() =>
            {
                OnPropertyChanged(nameof(LatestMessageItem));
                ShowMessagePreview();
            });
			_ = Task.Run(() => new ImportTemporaryDirectoryManager(SettingsService.CreateStoragePaths()).CleanupStaleDirectories());

            RunStartupChecks(configDir);

            ShowHomeCommand = new RelayCommand(() => Navigate(WorkspaceMode.Home));
            ShowProfileCommand = new RelayCommand(() => Navigate(WorkspaceMode.ProfileOnly));
            ShowLibraryCommand = new RelayCommand(() => Navigate(WorkspaceMode.LibraryOnly));
            LaunchGameCommand = new RelayCommand(LaunchGame);
            ShowSplitCommand = new RelayCommand(() => Navigate(WorkspaceMode.ProfileLibrarySplit));
            ShowSettingsCommand = new RelayCommand(() => Navigate(WorkspaceMode.Settings));
            ApplyChangesCommand = new RelayCommand(QueueActiveProfileDeployment);
            ImportCommand = new RelayCommand(BrowseAndImport);
            RefreshCommand = new RelayCommand(RefreshCurrentPage);
            CancelSelectionCommand = new RelayCommand(_selection.Clear);
            SelectionPrimaryCommand = new RelayCommand(async _ => await ExecuteSelectionPrimaryAsync());
            SelectionDeleteCommand = new RelayCommand(async _ => await ExecuteSelectionDeleteAsync());
            ToggleMessagePanelCommand = new RelayCommand(ToggleMessagePanel);
            CancelTaskCommand = new RelayCommand(CancelTask, task => task is BackgroundTaskItem { CanCancel: true });
            RetryTaskCommand = new RelayCommand(async task => await RetryTaskAsync(task), task => task is BackgroundTaskItem { CanRetry: true });
            CopyMessageCommand = new RelayCommand(CopyMessage, item => item is MessageCenterItem);
            CopySelectedMessagesCommand = new RelayCommand(CopySelectedMessages, items => items is System.Collections.IEnumerable);
            _selection.SelectionChanged += (_, _) => RaiseSelectionFlags();

            Navigate(WorkspaceMode.Home);
            _ = RestoreStableLibraryProjectionAsync();
            _ = _derivedState.RefreshAsync();
            _ = CheckGameDataIndexOnStartupAsync();
        }

        private async Task RestoreStableLibraryProjectionAsync()
        {
            try
            {
                await _libraryService.RefreshDerivedDataAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogService.Error($"Stable library projection restore failed: {exception}");
                System.Windows.Application.Current?.Dispatcher.Invoke(() => _notificationService.Show("已启动，但稳定资产事实恢复失败；可在库页刷新后重试。", NotificationLevel.Warning, TimeSpan.FromSeconds(8)));
            }
        }

        private async Task CheckGameDataIndexOnStartupAsync()
        {
            if (!SettingsService.GetAutoCheckGameDataIndex() || !IsCheckDue(
                    SettingsService.GetLastGameDataIndexCheckUtc(),
                    SettingsService.GetGameDataIndexCheckIntervalHours())) return;

            var gameData = SettingsService.GetGameDataFolder();
            var paths = SettingsService.CreateStoragePaths();
            if (string.IsNullOrWhiteSpace(gameData) || !System.IO.Directory.Exists(gameData) || !System.IO.File.Exists(paths.ArchiveHashesPath)) return;

            var task = _backgroundTasks.Enqueue(
                BackgroundTaskKind.BuildAssetIndex,
                "检查游戏资产索引",
                gameData,
                origin: "启动维护",
                userVisibleReason: "按设置的周期比较 Game Data 与现有资产索引。",
                suggestedAction: "若检测到过期，请在“设置与资产”中明确启动索引重建。");
            try
            {
                task.MarkRunning("正在检查索引指纹");
                var archiveHashes = await System.IO.File.ReadAllTextAsync(paths.ArchiveHashesPath).ConfigureAwait(false);
                var index = CoreServices.CreateAssetArchiveIndexService(paths);
                var status = await index.GetIndexStatusAsync(gameData, archiveHashes, task.CancellationToken).ConfigureAwait(false);
                SettingsService.SetLastGameDataIndexCheckUtc(DateTime.UtcNow);
                if (status.IsCurrent)
                {
                    task.MarkCompleted();
                    return;
                }

                task.UpdateStage("索引缺失或已过期");
                task.SetSuggestedAction("请在“设置与资产”中明确启动“建立 / 重建资产索引”。");
                task.MarkCompleted();
            }
            catch (OperationCanceledException)
            {
                task.MarkCanceled();
            }
            catch (Exception exception)
            {
                task.MarkFailed(exception.Message);
                LogService.Error($"Startup Game Data index check failed: {exception}");
            }
        }

        public void Navigate(WorkspaceMode mode)
        {
            ClearTransientSelection();
            var (left, right) = mode switch
            {
                WorkspaceMode.ProfileLibrarySplit => (WorkspacePageType.Profile, (WorkspacePageType?)WorkspacePageType.Library),
                WorkspaceMode.ProfileOnly => (WorkspacePageType.Profile, null),
                WorkspaceMode.LibraryOnly => (WorkspacePageType.Library, null),
                WorkspaceMode.Settings => (WorkspacePageType.Settings, null),
                _ => (WorkspacePageType.Home, null),
            };
            CommitNavigation(left, right, mode);
        }

        private void CommitNavigation(WorkspacePageType leftType, WorkspacePageType? rightType, WorkspaceMode mode)
        {
            var oldLeft = _leftPage;
            var oldRight = _rightPage;
            _leftPageType = leftType;
            _rightPageType = rightType ?? leftType;
            var nextLeft = CreatePage(leftType);
            var nextRight = rightType is { } type ? CreatePage(type) : null;
            _leftPage = nextLeft;
            _rightPage = nextRight;
            _currentMode = mode;

            if (!ReferenceEquals(oldLeft, oldRight)) (oldLeft as IDisposable)?.Dispose();
            if (!ReferenceEquals(oldRight, nextLeft)) (oldRight as IDisposable)?.Dispose();

            OnPropertyChanged(nameof(LeftPageType));
            OnPropertyChanged(nameof(RightPageType));
            OnPropertyChanged(nameof(LeftPage));
            OnPropertyChanged(nameof(RightPage));
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(CurrentMode));
            RaiseModeFlags();
            RaiseSlotFlags();
        }

        public void OpenMessagePanel()
        {
            IsMessagePanelOpen = true;
            IsMessagePreviewOpen = false;
            _messagePreviewCancellation?.Cancel();
            _notificationService.MarkAllRead();
            RefreshTaskHubState();
        }

        public void CloseMessagePanel()
        {
            if (!IsMessagePanelOpen) return;
            IsMessagePanelOpen = false;
            if (ActiveTaskCount > 0) ShowMessagePreview();
        }

        private async void ShowMessagePreview()
        {
            if (IsMessagePanelOpen) return;
            IsMessagePreviewOpen = true;
            _messagePreviewCancellation?.Cancel();
            _messagePreviewCancellation?.Dispose();
            _messagePreviewCancellation = new System.Threading.CancellationTokenSource();
            var token = _messagePreviewCancellation.Token;
            try
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5), token);
                if (!token.IsCancellationRequested && ActiveTaskCount == 0) IsMessagePreviewOpen = false;
                else if (!token.IsCancellationRequested) ShowMessagePreview();
            }
            catch (OperationCanceledException) { }
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
            ClearTransientSelection();
            SelectedModId = modId;
            OpenSecondaryPage(WorkspacePageType.ModDetails, preferRightSlot ? LeftPage : RightPage);
        }

        public void BeginBottomBarNameEdit(string modId, string currentValue) => _bottomBar.BeginNameEdit(modId, currentValue);
        public void BeginBottomBarDescriptionEdit(string modId, string currentValue) => _bottomBar.BeginDescriptionEdit(modId, currentValue);
        public void CancelBottomBarEdit() => _bottomBar.CancelEdit();
        public void ClearTransientSelection()
        {
            _bottomBar.CancelEdit();
            _selection.Clear();
        }

        public void OpenModDetailsFromPage(PageViewModel? sourcePage, string modId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return;
            ClearTransientSelection();
            SelectedModId = modId;
            OpenSecondaryPage(WorkspacePageType.ModDetails, sourcePage);
        }

        public void OpenAdvancedModDetails(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return;
            ClearTransientSelection();
            SelectedModId = modId;
            OpenSinglePage(WorkspacePageType.AdvancedModDetails);
        }

        public void OpenGameDataBrowser()
        {
            LeftPageType = WorkspacePageType.GameDataBrowser;
            RightPageType = WorkspacePageType.GameDataArchiveDetails;
            LeftPage = new GameDataBrowserPageViewModel(_libraryService, _profileService);
            RightPage = new GameDataArchiveDetailsHostPageViewModel((GameDataBrowserPageViewModel)LeftPage);
            UpdateModeFromSlots();
            RaiseSlotFlags();
        }

        public void OpenCrossArmorPlan(HD2ModManager.Views.CrossArmorTransferPlanWindowViewModel plan)
        {
            LeftPageType = WorkspacePageType.CrossArmorPlan;
            RightPageType = WorkspacePageType.CrossArmorPlan;
            LeftPage = plan;
            plan.AttachCandidateOutput(new CrossArmorCandidateOutputPageViewModel(plan, _notificationService));
            RightPage = null;
            UpdateModeFromSlots();
            RaiseSlotFlags();
        }

        public void OpenMaterialPackaging(ModDetailsPageViewModel sourcePage, MaterialPackagingPageViewModel packagingPage)
        {
            if (ReferenceEquals(sourcePage, RightPage))
            {
                LeftPageType = WorkspacePageType.MaterialPackaging;
                LeftPage = packagingPage;
            }
            else
            {
                RightPageType = WorkspacePageType.MaterialPackaging;
                RightPage = packagingPage;
            }
            UpdateModeFromSlots();
            RaiseSlotFlags();
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

        public async System.Threading.Tasks.Task ProcessImportQueueAsync(string[] paths)
        {
            _importQueue.Enqueue(paths);
            ShowQueueSummary();

            await _importProcessGate.WaitAsync();
            try
            {
                var libraryChanged = false;
                var importedModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var importedSources = new List<string>();
                ImportTaskItem? item;
                while ((item = _importQueue.DequeueNextQueued()) != null)
                {
                    var task = _backgroundTasks.Enqueue(BackgroundTaskKind.Import, $"导入 {System.IO.Path.GetFileName(item.Path)}", item.Path);
                    var import = new ImportService(_libraryService, onInfo: null, onError: err => _importQueue.MarkFailed(item, err), informationCenter: _derivedState.InformationCenter);
                    try
                    {
                        task.MarkRunning("正在解压缩");
                        var created = await import.ImportPathAsync(item.Path, task.CancellationToken, notifyLibraryChanged: false);
                        libraryChanged = true;
                        task.UpdateStage("模组库已更新");
                        LogService.Info($"快速导入完成：来源={item.Path}，新增 Mod 数={created.Count}。导入仅写入模组库；除非后续显式加入活动配置，否则不会部署。");
                        _importQueue.MarkDone(item);
                        task.MarkCompleted();
                        _notificationService.Show(string.Format(HD2ModManager.Resources.Strings.Notification_ImportComplete, item.Path));
                        if (created.Count > 0)
                        {
                            importedModIds.UnionWith(created);
                            importedSources.Add(item.Path);
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

                if (libraryChanged)
                {
                    _libraryService.NotifyImportCompleted();
                }
                if (importedModIds.Count > 0)
                {
                    StartImportedModAnalysis(importedModIds, importedSources);
                }
            }
            finally
            {
                _importProcessGate.Release();
            }
        }

        private async void StartImportedModAnalysis(IReadOnlyCollection<string> created, IReadOnlyCollection<string> sourcePaths)
        {
            var sourceDescription = sourcePaths.Count == 1 ? sourcePaths.First() : $"{sourcePaths.Count} 个导入来源";
            var task = _backgroundTasks.Enqueue(BackgroundTaskKind.AnalyzeImportedMod, $"分析 {created.Count} 个导入 Mod", sourceDescription);
            try
            {
                task.MarkRunning("正在建立内容索引");
                var import = new ImportService(_libraryService, informationCenter: _derivedState.InformationCenter);
                await import.AnalyzeImportedAsync(created, task.CancellationToken);
                task.MarkCompleted();
                LogService.Info($"导入内容分析完成：来源数={sourcePaths.Count}，Mod 数={created.Count}。已合并为一次库内容变更通知。");
				QueueCurrentPageRefresh("导入内容分析完成");
            }
            catch (OperationCanceledException) { task.MarkCanceled(); }
            catch (Exception ex)
            {
                task.MarkFailed(ex.Message);
                LogService.Error($"导入内容分析失败：来源数={sourcePaths.Count}，错误={ex}");
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
            if (!IsCheckDue(
                    SettingsService.GetLastAssetMetadataCheckUtc(),
                    SettingsService.GetAssetMetadataCheckIntervalHours())) return;

            var task = _backgroundTasks.Enqueue(
                BackgroundTaskKind.UpdateAssetMetadata,
                "更新资产元数据",
                "启动检查",
                origin: "启动维护",
                userVisibleReason: "已达到在线资产自动检查周期。",
                suggestedAction: "失败时请检查在线资产索引源与网络连接。");
            try
            {
                task.MarkRunning("正在同步资产元数据");
                var paths = SettingsService.CreateStoragePaths();
                var sync = CoreServices.CreateAssetMetadataSyncService(paths);
                var result = await sync.SyncAsync(SettingsService.GetAssetMetadataRepository()).ConfigureAwait(false);
                if (result.Success)
                {
                    SettingsService.SetLastAssetMetadataCheckUtc(DateTime.UtcNow);
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

        private static bool IsCheckDue(DateTime? lastCheckUtc, int intervalHours)
            => lastCheckUtc is null || DateTime.UtcNow - lastCheckUtc.Value >= TimeSpan.FromHours(intervalHours);

        private void QueueActiveProfileDeployment()
        {
            if (_profileService.ActiveProfile is null)
            {
                _notificationService.Show("当前没有活动配置。", NotificationLevel.Info, TimeSpan.FromSeconds(4));
                return;
            }
            _deploymentCoordinator.NotifyActiveProfileChanged();
            _notificationService.Show("已请求立即部署最新活动配置。", NotificationLevel.Info, TimeSpan.FromSeconds(4));
        }

        private void OnDeploymentStatusChanged(object? sender, ProfileDeploymentStatus status)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                _ = dispatcher.InvokeAsync(() => OnDeploymentStatusChanged(sender, status));
                return;
            }

            switch (status.Stage)
            {
                case ProfileDeploymentStage.Deploying:
                    if (_deploymentTask?.IsActive != true)
                        _deploymentTask = _backgroundTasks.Enqueue(BackgroundTaskKind.Deployment, "部署活动配置", status.Message);
                    _deploymentTask.MarkRunning(status.Message ?? "正在部署");
                    break;
                case ProfileDeploymentStage.Deactivating:
                    if (_deploymentTask?.IsActive == true) _deploymentTask.Cancel();
                    _deploymentTask = _backgroundTasks.Enqueue(BackgroundTaskKind.DeactivateProfile, "停用活动配置", status.Message);
                    _deploymentTask.MarkRunning(status.Message ?? "正在清理 Patch");
                    break;
                case ProfileDeploymentStage.Completed:
                    _deploymentTask?.MarkCompleted();
                    if (status.ApplyResult is { } success) _applyStatus.Record(new ApplyExecutionStatus(success.Success, status.Message ?? "部署完成", success));
                    _derivedState.MarkDeploymentDirty();
                    RefreshCurrentPage();
                    break;
                case ProfileDeploymentStage.Failed:
                    _deploymentTask?.MarkFailed(status.Message ?? "部署失败");
                    if (status.ApplyResult is { } failure) _applyStatus.Record(new ApplyExecutionStatus(false, status.Message ?? "部署失败", failure));
                    var failureMessage = status.Message ?? "活动配置部署失败。";
                    if (status.ApplyResult is { Issues.Count: > 0 } failedResult)
                    {
                        var details = failedResult.Issues.Take(3).Select(issue => $"{issue.Code}: {issue.Message}");
                        failureMessage = $"{failureMessage}\n{string.Join("\n", details)}";
                    }
                    _notificationService.Show(failureMessage, NotificationLevel.Error, TimeSpan.FromSeconds(12));
                    RefreshCurrentPage();
                    break;
                case ProfileDeploymentStage.Canceled:
                    _deploymentTask?.MarkCanceled();
                    break;
            }
        }

        private void LaunchGame()
        {
            Process.Start(new ProcessStartInfo("steam://rungameid/553850")
            {
                UseShellExecute = true,
            });
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
            RefreshPage(LeftPage);
            if (IsSplitView) RefreshPage(RightPage);
        }

        private void QueueCurrentPageRefresh(string reason)
        {
            if (Interlocked.Exchange(ref _pageRefreshQueued, 1) != 0) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                Interlocked.Exchange(ref _pageRefreshQueued, 0);
                RefreshCurrentPage();
                return;
            }

            _ = dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(80);
                Interlocked.Exchange(ref _pageRefreshQueued, 0);
                var stopwatch = Stopwatch.StartNew();
                RefreshCurrentPage();
                LogService.Info($"UI 性能：合并页面刷新，原因={reason}，耗时 {stopwatch.ElapsedMilliseconds}ms。");
            });
        }

        private void RefreshPage(PageViewModel? page)
        {
            switch (page)
            {
                case HomePageViewModel home:
                    home.Refresh();
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

        private void HandleLibrarySnapshotChanged()
        {
            _profileService.ReloadFromLibrary();
            CloseDeletedModDetails();
            RefreshCurrentPage();
        }

        private void ToggleMessagePanel()
        {
            if (IsMessagePanelOpen) CloseMessagePanel(); else OpenMessagePanel();
        }

        private static void CopyMessage(object? value)
        {
            if (value is MessageCenterItem item && !string.IsNullOrWhiteSpace(item.CopyText)) System.Windows.Clipboard.SetText(item.CopyText);
        }

        private static void CopySelectedMessages(object? value)
        {
            if (value is not System.Collections.IEnumerable items) return;
            var text = string.Join(Environment.NewLine + Environment.NewLine, items.Cast<object>().OfType<MessageCenterItem>().Select(item => item.CopyText));
            if (!string.IsNullOrWhiteSpace(text)) System.Windows.Clipboard.SetText(text);
        }

        private static void CancelTask(object? parameter)
        {
            if (parameter is BackgroundTaskItem task) task.Cancel();
        }

        private static async Task RetryTaskAsync(object? parameter)
        {
            if (parameter is not BackgroundTaskItem { Retry: { } retry }) return;
            await retry();
        }

        private void RefreshTaskHubState()
        {
            OnPropertyChanged(nameof(MessageItems));
            OnPropertyChanged(nameof(ActiveTaskCount));
            OnPropertyChanged(nameof(HasUnreadTaskHubEvents));
            CancelTaskCommand.RaiseCanExecuteChanged();
            RetryTaskCommand.RaiseCanExecuteChanged();
        }

        private void CloseDeletedModDetails()
        {
            var selectedModWasDeleted = !string.IsNullOrWhiteSpace(SelectedModId)
                && _libraryService.Get(SelectedModId) is null;
            if (selectedModWasDeleted) SelectedModId = null;

            var leftDetailsWasDeleted = LeftPage is ModDetailsPageViewModel leftDetails
                && _libraryService.Get(leftDetails.ModId) is null;
            var rightDetailsWasDeleted = RightPage is ModDetailsPageViewModel rightDetails
                && _libraryService.Get(rightDetails.ModId) is null;
            if (!leftDetailsWasDeleted && !rightDetailsWasDeleted) return;

            if (rightDetailsWasDeleted)
            {
                RightPageType = LeftPageType;
                RightPage = null;
                UpdateModeFromSlots();
                RaiseSlotFlags();
                return;
            }

            if (RightPage is not null)
            {
                OpenSinglePage(RightPageType);
                return;
            }

            OpenSinglePage(WorkspacePageType.Library);
        }

        private async Task ExecuteSelectionPrimaryAsync()
        {
            if (!_selection.HasSelection) return;
            if (string.Equals(_selection.Scope, "Library", StringComparison.OrdinalIgnoreCase))
            {
                var ids = _selection.SelectedIds.ToList();
                var task = _backgroundTasks.Enqueue(BackgroundTaskKind.Other, "加入配置", $"{ids.Count} 个 Mod");
                task.MarkRunning("正在写入配置");
                try
                {
                    var added = 0;
                    foreach (var guid in ids)
                    {
                        task.CancellationToken.ThrowIfCancellationRequested();
                        if (await _profileService.AddModToSelectedAsync(guid, task.CancellationToken)) added++;
                    }
                    task.MarkCompleted();
                    _notificationService.Show($"已加入正在编辑的配置：{added} 个 Mod");
                }
                catch (OperationCanceledException)
                {
                    task.MarkCanceled();
                    return;
                }
                catch (Exception exception)
                {
                    task.MarkFailed(exception.Message);
                    return;
                }
                _selection.Clear();
                RefreshCurrentPage();
            }
            else if (string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase))
            {
                var ids = _selection.SelectedIds.ToList();
                var task = _backgroundTasks.Enqueue(BackgroundTaskKind.Other, "从配置移除", $"{ids.Count} 个 Mod");
                task.MarkRunning("正在写入配置");
                try
                {
                    var removed = 0;
                    foreach (var guid in ids)
                    {
                        task.CancellationToken.ThrowIfCancellationRequested();
                        if (await _profileService.RemoveModFromSelectedAsync(guid, task.CancellationToken)) removed++;
                    }
                    task.MarkCompleted();
                    _notificationService.Show($"已从配置移除：{removed} 个 Mod");
                }
                catch (OperationCanceledException)
                {
                    task.MarkCanceled();
                    return;
                }
                catch (Exception exception)
                {
                    task.MarkFailed(exception.Message);
                    return;
                }
                _selection.Clear();
                RefreshCurrentPage();
            }
        }

        public async Task AddModToSelectedProfileAsync(string modId, string modName)
        {
            var task = _backgroundTasks.Enqueue(BackgroundTaskKind.Other, "加入配置", modName);
            task.MarkRunning("正在写入配置");
            try
            {
                if (await _profileService.AddModToSelectedAsync(modId, task.CancellationToken))
                {
                    task.MarkCompleted();
                    _notificationService.Show($"已加入正在编辑的配置：{modName}");
                    RefreshCurrentPage();
                }
                else
                {
                    task.MarkFailed("可能尚未选择配置或该 Mod 已存在。");
                }
            }
            catch (OperationCanceledException)
            {
                task.MarkCanceled();
            }
            catch (Exception exception)
            {
                task.MarkFailed(exception.Message);
            }
        }

        public async Task RemoveModFromSelectedProfileAsync(string modId, string modName)
        {
            var task = _backgroundTasks.Enqueue(BackgroundTaskKind.Other, "从配置移除", modName);
            task.MarkRunning("正在写入配置");
            try
            {
                if (await _profileService.RemoveModFromSelectedAsync(modId, task.CancellationToken))
                {
                    task.MarkCompleted();
                    _notificationService.Show($"已从配置移除：{modName}");
                    RefreshCurrentPage();
                }
                else
                {
                    task.MarkFailed("可能尚未选择配置或该 Mod 已不存在。");
                }
            }
            catch (OperationCanceledException)
            {
                task.MarkCanceled();
            }
            catch (Exception exception)
            {
                task.MarkFailed(exception.Message);
            }
        }

        private async Task ExecuteSelectionDeleteAsync()
        {
            if (!_selection.HasSelection) return;
            var ids = _selection.SelectedIds.ToList();
            if (string.Equals(_selection.Scope, "Library", StringComparison.OrdinalIgnoreCase))
            {
                var confirm = System.Windows.MessageBox.Show($"确定删除选中的 {ids.Count} 个 Mod？\n这会同时删除库中的已存储文件。", "批量删除 Mod", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (confirm != System.Windows.MessageBoxResult.Yes) return;
                foreach (var guid in ids) _libraryService.Remove(guid);
                _libraryService.Save();
                _notificationService.Show($"已删除：{ids.Count} 个 Mod");
            }
            else if (string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase))
            {
                var task = _backgroundTasks.Enqueue(BackgroundTaskKind.Other, "从配置移除", $"{ids.Count} 个 Mod");
                task.MarkRunning("正在写入配置");
                try
                {
                    var removed = 0;
                    foreach (var guid in ids)
                    {
                        task.CancellationToken.ThrowIfCancellationRequested();
                        if (await _profileService.RemoveModFromSelectedAsync(guid, task.CancellationToken)) removed++;
                    }
                    task.MarkCompleted();
                    _notificationService.Show($"已从配置移除：{removed} 个 Mod");
                }
                catch (OperationCanceledException)
                {
                    task.MarkCanceled();
                    return;
                }
                catch (Exception exception)
                {
                    task.MarkFailed(exception.Message);
                    return;
                }
            }

            _selection.Clear();
            RefreshCurrentPage();
        }

        private void RaiseSelectionFlags()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(SelectionPrimaryText));
            OnPropertyChanged(nameof(SelectionDeleteText));
        }

        private static void RefreshOnUiThread(Action refresh)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                refresh();
                return;
            }
            _ = dispatcher.InvokeAsync(refresh);
        }

        private PageViewModel CreatePage(WorkspacePageType pageType)
        {
            var stopwatch = Stopwatch.StartNew();
            PageViewModel page = pageType switch
            {
                WorkspacePageType.Home => new HomePageViewModel(_profileService, _libraryService, _importQueue, _applyStatus, _backgroundTasks),
                WorkspacePageType.Profile => new ProfilePageViewModel(_profileService, _libraryService, _derivedState, _selection),
                WorkspacePageType.Library => CreateLibraryPage(),
                WorkspacePageType.Settings => new SettingsPageViewModel(_profileService, _libraryService, _backgroundTasks),
                WorkspacePageType.ModDetails => new ModDetailsPageViewModel(_libraryService, _profileService, _derivedState, SelectedModId ?? string.Empty, _notificationService),
                WorkspacePageType.AdvancedModDetails => new AdvancedModDetailsPageViewModel(_libraryService, _profileService, _derivedState, SelectedModId ?? string.Empty, _notificationService),
                WorkspacePageType.GameDataBrowser => new GameDataBrowserPageViewModel(_libraryService, _profileService),
                WorkspacePageType.GameDataArchiveDetails => new GameDataArchiveDetailsHostPageViewModel(null),
                WorkspacePageType.CrossArmorPlan => throw new InvalidOperationException("跨护甲计划必须通过专用路由创建。"),
                WorkspacePageType.MaterialPackaging => throw new InvalidOperationException("材质打包必须通过 Mod 详情创建。"),
                _ => new HomePageViewModel(_profileService, _libraryService, _importQueue, _applyStatus, _backgroundTasks),
            };
            LogService.Info($"UI 性能：创建 {pageType} ViewModel 耗时 {stopwatch.ElapsedMilliseconds}ms。");
            return page;
        }

        private LibraryPageViewModel CreateLibraryPage()
        {
            var hideSelectedProfileMembers = LeftPageType == WorkspacePageType.Profile || RightPageType == WorkspacePageType.Profile;
            var page = new LibraryPageViewModel(_libraryService, _derivedState, _selection, _profileService, _notificationService, hideSelectedProfileMembers);
            RegisterLibraryActions(page);
            return page;
        }

        private void RegisterLibraryActions(PageViewModel page)
        {
            page.PageActions.Add(new PageActionViewModel("⟳", "刷新当前页", RefreshCommand, background: new SolidColorBrush(Color.FromRgb(94, 100, 112)), order: 10, kind: "Refresh"));
            page.PageActions.Add(new PageActionViewModel("＋", "导入 Mod", ImportCommand, order: 20, kind: "Import"));
            page.PageActions.Add(new PageActionViewModel("▶", "立即重新部署", ApplyChangesCommand, background: new SolidColorBrush(Color.FromRgb(46, 125, 50)), order: 30, kind: "ScheduleDeployment"));
        }

        private void UpdateModeFromSlots()
        {
            CurrentMode = (LeftPageType, RightPageType, IsSplitView) switch
            {
                (WorkspacePageType.Home, WorkspacePageType.Home, false) => WorkspaceMode.Home,
                (WorkspacePageType.Profile, WorkspacePageType.Profile, false) => WorkspaceMode.ProfileOnly,
                (WorkspacePageType.Library, WorkspacePageType.Library, false) => WorkspaceMode.LibraryOnly,
                (WorkspacePageType.Profile, WorkspacePageType.Library, true) => WorkspaceMode.ProfileLibrarySplit,
                (WorkspacePageType.Settings, WorkspacePageType.Settings, false) => WorkspaceMode.Settings,
                _ => CurrentMode,
            };
        }

        private static string GetPageTitle(WorkspacePageType pageType) => pageType switch
        {
            WorkspacePageType.Home => "首页",
            WorkspacePageType.Profile => "配置页",
            WorkspacePageType.Library => "模组库",
            WorkspacePageType.Settings => "设置",
            WorkspacePageType.ModDetails => "Mod 详情",
            WorkspacePageType.AdvancedModDetails => "高级 Mod 详情",
            WorkspacePageType.GameDataBrowser => "Game Data 资产",
            WorkspacePageType.GameDataArchiveDetails => "Archive 详情",
            WorkspacePageType.CrossArmorPlan => "跨护甲计划",
            WorkspacePageType.MaterialPackaging => "材质候选与输出",
            _ => "页面",
        };

        private void RaiseSlotFlags()
        {
            UpdateLibraryCompanionContext();
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

        private void UpdateLibraryCompanionContext()
        {
            _bottomBar.SetLibraryProfileCompanionVisible(IsSplitView
                && ((LeftPageType == WorkspacePageType.Library && RightPageType == WorkspacePageType.Profile)
                    || (RightPageType == WorkspacePageType.Library && LeftPageType == WorkspacePageType.Profile)));
            if (LeftPage is LibraryPageViewModel leftLibrary)
                leftLibrary.SetProfileCompanionVisible(IsSplitView && RightPageType == WorkspacePageType.Profile);
            if (RightPage is LibraryPageViewModel rightLibrary)
                rightLibrary.SetProfileCompanionVisible(IsSplitView && LeftPageType == WorkspacePageType.Profile);
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
            OnPropertyChanged(nameof(ShowHomeTitle));
            OnPropertyChanged(nameof(ShowLaunchGameButton));
            OnPropertyChanged(nameof(IsProfileActive));
            OnPropertyChanged(nameof(IsLibraryActive));
            OnPropertyChanged(nameof(IsSplitActive));
            OnPropertyChanged(nameof(IsSettingsActive));
        }
    }
}

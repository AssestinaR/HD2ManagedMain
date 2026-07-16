using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    // Purpose: Provides bindable state and commands for Manager pages.
    public abstract class PageViewModel : BaseViewModel, IPageActionProvider, IDisposable
    {
        private string _title = string.Empty;
        public string Title { get => _title; set => SetField(ref _title, value); }
        public virtual bool RequiresSingleSlot => false;
        public ObservableCollection<PageActionViewModel> PageActions { get; } = new();
        public virtual void Dispose() { }
    }

    public sealed class HomePageViewModel : PageViewModel
    {
        private readonly ProfileService _profiles;
        private readonly ModLibraryService _library;
        private readonly ImportQueueService _queue;
        private readonly ApplyStatusService _applyStatus;
        private DeploymentCapability _deploymentCapability = DeploymentCapability.Unavailable("尚未检测。");

        public string ActiveProfile => _profiles.ActiveKey ?? "未启用";
        public int ModCount => _library.All().Count();
        public int ProfileCount => _profiles.All().Count;
        public string ActiveProfileModSummary => BuildActiveProfileModSummary(_profiles.ActiveProfile);
        public string QueueSummary => $"总计 {_queue.Tasks.Count}，完成 {_queue.CountDone}，待处理 {_queue.CountQueued + _queue.CountRunning}";
        public string ApplySummary => _applyStatus.Summary;
        public DeploymentCapability DeploymentCapability => _deploymentCapability;
        public bool IsDeploymentBlocked => !DeploymentCapability.IsAvailable;
        public string DeploymentCapabilityText => DeploymentCapability.IsAvailable
            ? $"当前部署方式：{(DeploymentCapability.Method == DeploymentMethod.HardLink ? "硬链接" : "符号链接")}。{DeploymentCapability.Summary}"
            : $"当前无法部署 Mod：{DeploymentCapability.Error}";
        public RelayCommand MoveLibraryToRecommendedCommand { get; }
        public RelayCommand OpenDeveloperSettingsCommand { get; }
        public RelayCommand RestartAsAdministratorCommand { get; }

        public HomePageViewModel(ProfileService profiles, ModLibraryService library, ImportQueueService queue, ApplyStatusService applyStatus)
        {
            Title = "首页";
            _profiles = profiles;
            _library = library;
            _queue = queue;
            _applyStatus = applyStatus;
            RefreshDeploymentCapability();
            MoveLibraryToRecommendedCommand = new RelayCommand(MoveLibraryToRecommended);
            OpenDeveloperSettingsCommand = new RelayCommand(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:developers") { UseShellExecute = true }));
            RestartAsAdministratorCommand = new RelayCommand(RestartAsAdministrator);
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(ActiveProfile));
            OnPropertyChanged(nameof(ModCount));
            OnPropertyChanged(nameof(ProfileCount));
            OnPropertyChanged(nameof(ActiveProfileModSummary));
            OnPropertyChanged(nameof(QueueSummary));
            OnPropertyChanged(nameof(ApplySummary));
            RefreshDeploymentCapability();
            OnPropertyChanged(nameof(DeploymentCapability));
            OnPropertyChanged(nameof(IsDeploymentBlocked));
            OnPropertyChanged(nameof(DeploymentCapabilityText));
        }

        private static string BuildActiveProfileModSummary(Profile? profile)
        {
            if (profile == null) return "无活动配置";
            return $"已启用 {profile.Entries.Count} 个 Mod";
        }

        private async void MoveLibraryToRecommended()
        {
            var source = SettingsService.GetModLibraryFolder();
            var target = SettingsService.GetRecommendedModLibraryFolder();
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                Directory.CreateDirectory(target);
                if (Directory.Exists(source))
                {
                    foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(source, sourceFile);
                        var targetFile = Path.Combine(target, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                        await Task.Run(() => File.Copy(sourceFile, targetFile, overwrite: true));
                        var sourceHash = await HashFileAsync(sourceFile);
                        var targetHash = await HashFileAsync(targetFile);
                        if (!sourceHash.SequenceEqual(targetHash)) throw new IOException($"迁移校验失败：{relative}");
                    }
                }
                SettingsService.SetModLibraryFolder(target);
                System.Windows.MessageBox.Show("Mod 库已复制并校验到推荐目录，旧库被保留。应用将重新启动以载入新路径。", "移动 Mod 库", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                RestartCurrentProcess(elevated: false);
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show($"迁移失败，当前 Mod 库未切换：{exception.Message}", "移动 Mod 库", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private static void RestartAsAdministrator()
            => RestartCurrentProcess(elevated: true);

        private static void RestartCurrentProcess(bool elevated)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return;
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = true };
                if (elevated) startInfo.Verb = "runas";
                System.Diagnostics.Process.Start(startInfo);
                System.Windows.Application.Current.Shutdown();
            }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private void RefreshDeploymentCapability()
            => _deploymentCapability = CoreServices.CreateDeploymentCapabilityService().Probe(_library.ModsRootDirectory, SettingsService.GetGameDataFolder());

        private static async Task<byte[]> HashFileAsync(string path)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await SHA256.HashDataAsync(stream);
        }
    }

    public sealed class StatusPageViewModel : PageViewModel, IDisposable
    {
        private readonly ProfileService _profiles;
        private readonly ModLibraryService _library;
        private readonly ImportQueueService _queue;
        private readonly BackgroundTaskService _backgroundTasks;
        private readonly ApplyStatusService _applyStatus;
        private readonly StoragePaths _paths;

        private string _assetIndexState = "未检查";
        private string _assetIndexSummary = "尚未刷新";
        private string _assetIndexBuiltUtc = "未知";
        private string _assetIndexGameData = "未知";
        private string _assetIndexCounts = "未知";
        private string _assetIndexHint = "刷新状态后显示索引诊断。";
        private string _assetMetadataStatus = "未知";
        private bool _isBuildingAssetIndex;
        private bool _isRefreshingAssetIndex;
        private readonly EventHandler _backgroundTasksChangedHandler;
        private CancellationTokenSource? _statusRefreshCancellation;
        private bool _disposed;

        public string ActiveProfile => _profiles.ActiveKey ?? "未启用";
        public string GameDataFolder => SettingsService.GetGameDataFolder();
        public string ModsRoot => _library.ModsRootDirectory;
        public int ModCount => _library.All().Count();
        public int ProfileCount => _profiles.All().Count;
        public string QueueSummary => $"Queued={_queue.CountQueued}, Running={_queue.CountRunning}, Done={_queue.CountDone}, Failed={_queue.CountFailed}";
        public string ApplySummary => _applyStatus.Summary;
        public string LastAppliedUtc => _applyStatus.LastAppliedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未应用";
        public string ProfileEntrySummary => BuildProfileEntrySummary();
        public string ConflictSummary => BuildConflictSummary();
        public string AssetIndexState { get => _assetIndexState; private set => SetField(ref _assetIndexState, value); }
        public string AssetIndexSummary { get => _assetIndexSummary; private set => SetField(ref _assetIndexSummary, value); }
        public string AssetIndexBuiltUtc { get => _assetIndexBuiltUtc; private set => SetField(ref _assetIndexBuiltUtc, value); }
        public string AssetIndexGameData { get => _assetIndexGameData; private set => SetField(ref _assetIndexGameData, value); }
        public string AssetIndexCounts { get => _assetIndexCounts; private set => SetField(ref _assetIndexCounts, value); }
        public string AssetIndexHint { get => _assetIndexHint; private set => SetField(ref _assetIndexHint, value); }
        public string AssetMetadataStatus { get => _assetMetadataStatus; private set => SetField(ref _assetMetadataStatus, value); }
        public bool IsBuildingAssetIndex { get => _isBuildingAssetIndex; private set => SetField(ref _isBuildingAssetIndex, value); }
        public bool IsRefreshingAssetIndex { get => _isRefreshingAssetIndex; private set => SetField(ref _isRefreshingAssetIndex, value); }
        public ObservableCollection<string> ApplyDetails { get; } = new();
        public ReadOnlyObservableCollection<BackgroundTaskItem> BackgroundTasks => _backgroundTasks.Tasks;
        public IEnumerable<BackgroundTaskItem> RunningTasks => BackgroundTasks.Where(t => t.Status == BackgroundTaskStatus.Running);
        public IEnumerable<BackgroundTaskItem> QueuedTasks => BackgroundTasks.Where(t => t.Status == BackgroundTaskStatus.Queued);
        public IEnumerable<BackgroundTaskItem> RecentCompletedTasks => BackgroundTasks.Where(t => t.Status == BackgroundTaskStatus.Completed).OrderByDescending(t => t.FinishedAt).Take(4);
        public int MoreQueuedTaskCount => Math.Max(0, _backgroundTasks.CountQueued - 4);
        public int MoreCompletedTaskCount => Math.Max(0, _backgroundTasks.CountCompleted - 4);
        public string BackgroundTaskSummary => $"进行中 {_backgroundTasks.CountRunning} · 排队中 {_backgroundTasks.CountQueued} · 已完成 {_backgroundTasks.CountCompleted} · 失败 {_backgroundTasks.CountFailed}";
        public bool HasRunningTasks => RunningTasks.Any();
        public bool HasQueuedTasks => QueuedTasks.Any();
        public bool HasCompletedTasks => RecentCompletedTasks.Any();
        public RelayCommand ShowAllTasksCommand { get; }

        public RelayCommand RefreshCommand { get; }
        public RelayCommand BuildAssetIndexCommand { get; }

        public StatusPageViewModel(ProfileService profiles, ModLibraryService library, ImportQueueService queue, ApplyStatusService applyStatus, BackgroundTaskService backgroundTasks)
        {
            Title = "状态";
            _profiles = profiles;
            _library = library;
            _queue = queue;
            _backgroundTasks = backgroundTasks;
            _applyStatus = applyStatus;
            _paths = SettingsService.CreateStoragePaths();
            RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !IsRefreshingAssetIndex);
            BuildAssetIndexCommand = new RelayCommand(_ => BuildAssetIndex(), _ => !IsBuildingAssetIndex);
            ShowAllTasksCommand = new RelayCommand(_ => ShowAllTasks());
            _backgroundTasksChangedHandler = (_, _) => RefreshBackgroundTaskProperties();
            _backgroundTasks.Changed += _backgroundTasksChangedHandler;
            RefreshDisplayProperties();
            _ = RefreshAsync();
        }

        public void Refresh() => _ = RefreshAsync();

        private void RefreshDisplayProperties()
        {
            OnPropertyChanged(nameof(ActiveProfile));
            OnPropertyChanged(nameof(GameDataFolder));
            OnPropertyChanged(nameof(ModsRoot));
            OnPropertyChanged(nameof(ModCount));
            OnPropertyChanged(nameof(ProfileCount));
            OnPropertyChanged(nameof(QueueSummary));
            RefreshBackgroundTaskProperties();
            OnPropertyChanged(nameof(ApplySummary));
            OnPropertyChanged(nameof(LastAppliedUtc));
            OnPropertyChanged(nameof(ProfileEntrySummary));
            OnPropertyChanged(nameof(ConflictSummary));
            ApplyDetails.Clear();
            foreach (var detail in _applyStatus.Details) ApplyDetails.Add(detail);
        }

        private async Task RefreshAsync()
        {
            if (_disposed || IsRefreshingAssetIndex) return;

            RefreshDisplayProperties();
            _statusRefreshCancellation?.Cancel();
            _statusRefreshCancellation?.Dispose();
            _statusRefreshCancellation = new CancellationTokenSource();
            IsRefreshingAssetIndex = true;
            RefreshCommand.RaiseCanExecuteChanged();
            AssetIndexState = "检查中";
            AssetIndexSummary = "正在读取索引状态。";

            try
            {
                await RefreshAssetIndexStatusAsync(_statusRefreshCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                if (!_disposed)
                {
                    AssetIndexState = "已取消";
                    AssetIndexSummary = "索引状态检查已取消。";
                }
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    AssetIndexState = "检查失败";
                    AssetIndexSummary = ex.Message;
                    AssetIndexBuiltUtc = "未知";
                    AssetIndexGameData = SettingsService.GetGameDataFolder();
                    AssetIndexCounts = "未知";
                    AssetIndexHint = "请检查 Game Data 路径、资产元数据和索引数据库是否可访问。";
                }
            }
            finally
            {
                if (!_disposed)
                {
                    IsRefreshingAssetIndex = false;
                    RefreshCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private void RefreshBackgroundTaskProperties()
        {
            if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                _ = dispatcher.InvokeAsync(RefreshBackgroundTaskProperties);
                return;
            }
            OnPropertyChanged(nameof(BackgroundTaskSummary));
            OnPropertyChanged(nameof(RunningTasks));
            OnPropertyChanged(nameof(QueuedTasks));
            OnPropertyChanged(nameof(RecentCompletedTasks));
            OnPropertyChanged(nameof(MoreQueuedTaskCount));
            OnPropertyChanged(nameof(MoreCompletedTaskCount));
            OnPropertyChanged(nameof(HasRunningTasks));
            OnPropertyChanged(nameof(HasQueuedTasks));
            OnPropertyChanged(nameof(HasCompletedTasks));
            OnPropertyChanged(nameof(BackgroundTasks));
        }

        private void ShowAllTasks()
        {
            var window = new HD2ModManager.Views.BackgroundTasksWindow(_backgroundTasks)
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };
            window.ShowDialog();
        }

        private async Task RefreshAssetIndexStatusAsync(CancellationToken cancellationToken)
        {
            AssetMetadataStatus = File.Exists(_paths.ArchiveHashesPath)
                ? $"已找到 archivehashes.json：{_paths.ArchiveHashesPath}"
                : $"缺少 archivehashes.json：{_paths.ArchiveHashesPath}";

            var gameData = SettingsService.GetGameDataFolder();
            if (string.IsNullOrWhiteSpace(gameData))
            {
                AssetIndexState = "未设置游戏目录";
                AssetIndexSummary = "无法判断索引状态，因为 Game Data 目录尚未设置。";
                AssetIndexBuiltUtc = "无";
                AssetIndexGameData = "未设置";
                AssetIndexCounts = "无";
                AssetIndexHint = "请先在设置页配置 Game Data 目录，再建立资产索引。";
                return;
            }

            var index = CoreServices.CreateAssetArchiveIndexService(_paths);
            var fingerprint = await index.GetFingerprintAsync(cancellationToken);
            if (fingerprint is null)
            {
                AssetIndexState = "缺失";
                AssetIndexSummary = "未找到资产反向索引数据库，无法从 patch 资产反查真实装备/分类。";
                AssetIndexBuiltUtc = "无";
                AssetIndexGameData = gameData;
                AssetIndexCounts = "无";
                AssetIndexHint = "这是当前无语义资产标签的最可能原因。需要先基于当前 Game Data 建立索引。";
                return;
            }

            AssetIndexBuiltUtc = fingerprint.BuiltUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            AssetIndexGameData = string.IsNullOrWhiteSpace(fingerprint.GameDataDirectory) ? gameData : fingerprint.GameDataDirectory;
            AssetIndexCounts = $"Archive {fingerprint.ArchivesIndexed}/{fingerprint.ArchivesTotal}，AssetKey {fingerprint.AssetKeysTotal}";

            if (!File.Exists(_paths.ArchiveHashesPath))
            {
                AssetIndexState = "无法校验";
                AssetIndexSummary = "索引数据库存在，但缺少 archivehashes.json，无法判断是否与当前游戏文件匹配。";
                AssetIndexHint = "请先在设置页更新资产信息，然后刷新状态。";
                return;
            }

            var archiveHashesJson = await File.ReadAllTextAsync(_paths.ArchiveHashesPath, cancellationToken);
            var status = await index.GetIndexStatusAsync(gameData, archiveHashesJson, cancellationToken);
            AssetIndexState = ToDisplayState(status.State);
            AssetIndexSummary = status.State switch
            {
                GameDataIndexState.Current => "索引与当前 Game Data 匹配，资产标签可以使用真实 archive 语义。",
                GameDataIndexState.Stale => "索引存在但已过期，当前游戏文件或 archive metadata 已变化。",
                GameDataIndexState.Invalid => "索引状态无法验证，资产元数据格式可能无效。",
                GameDataIndexState.Missing => "未找到资产反向索引数据库。",
                _ => "索引状态未知。"
            };
            AssetIndexHint = status.State == GameDataIndexState.Current
                ? "如果仍无标签，可能是该 mod 的 TypeID/FileID 未命中当前游戏索引，或旧资产分析缓存尚未刷新。"
                : "语义资产标签依赖当前索引；请重建索引后刷新模组库资产摘要。";
        }

        private async void BuildAssetIndex()
        {
            if (IsBuildingAssetIndex) return;

            var gameData = SettingsService.GetGameDataFolder();
            if (string.IsNullOrWhiteSpace(gameData) || !Directory.Exists(gameData))
            {
                AssetIndexState = "无法建立";
                AssetIndexSummary = "Game Data 目录未设置或不存在。";
                AssetIndexHint = "请先在设置页配置正确的 Helldivers 2 data 目录。";
                return;
            }

            if (!File.Exists(_paths.ArchiveHashesPath))
            {
                AssetIndexState = "无法建立";
                AssetIndexSummary = "缺少 archivehashes.json，无法知道需要索引哪些 archive。";
                AssetIndexHint = "请先在设置页更新资产信息，然后再建立资产索引。";
                return;
            }

            IsBuildingAssetIndex = true;
            var backgroundTask = _backgroundTasks.Enqueue(BackgroundTaskKind.BuildAssetIndex, "建立资产索引", gameData);
            backgroundTask.MarkRunning("正在准备资产索引");
            BuildAssetIndexCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            AssetIndexState = "建立中";
            AssetIndexSummary = "正在解析 Game Data。新版 slim/bundled 数据会通过 bundles.nxa 与 bundles.xx.nxa 建立索引。";
            AssetIndexCounts = "准备中";
            AssetIndexGameData = gameData;
            AssetIndexHint = "建立过程可能需要一些时间，请不要关闭程序。";

            try
            {
                var archiveHashesJson = await File.ReadAllTextAsync(_paths.ArchiveHashesPath).ConfigureAwait(true);
                var index = CoreServices.CreateAssetArchiveIndexService(_paths);
                var progress = new Progress<IndexBuildProgress>(p =>
                {
                    backgroundTask.UpdateStage($"正在索引 Archive {p.Current}/{p.Total}");
                    AssetIndexCounts = $"Archive {p.Current}/{p.Total}";
                    if (!string.IsNullOrWhiteSpace(p.CurrentArchiveId))
                    {
                        AssetIndexHint = $"正在索引 {p.CurrentArchiveId}。";
                    }
                });

                await Task.Run(
                    () => index.BuildOrRebuildAsync(gameData, archiveHashesJson, progress, backgroundTask.CancellationToken).AsTask(),
                    backgroundTask.CancellationToken).ConfigureAwait(true);
                await RefreshAsync();
                AssetIndexHint = "索引已重建；稳定资产事实会在需要时直接投影到界面。";
                backgroundTask.MarkCompleted();
            }
            catch (OperationCanceledException)
            {
                backgroundTask.MarkCanceled();
                AssetIndexState = "已取消";
                AssetIndexSummary = "资产索引建立已取消。";
            }
            catch (Exception ex)
            {
                var detail = FormatAssetIndexException(ex);
                backgroundTask.MarkFailed(detail);
                AssetIndexState = "建立失败";
                AssetIndexSummary = detail;
                AssetIndexHint = $"索引失败。路径包含空格或中文通常不会导致此问题。GameData：{gameData}";
            }
            finally
            {
                IsBuildingAssetIndex = false;
                BuildAssetIndexCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }

        private static string FormatAssetIndexException(Exception exception)
        {
            var messages = new List<string>();
            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message, StringComparer.Ordinal))
                {
                    messages.Add(current.Message);
                }
            }

            return $"{exception.GetType().Name}: {string.Join(" | ", messages)}";
        }

        private static string ToDisplayState(GameDataIndexState state)
            => state switch
            {
                GameDataIndexState.Current => "可用",
                GameDataIndexState.Stale => "过期",
                GameDataIndexState.Missing => "缺失",
                GameDataIndexState.Invalid => "无效",
                _ => "未知"
            };

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _backgroundTasks.Changed -= _backgroundTasksChangedHandler;
            _statusRefreshCancellation?.Cancel();
            _statusRefreshCancellation?.Dispose();
            _statusRefreshCancellation = null;
        }

        private string BuildProfileEntrySummary()
        {
            var profile = _profiles.ActiveProfile;
            if (profile == null) return "无活动配置";
            return $"已启用 {profile.Entries.Count} 个 Mod";
        }

        private string BuildConflictSummary()
        {
            try
            {
                var profile = _profiles.ActiveProfile;
                if (profile == null) return "无活动配置，未检测冲突";
                var ids = profile.Entries.Select(e => e.NodeId).Distinct().ToList();
                if (ids.Count < 2) return "启用 Mod 少于 2 个，无需检测";

                var detector = CoreServices.CreateConflictDetector(_paths);
                var conflicts = detector.DetectNodeConflictsAsync(ids, _library.Snapshot, _library.ModsRootDirectory).AsTask().GetAwaiter().GetResult();
                if (conflicts.Count == 0) return "未发现启用 Mod 资产冲突";
                var sharedKeyCount = conflicts.Sum(c => c.SharedKeys.Count);
                return $"发现 {conflicts.Count} 组冲突，涉及 {sharedKeyCount} 个资产键";
            }
            catch (Exception ex)
            {
                return $"冲突检测失败：{ex.Message}";
            }
        }
    }

    public sealed class ProfilePageViewModel : PageViewModel
    {
        private const string SelectionScope = "Profile";
        private readonly ProfileService _profiles;
        private readonly ModLibraryService _library;
        private readonly DerivedStateCoordinator _derivedState;
        private readonly SelectionCoordinator? _selection;
        private readonly ObservableCollection<string> _selectedGuids = new();
        private readonly Dictionary<string, ModUserStatus> _userStatuses = new(StringComparer.OrdinalIgnoreCase);
        private string? _selectionAnchorGuid;
        private string _query = string.Empty;

        public ObservableCollection<ProfileListItemViewModel> Items { get; } = new();
        public ObservableCollection<string> Profiles { get; } = new();
        public string Query { get => _query; set { if (SetField(ref _query, value)) Refresh(); } }

        private string? _selectedProfileKey;
        private string _renameText = string.Empty;
        public string? SelectedProfileKey
        {
            get => _selectedProfileKey;
            set
            {
                if (SetField(ref _selectedProfileKey, value) && !string.IsNullOrWhiteSpace(value))
                {
                    _profiles.Select(value);
                    Refresh();
                }
            }
        }
        public string ActiveProfileText => _profiles.ActiveKey is { } name ? $"活动配置：{name}" : "当前没有活动配置";
        public string SelectedProfileState => _profiles.SelectedProfileId is { } selected && selected == _profiles.ActiveProfileId ? "正在编辑活动配置" : "正在编辑非活动配置";

        public string RenameText
        {
            get => _renameText;
            set => SetField(ref _renameText, value);
        }

        public RelayCommand CreateProfileCommand { get; }
        public RelayCommand RemoveSelectedProfileCommand { get; }
        public RelayCommand RenameProfileCommand { get; }
        public RelayCommand ActivateProfileCommand { get; }
        public RelayCommand DeactivateProfileCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand RemoveModCommand { get; }
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }
        public RelayCommand ToggleSelectionCommand { get; }

        public ProfilePageViewModel(ProfileService profiles, ModLibraryService library, DerivedStateCoordinator derivedState, SelectionCoordinator? selection = null)
        {
            Title = "配置页";
            _profiles = profiles;
            _library = library;
            _derivedState = derivedState;
            _selection = selection;
            if (_selection != null) _selection.SelectionChanged += (_, _) => SyncSelectionFromCoordinator();
            _profiles.Changed += (_, _) => QueueStatusRefresh();
            _derivedState.SnapshotChanged += (_, _) => RunOnUiThread(QueueStatusRefresh);
            CreateProfileCommand = new RelayCommand(CreateProfile);
            RemoveSelectedProfileCommand = new RelayCommand(RemoveSelectedProfile);
            RenameProfileCommand = new RelayCommand(RenameProfile);
            ActivateProfileCommand = new RelayCommand(ActivateProfile);
            DeactivateProfileCommand = new RelayCommand(DeactivateProfile);
            RefreshCommand = new RelayCommand(Refresh);
            RemoveModCommand = new RelayCommand(RemoveMod);
            MoveUpCommand = new RelayCommand(parameter => MoveMod(parameter, -1));
            MoveDownCommand = new RelayCommand(parameter => MoveMod(parameter, 1));
            ToggleSelectionCommand = new RelayCommand(ToggleSelection);
            PageActions.Add(new PageActionViewModel("＋", "新建配置", CreateProfileCommand, order: 10, kind: "CreateProfile"));
            PageActions.Add(new PageActionViewModel("▶", "设为活动配置", ActivateProfileCommand, background: new SolidColorBrush(Color.FromRgb(26, 127, 75)), order: 15, kind: "ActivateProfile"));
            PageActions.Add(new PageActionViewModel("■", "停用活动配置", DeactivateProfileCommand, background: new SolidColorBrush(Color.FromRgb(94, 100, 112)), order: 16, kind: "DeactivateProfile"));
            PageActions.Add(new PageActionViewModel("✎", "重命名配置", RenameProfileCommand, background: new SolidColorBrush(Color.FromRgb(30, 99, 214)), order: 20, kind: "RenameProfile"));
            PageActions.Add(new PageActionViewModel("🗑", "删除当前配置", RemoveSelectedProfileCommand, background: new SolidColorBrush(Color.FromRgb(179, 38, 30)), order: 30, kind: "RemoveProfile"));
            PageActions.Add(new PageActionViewModel("⟳", "刷新配置", RefreshCommand, background: new SolidColorBrush(Color.FromRgb(94, 100, 112)), order: 40, kind: "RefreshProfile"));
            Refresh();
            QueueStatusRefresh();
        }

        public void AddMod(string guid)
        {
            if (_profiles.SelectedProfile == null)
            {
                _profiles.CreateNew();
            }

            _profiles.AddModToSelected(guid);
            Refresh();
        }

        public void SelectRow(string guid, ModifierKeys modifiers)
        {
            if (string.IsNullOrWhiteSpace(guid)) return;
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && !string.IsNullOrWhiteSpace(_selectionAnchorGuid))
            {
                var all = Items.ToList();
                var anchorIndex = all.FindIndex(i => string.Equals(i.Guid, _selectionAnchorGuid, StringComparison.OrdinalIgnoreCase));
                var targetIndex = all.FindIndex(i => string.Equals(i.Guid, guid, StringComparison.OrdinalIgnoreCase));
                if (anchorIndex >= 0 && targetIndex >= 0)
                {
                    _selectedGuids.Clear();
                    foreach (var item in all.Skip(Math.Min(anchorIndex, targetIndex)).Take(Math.Abs(anchorIndex - targetIndex) + 1))
                    {
                        _selectedGuids.Add(item.Guid);
                    }
                }
            }
            else if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (!_selectedGuids.Remove(guid)) _selectedGuids.Add(guid);
                _selectionAnchorGuid = guid;
            }
            else
            {
                _selectedGuids.Clear();
                _selectedGuids.Add(guid);
                _selectionAnchorGuid = guid;
            }

            _selection?.Replace(SelectionScope, _selectedGuids);
            RefreshSelectionFlags();
        }

        private void CreateProfile()
        {
            _profiles.CreateNew();
            Refresh();
        }

        private void ActivateProfile()
        {
            _profiles.ActivateSelected();
            Refresh();
        }

        private void DeactivateProfile()
        {
            _profiles.DisableActive();
            Refresh();
        }

        private void RemoveSelectedProfile()
        {
            var key = _profiles.SelectedKey;
            if (string.IsNullOrWhiteSpace(key)) return;
            var confirm = System.Windows.MessageBox.Show($"确定删除当前配置“{key}”？\n这只会移除配置，不会删除库中的 Mod 文件。", "删除配置", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
            _profiles.Remove(key);
            Refresh();
        }

        private void RenameProfile()
        {
            var key = _profiles.SelectedKey;
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(RenameText)) return;
            var newName = RenameText.Trim();
            if (string.Equals(key, newName, StringComparison.OrdinalIgnoreCase)) return;
            var confirm = System.Windows.MessageBox.Show($"将配置“{key}”重命名为“{newName}”？", "重命名配置", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
            _profiles.Rename(key, RenameText);
            Refresh();
        }

        private void RemoveMod(object? parameter)
        {
            var guid = parameter as string;
            if (string.IsNullOrWhiteSpace(guid)) return;
            _profiles.RemoveModFromSelected(guid);
            Refresh();
        }

        private void MoveMod(object? parameter, int direction)
        {
            var guid = parameter as string;
            if (string.IsNullOrWhiteSpace(guid)) return;
            _profiles.MoveModInSelected(guid, direction);
            Refresh();
        }

        public void Refresh()
        {
            Profiles.Clear();
            foreach (var profileItem in _profiles.All()) Profiles.Add(profileItem.Name);
            _selectedProfileKey = _profiles.SelectedKey;
            _renameText = _selectedProfileKey ?? string.Empty;
            OnPropertyChanged(nameof(SelectedProfileKey));
            OnPropertyChanged(nameof(RenameText));
            OnPropertyChanged(nameof(ActiveProfileText));
            OnPropertyChanged(nameof(SelectedProfileState));

            Items.Clear();
            var profile = _profiles.SelectedProfile;
            if (profile == null) return;
            foreach (var entry in _profiles.GetSortedEntries(profile))
            {
                var guid = entry.NodeId.Value.ToString("N");
                var mod = _library.Get(guid);
                var assetSummary = _library.GetDerivedData(guid)?.AssetSummary;
                if (!ModSearchMatcher.IsMatch(mod?.Name, mod?.Description, assetSummary, Query)) continue;
                _userStatuses.TryGetValue(guid, out var status);
                Items.Add(new ProfileListItemViewModel(guid, mod?.Name ?? guid, mod?.Description, mod?.Image, ModAssetSummaryFormatter.Format(assetSummary), entry.LoadOrder, entry.AddedUtc, IsSelected(guid), status));
            }
        }

        private async Task RefreshUserStatusesAsync()
        {
            await Task.Yield();
            var statuses = _derivedState.ProjectStatuses(_profiles.SelectedProfileId);
            _userStatuses.Clear();
            foreach (var pair in statuses) _userStatuses[pair.Key.Value.ToString("N")] = pair.Value;
            Refresh();
        }

        private void QueueStatusRefresh()
        {
            Refresh();
            _ = RefreshUserStatusesAsync();
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) action();
            else _ = dispatcher.InvokeAsync(action);
        }

        private void ToggleSelection(object? parameter)
        {
            var guid = parameter as string;
            if (string.IsNullOrWhiteSpace(guid)) return;
            if (!_selectedGuids.Remove(guid)) _selectedGuids.Add(guid);
            _selection?.Replace(SelectionScope, _selectedGuids);
            RefreshSelectionFlags();
        }

        private bool IsSelected(string guid) => _selectedGuids.Any(id => string.Equals(id, guid, StringComparison.OrdinalIgnoreCase));

        private void SyncSelectionFromCoordinator()
        {
            if (_selection == null) return;
            _selectedGuids.Clear();
            if (string.Equals(_selection.Scope, SelectionScope, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var id in _selection.SelectedIds) _selectedGuids.Add(id);
            }
            RefreshSelectionFlags();
        }

        private void RefreshSelectionFlags()
        {
            foreach (var item in Items) item.IsSelected = IsSelected(item.Guid);
        }
    }

    public sealed class ProfileListItemViewModel : BaseViewModel
    {
        public string Guid { get; }
        public string Name { get; }
        public string? Description { get; }
        public string? ImagePath { get; }
        public string AssetSummary { get; }
        public int LoadOrder { get; }
        public DateTimeOffset AddedUtc { get; }
        public ModUserStatus? UserStatus { get; }
        public string StatusText => UserStatus is null ? $"配置成员 · 顺序 {LoadOrder}" : $"{UserStatus.Title} · 顺序 {LoadOrder}";
        public string SecondaryDetailText => string.Join(" · ", new[] { Description, AssetSummary }.Where(value => !string.IsNullOrWhiteSpace(value)));
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

        public ProfileListItemViewModel(string guid, string name, string? description, string? imagePath, string assetSummary, int loadOrder, DateTimeOffset addedUtc, bool isSelected = false, ModUserStatus? userStatus = null)
        {
            Guid = guid;
            Name = name;
            Description = description;
            ImagePath = imagePath;
            AssetSummary = assetSummary;
            LoadOrder = loadOrder;
            AddedUtc = addedUtc;
            _isSelected = isSelected;
            UserStatus = userStatus;
        }
    }

    public class SettingsPageViewModel : PageViewModel
    {
		private readonly ProfileService _profiles;
		private readonly ModLibraryService _library;
        public string Language
        {
            get => SettingsService.GetLanguage() ?? "";
            set
            {
                SettingsService.SetLanguage(value);
                OnPropertyChanged(nameof(Language));
            }
        }

        public bool AutoCleanup
        {
            get => SettingsService.GetAutoCleanup();
            set
            {
                SettingsService.SetAutoCleanup(value);
                OnPropertyChanged(nameof(AutoCleanup));
            }
        }

        public bool EnableLibraryImages
        {
            get => SettingsService.GetEnableLibraryImages();
            set
            {
                SettingsService.SetEnableLibraryImages(value);
                OnPropertyChanged(nameof(EnableLibraryImages));
            }
        }

        public bool AutoUpdateAssetMetadata
        {
            get => SettingsService.GetAutoUpdateAssetMetadata();
            set
            {
                SettingsService.SetAutoUpdateAssetMetadata(value);
                OnPropertyChanged(nameof(AutoUpdateAssetMetadata));
            }
        }

        public string AssetMetadataRepository
        {
            get => SettingsService.GetAssetMetadataRepository();
            set
            {
                SettingsService.SetAssetMetadataRepository(value);
                OnPropertyChanged(nameof(AssetMetadataRepository));
            }
        }

        private string _assetMetadataStatus = BuildInitialAssetMetadataStatus();
        public string AssetMetadataStatus
        {
            get => _assetMetadataStatus;
            private set => SetField(ref _assetMetadataStatus, value);
        }

        public string ModLibraryFolder
        {
            get => SettingsService.GetModLibraryFolder();
            set
            {
                SettingsService.SetModLibraryFolder(value);
                OnPropertyChanged(nameof(ModLibraryFolder));
            }
        }

        public string GameDataFolder
        {
            get => SettingsService.GetGameDataFolder();
            set
            {
                SettingsService.SetGameDataFolder(value);
                OnPropertyChanged(nameof(GameDataFolder));
            }
        }

        public RelayCommand OpenModFolderCommand { get; }
        public RelayCommand ResetModFolderCommand { get; }
        public RelayCommand OpenGameDataFolderCommand { get; }
        public RelayCommand DetectGameDataFolderCommand { get; }
        public RelayCommand ViewGameDataIndexCommand { get; }
        public RelayCommand UpdateAssetMetadataCommand { get; }
        private bool _isLoadingGameDataIndex;
        public bool IsLoadingGameDataIndex { get => _isLoadingGameDataIndex; private set => SetField(ref _isLoadingGameDataIndex, value); }

        public SettingsPageViewModel(ProfileService profiles, ModLibraryService library)
        {
            Title = "设置";
			_profiles = profiles;
			_library = library;
            OpenModFolderCommand = new RelayCommand(() => OpenFolder(ModLibraryFolder));
            ResetModFolderCommand = new RelayCommand(() => ModLibraryFolder = SettingsService.GetDefaultModLibraryFolder());
            OpenGameDataFolderCommand = new RelayCommand(OpenGameDataFolder);
            DetectGameDataFolderCommand = new RelayCommand(DetectGameDataFolder);
            ViewGameDataIndexCommand = new RelayCommand(_ => ViewGameDataIndex(), _ => !IsLoadingGameDataIndex);
            UpdateAssetMetadataCommand = new RelayCommand(UpdateAssetMetadata);
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(AutoCleanup));
            OnPropertyChanged(nameof(EnableLibraryImages));
            OnPropertyChanged(nameof(AutoUpdateAssetMetadata));
            OnPropertyChanged(nameof(AssetMetadataRepository));
            AssetMetadataStatus = BuildInitialAssetMetadataStatus();
            OnPropertyChanged(nameof(ModLibraryFolder));
            OnPropertyChanged(nameof(GameDataFolder));
        }

        private async void ViewGameDataIndex()
        {
            if (IsLoadingGameDataIndex) return;
            var gameData = SettingsService.GetGameDataFolder();
            if (string.IsNullOrWhiteSpace(gameData) || !Directory.Exists(gameData))
            {
                System.Windows.MessageBox.Show("请先配置有效的 Game Data 文件夹。", "GameData 资产索引", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            IsLoadingGameDataIndex = true;
            ViewGameDataIndexCommand.RaiseCanExecuteChanged();
            try
            {
                var paths = SettingsService.CreateStoragePaths();
                var index = CoreServices.CreateAssetArchiveIndexService(paths);
                var browser = CoreServices.CreateGameDataArchiveBrowserService(paths);
                var snapshot = await browser.BuildAsync(_library.Snapshot, _library.ModsRootDirectory, gameData).ConfigureAwait(true);
                if (snapshot is null)
                {
                    System.Windows.MessageBox.Show("当前 GameData 资产索引不可用。请先在状态页建立资产索引。", "GameData 资产索引", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (snapshot.Archives.Count == 0)
                {
                    System.Windows.MessageBox.Show("资产索引数据库中没有可显示的 archive。请重新建立资产索引。", "GameData 资产索引", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var window = new HD2ModManager.Views.GameDataIndexWindow
                {
                    Owner = System.Windows.Application.Current?.MainWindow,
                    DataContext = new HD2ModManager.Views.GameDataIndexWindowViewModel(snapshot, _profiles.ActiveKey, index),
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"读取 GameData 资产索引失败：{ex.Message}", "GameData 资产索引", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoadingGameDataIndex = false;
                ViewGameDataIndexCommand.RaiseCanExecuteChanged();
            }
        }

        public void PromptLanguageIfMissing()
        {
            if (!string.IsNullOrWhiteSpace(SettingsService.GetLanguage())) return;
            var result = System.Windows.MessageBox.Show(
                "选择语言?\nYes: 中文(zh-CN) / No: English(en-US)",
                "Language",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            Language = result == System.Windows.MessageBoxResult.Yes ? "zh-CN" : "en-US";
            System.Windows.MessageBox.Show("语言已设置为 " + Language + "，请重启应用以生效。", "Language", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private static void OpenFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }

        private void OpenGameDataFolder()
        {
            var folder = GameDataFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = SettingsService.TryDetectAndSetGameDataFolder();
                OnPropertyChanged(nameof(GameDataFolder));
            }
            OpenFolder(folder);
        }

        private void DetectGameDataFolder()
        {
            GameDataFolder = SettingsService.TryDetectAndSetGameDataFolder();
        }

        private async void UpdateAssetMetadata()
        {
            AssetMetadataStatus = "正在更新资产信息...";
            try
            {
                var paths = SettingsService.CreateStoragePaths();
                var sync = CoreServices.CreateAssetMetadataSyncService(paths);
                var result = await sync.SyncAsync(AssetMetadataRepository);
                if (!result.Success)
                {
                    AssetMetadataStatus = $"更新失败：{result.ErrorMessage}";
                    System.Windows.MessageBox.Show(AssetMetadataStatus, "资产信息", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                AssetMetadataStatus = $"更新成功：{result.UpdatedFiles.Count} 个文件，{result.UpdatedAtUtc?.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
                System.Windows.MessageBox.Show(AssetMetadataStatus, "资产信息", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AssetMetadataStatus = $"更新失败：{ex.Message}";
                System.Windows.MessageBox.Show(AssetMetadataStatus, "资产信息", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private static string BuildInitialAssetMetadataStatus()
        {
            var paths = SettingsService.CreateStoragePaths();
            if (!File.Exists(paths.AssetMetadataManifestPath)) return "尚未更新资产信息";
            try
            {
                var updated = File.GetLastWriteTime(paths.AssetMetadataManifestPath);
                return $"本地缓存已存在，最后写入 {updated:yyyy-MM-dd HH:mm:ss}";
            }
            catch
            {
                return "本地缓存已存在";
            }
        }
    }
}

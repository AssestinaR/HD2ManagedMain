using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    public abstract class PageViewModel : BaseViewModel, IPageActionProvider
    {
        private string _title = string.Empty;
        public string Title { get => _title; set => SetField(ref _title, value); }
        public virtual bool RequiresSingleSlot => false;
        public ObservableCollection<PageActionViewModel> PageActions { get; } = new();
    }

    public sealed class HomePageViewModel : PageViewModel
    {
        private readonly ProfileService _profiles;
        private readonly ModLibraryService _library;
        private readonly ImportQueueService _queue;
        private readonly ApplyStatusService _applyStatus;

        public string ActiveProfile => _profiles.ActiveKey ?? "未启用";
        public int ModCount => _library.All().Count();
        public int ProfileCount => _profiles.All().Count;
        public string ActiveProfileModSummary => BuildActiveProfileModSummary(_profiles.ActiveProfile);
        public string QueueSummary => $"总计 {_queue.Tasks.Count}，完成 {_queue.CountDone}，待处理 {_queue.CountQueued + _queue.CountRunning}";
        public string ApplySummary => _applyStatus.Summary;

        public HomePageViewModel(ProfileService profiles, ModLibraryService library, ImportQueueService queue, ApplyStatusService applyStatus)
        {
            Title = "首页";
            _profiles = profiles;
            _library = library;
            _queue = queue;
            _applyStatus = applyStatus;
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(ActiveProfile));
            OnPropertyChanged(nameof(ModCount));
            OnPropertyChanged(nameof(ProfileCount));
            OnPropertyChanged(nameof(ActiveProfileModSummary));
            OnPropertyChanged(nameof(QueueSummary));
            OnPropertyChanged(nameof(ApplySummary));
        }

        private static string BuildActiveProfileModSummary(Profile? profile)
        {
            if (profile == null) return "无活动配置";
            var enabled = profile.Entries.Count(e => e.Enabled);
            var disabled = profile.Entries.Count - enabled;
            return $"启用 {enabled}，禁用 {disabled}";
        }
    }

    public sealed class StatusPageViewModel : PageViewModel
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
            _paths = new StoragePaths(AppDomain.CurrentDomain.BaseDirectory);
            RefreshCommand = new RelayCommand(Refresh);
            BuildAssetIndexCommand = new RelayCommand(_ => BuildAssetIndex(), _ => !IsBuildingAssetIndex);
            ShowAllTasksCommand = new RelayCommand(_ => ShowAllTasks());
            _backgroundTasks.Changed += (_, _) => RefreshBackgroundTaskProperties();
            Refresh();
        }

        public void Refresh()
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
            RefreshAssetIndexStatus();
            ApplyDetails.Clear();
            foreach (var detail in _applyStatus.Details) ApplyDetails.Add(detail);
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

        private void RefreshAssetIndexStatus()
        {
            try
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
                var fingerprint = index.GetFingerprintAsync().AsTask().GetAwaiter().GetResult();
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
                    AssetIndexHint = "请先在设置页更新资产元数据，然后刷新状态。";
                    return;
                }

                var archiveHashesJson = File.ReadAllText(_paths.ArchiveHashesPath);
                var status = index.GetIndexStatusAsync(gameData, archiveHashesJson).AsTask().GetAwaiter().GetResult();
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
            catch (Exception ex)
            {
                AssetIndexState = "检查失败";
                AssetIndexSummary = ex.Message;
                AssetIndexBuiltUtc = "未知";
                AssetIndexGameData = SettingsService.GetGameDataFolder();
                AssetIndexCounts = "未知";
                AssetIndexHint = "请检查 Game Data 路径、资产元数据和索引数据库是否可访问。";
            }
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

                await index.BuildOrRebuildAsync(gameData, archiveHashesJson, progress, backgroundTask.CancellationToken).ConfigureAwait(true);
                ClearAssetAnalysisCache();
                RefreshAssetIndexStatus();
                AssetIndexHint = "索引已重建，并已清理旧资产分析缓存；请刷新模组库以重新生成语义资产标签。";
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
                backgroundTask.MarkFailed(ex.Message);
                AssetIndexState = "建立失败";
                AssetIndexSummary = ex.Message;
                AssetIndexHint = "请检查 Game Data 路径是否指向 Helldivers 2 的 data 目录，以及 bundles.nxa / bundles.xx.nxa 是否可访问。";
            }
            finally
            {
                IsBuildingAssetIndex = false;
                BuildAssetIndexCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }

        private void ClearAssetAnalysisCache()
        {
            try
            {
                if (Directory.Exists(_paths.AssetAnalysisCacheDirectory))
                {
                    Directory.Delete(_paths.AssetAnalysisCacheDirectory, recursive: true);
                }
            }
            catch
            {
                // Cache cleanup is best-effort; stale entries are also invalidated by metadata/index fingerprints.
            }
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

        private string BuildProfileEntrySummary()
        {
            var profile = _profiles.ActiveProfile;
            if (profile == null) return "无活动配置";
            var enabled = profile.Entries.Count(e => e.Enabled);
            var disabled = profile.Entries.Count - enabled;
            return $"总计 {profile.Entries.Count}，启用 {enabled}，禁用 {disabled}";
        }

        private string BuildConflictSummary()
        {
            try
            {
                var profile = _profiles.ActiveProfile;
                if (profile == null) return "无活动配置，未检测冲突";
                var ids = profile.Entries.Where(e => e.Enabled).Select(e => e.NodeId).Distinct().ToList();
                if (ids.Count < 2) return "启用 Mod 少于 2 个，无需检测";

                var detector = CoreServices.CreateConflictDetector();
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
        private readonly SelectionCoordinator? _selection;
        private readonly ObservableCollection<string> _selectedGuids = new();
        private string? _selectionAnchorGuid;

        public ObservableCollection<ProfileListItemViewModel> Items { get; } = new();
        public ObservableCollection<string> Profiles { get; } = new();

        private string? _activeProfileKey;
        private string _renameText = string.Empty;
        public string? ActiveProfileKey
        {
            get => _activeProfileKey;
            set
            {
                if (SetField(ref _activeProfileKey, value) && !string.IsNullOrWhiteSpace(value))
                {
                    _profiles.SetActive(value);
                    Refresh();
                }
            }
        }

        public string RenameText
        {
            get => _renameText;
            set => SetField(ref _renameText, value);
        }

        public RelayCommand CreateProfileCommand { get; }
        public RelayCommand RemoveSelectedProfileCommand { get; }
        public RelayCommand RenameProfileCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand RemoveModCommand { get; }
        public RelayCommand EnableModCommand { get; }
        public RelayCommand DisableModCommand { get; }
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }
        public RelayCommand ToggleSelectionCommand { get; }

        public ProfilePageViewModel(ProfileService profiles, ModLibraryService library, SelectionCoordinator? selection = null)
        {
            Title = "配置页";
            _profiles = profiles;
            _library = library;
            _selection = selection;
            if (_selection != null) _selection.SelectionChanged += (_, _) => SyncSelectionFromCoordinator();
            CreateProfileCommand = new RelayCommand(CreateProfile);
            RemoveSelectedProfileCommand = new RelayCommand(RemoveSelectedProfile);
            RenameProfileCommand = new RelayCommand(RenameProfile);
            RefreshCommand = new RelayCommand(Refresh);
            RemoveModCommand = new RelayCommand(RemoveMod);
            EnableModCommand = new RelayCommand(parameter => SetModEnabled(parameter, true));
            DisableModCommand = new RelayCommand(parameter => SetModEnabled(parameter, false));
            MoveUpCommand = new RelayCommand(parameter => MoveMod(parameter, -1));
            MoveDownCommand = new RelayCommand(parameter => MoveMod(parameter, 1));
            ToggleSelectionCommand = new RelayCommand(ToggleSelection);
            PageActions.Add(new PageActionViewModel("＋", "新建配置", CreateProfileCommand, order: 10, kind: "CreateProfile"));
            PageActions.Add(new PageActionViewModel("✎", "重命名配置", RenameProfileCommand, background: new SolidColorBrush(Color.FromRgb(30, 99, 214)), order: 20, kind: "RenameProfile"));
            PageActions.Add(new PageActionViewModel("🗑", "删除当前配置", RemoveSelectedProfileCommand, background: new SolidColorBrush(Color.FromRgb(179, 38, 30)), order: 30, kind: "RemoveProfile"));
            PageActions.Add(new PageActionViewModel("⟳", "刷新配置", RefreshCommand, background: new SolidColorBrush(Color.FromRgb(94, 100, 112)), order: 40, kind: "RefreshProfile"));
            Refresh();
        }

        public void AddMod(string guid)
        {
            if (_profiles.ActiveProfile == null)
            {
                _profiles.SetActive(_profiles.CreateNew());
            }

            _profiles.AddModToActive(guid);
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
            var key = _profiles.CreateNew();
            _profiles.SetActive(key);
            Refresh();
        }

        private void RemoveSelectedProfile()
        {
            var key = _profiles.ActiveKey;
            if (string.IsNullOrWhiteSpace(key)) return;
            var confirm = System.Windows.MessageBox.Show($"确定删除当前配置“{key}”？\n这只会移除配置，不会删除库中的 Mod 文件。", "删除配置", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
            _profiles.Remove(key);
            Refresh();
        }

        private void RenameProfile()
        {
            var key = _profiles.ActiveKey;
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
            _profiles.RemoveModFromActive(guid);
            Refresh();
        }

        private void MoveMod(object? parameter, int direction)
        {
            var guid = parameter as string;
            if (string.IsNullOrWhiteSpace(guid)) return;
            _profiles.MoveModInActive(guid, direction);
            Refresh();
        }

        private void SetModEnabled(object? parameter, bool enabled)
        {
            var guid = parameter as string;
            if (string.IsNullOrWhiteSpace(guid)) return;
            _profiles.SetModEnabledInActive(guid, enabled);
            Refresh();
        }

        public void Refresh()
        {
            Profiles.Clear();
            foreach (var profileItem in _profiles.All()) Profiles.Add(profileItem.Name);
            _activeProfileKey = _profiles.ActiveKey;
            _renameText = _activeProfileKey ?? string.Empty;
            OnPropertyChanged(nameof(ActiveProfileKey));
            OnPropertyChanged(nameof(RenameText));

            Items.Clear();
            var profile = _profiles.ActiveProfile;
            if (profile == null) return;
            foreach (var entry in _profiles.GetSortedEntries(profile))
            {
                var guid = entry.NodeId.Value.ToString("N");
                var mod = _library.Get(guid);
                Items.Add(new ProfileListItemViewModel(guid, mod?.Name ?? guid, mod?.Description, mod?.Image, string.Join(", ", mod?.Tags ?? new List<string>()), entry.LoadOrder, entry.Enabled, entry.AddedUtc, IsSelected(guid)));
            }
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
        public string TagsString { get; }
        public int LoadOrder { get; }
        public bool Enabled { get; }
        public DateTimeOffset AddedUtc { get; }
        public string StatusText => Enabled ? $"启用 · 顺序 {LoadOrder}" : $"禁用 · 顺序 {LoadOrder}";
        public string SecondaryText => string.IsNullOrWhiteSpace(Description) ? StatusText : $"{StatusText} · {Description}";
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

        public ProfileListItemViewModel(string guid, string name, string? description, string? imagePath, string tagsString, int loadOrder, bool enabled, DateTimeOffset addedUtc, bool isSelected = false)
        {
            Guid = guid;
            Name = name;
            Description = description;
            ImagePath = imagePath;
            TagsString = tagsString;
            LoadOrder = loadOrder;
            Enabled = enabled;
            AddedUtc = addedUtc;
            _isSelected = isSelected;
        }
    }

    public class SettingsPageViewModel : PageViewModel
    {
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

        public bool AutoOpenTagEdit
        {
            get => SettingsService.GetAutoOpenTagEdit();
            set
            {
                SettingsService.SetAutoOpenTagEdit(value);
                OnPropertyChanged(nameof(AutoOpenTagEdit));
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

        public RelayCommand ReloadTagsCommand { get; }
        public RelayCommand OpenModFolderCommand { get; }
        public RelayCommand ResetModFolderCommand { get; }
        public RelayCommand OpenGameDataFolderCommand { get; }
        public RelayCommand DetectGameDataFolderCommand { get; }
        public RelayCommand UpdateAssetMetadataCommand { get; }

        public SettingsPageViewModel()
        {
            Title = "设置";
            ReloadTagsCommand = new RelayCommand(ReloadTags);
            OpenModFolderCommand = new RelayCommand(() => OpenFolder(ModLibraryFolder));
            ResetModFolderCommand = new RelayCommand(() => ModLibraryFolder = SettingsService.GetDefaultModLibraryFolder());
            OpenGameDataFolderCommand = new RelayCommand(OpenGameDataFolder);
            DetectGameDataFolderCommand = new RelayCommand(DetectGameDataFolder);
            UpdateAssetMetadataCommand = new RelayCommand(UpdateAssetMetadata);
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(AutoCleanup));
            OnPropertyChanged(nameof(AutoOpenTagEdit));
            OnPropertyChanged(nameof(EnableLibraryImages));
            OnPropertyChanged(nameof(AutoUpdateAssetMetadata));
            OnPropertyChanged(nameof(AssetMetadataRepository));
            AssetMetadataStatus = BuildInitialAssetMetadataStatus();
            OnPropertyChanged(nameof(ModLibraryFolder));
            OnPropertyChanged(nameof(GameDataFolder));
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

        private static void ReloadTags()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var configDir = System.IO.Path.Combine(baseDir, "config");
                var catalog = TagCatalogService.Instance;
                catalog.RebuildFromCsv(baseDir);
                catalog.Save();
                catalog.Load(configDir);
                System.Windows.MessageBox.Show("Tags reloaded from CSV.", "Tags", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to reload tags: {ex.Message}", "Tags", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async void UpdateAssetMetadata()
        {
            AssetMetadataStatus = "正在更新资产信息...";
            try
            {
                var paths = new StoragePaths(AppDomain.CurrentDomain.BaseDirectory);
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
            var paths = new StoragePaths(AppDomain.CurrentDomain.BaseDirectory);
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

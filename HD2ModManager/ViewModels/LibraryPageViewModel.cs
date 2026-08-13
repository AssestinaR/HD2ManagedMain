using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HD2ModCore.Infrastructure;
using HD2ModCore.Domain;
using HD2ModManager.Models;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    // 作用：管理模组库列表、分组、行选择与库内条目操作。
    public class LibraryPageViewModel : PageViewModel
    {
        private const string SelectionScope = "Library";
        private readonly ModLibraryService _library;
        private readonly DerivedStateCoordinator? _derivedState;
        private readonly ProfileService? _profiles;
        private readonly NotificationService? _notifications;
        private readonly SelectionCoordinator? _selection;
        private bool _hideSelectedProfileMembers;
        private readonly ObservableCollection<string> _selectedGuids = new();
        private readonly Dictionary<string, ModUserStatus> _userStatuses = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _thumbnailSources = new(StringComparer.OrdinalIgnoreCase);
        private string? _selectionAnchorGuid;
        private CancellationTokenSource? _searchCancellation;
        private CancellationTokenSource? _thumbnailCancellation;
        private int _lifecycleVersion;
        private bool _disposed;

        public BulkObservableCollection<ModCardViewModel> Items { get; } = new();
        public bool HasItems => Items.Count != 0;

        private string _query = string.Empty;
        public string Query
        {
            get => _query;
            set
            {
                if (string.Equals(_query, value, StringComparison.Ordinal)) return;
                _query = value;
                OnPropertyChanged();
                QueueSearchRefresh();
            }
        }
        public string ItemCountText => $"显示 {Items.Count} / {_library.All().Count()} 个 Mod";
        public string EmptyMessage => _hideSelectedProfileMembers && _profiles?.SelectedProfile is not null ? "所有 Mod 都已加入此配置。" : "模组库中没有可显示的 Mod。";
        private bool _showOnlyOutdated;
        public bool ShowOnlyOutdated
        {
            get => _showOnlyOutdated;
            set
            {
                if (!SetField(ref _showOnlyOutdated, value)) return;
                OnPropertyChanged(nameof(OutdatedFilterText));
                Refresh();
            }
        }
        public string OutdatedFilterText => ShowOnlyOutdated ? "显示全部" : "显示过时";

        public RelayCommand RemoveModCommand { get; }
        public RelayCommand ToggleSelectionCommand { get; }
        public RelayCommand AddToProfileCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand EditDescriptionCommand { get; }
        public RelayCommand EditImageCommand { get; }
        public RelayCommand RemoveCommand { get; }
        private bool _isCompact = true;
        public bool IsCompact { get => _isCompact; set { _isCompact = value; OnPropertyChanged(nameof(IsCompact)); } }

        public LibraryPageViewModel(ModLibraryService library, DerivedStateCoordinator? derivedState = null, SelectionCoordinator? selection = null, ProfileService? profiles = null, NotificationService? notifications = null, bool hideSelectedProfileMembers = false)
        {
            Title = "模组库";
            _library = library;
            _derivedState = derivedState;
            _profiles = profiles;
            _notifications = notifications;
            _selection = selection;
            _hideSelectedProfileMembers = hideSelectedProfileMembers;
            if (_selection != null) _selection.SelectionChanged += OnSelectionChanged;
            if (_profiles != null) _profiles.Changed += OnProfileChanged;
            _library.ModContentFactsChanged += OnLibraryContentFactsChanged;
            RemoveModCommand = new RelayCommand(() => { /* parameter passed via CommandParameter not used here */ });
            ToggleSelectionCommand = new RelayCommand(ToggleSelection);
            AddToProfileCommand = new RelayCommand(parameter => AddToProfile(parameter as ModCardViewModel));
            OpenFolderCommand = new RelayCommand(parameter => OpenFolder(parameter as ModCardViewModel));
            RenameCommand = new AsyncRelayCommand(parameter => RenameModAsync(parameter as ModCardViewModel, parameter is string name ? name : string.Empty));
            EditDescriptionCommand = new AsyncRelayCommand(parameter => UpdateDescriptionAsync(parameter as ModCardViewModel, parameter is string description ? description : string.Empty));
            EditImageCommand = new RelayCommand(parameter => _ = UpdateIconAsync(parameter as ModCardViewModel, parameter is string path ? path : string.Empty));
            RemoveCommand = new RelayCommand(parameter => RemoveMod(parameter as ModCardViewModel));
            QueueStatusRefresh();
            QueueThumbnailRefresh();
        }

        public LibraryPageViewModel(ModLibraryService library, SelectionCoordinator? selection, ProfileService? profiles, NotificationService? notifications = null)
            : this(library, null, selection, profiles, notifications)
        {
        }

        public void SetProfileCompanionVisible(bool visible)
        {
            if (_hideSelectedProfileMembers == visible) return;
            _hideSelectedProfileMembers = visible;
            Refresh();
        }

        public void SelectRow(ModCardViewModel card, ModifierKeys modifiers)
        {
            var allCards = Items.ToList();
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && !string.IsNullOrWhiteSpace(_selectionAnchorGuid))
            {
                var anchorIndex = allCards.FindIndex(c => string.Equals(c.Mod.Guid, _selectionAnchorGuid, System.StringComparison.OrdinalIgnoreCase));
                var targetIndex = allCards.FindIndex(c => string.Equals(c.Mod.Guid, card.Mod.Guid, System.StringComparison.OrdinalIgnoreCase));
                if (anchorIndex >= 0 && targetIndex >= 0)
                {
                    _selectedGuids.Clear();
                    foreach (var selected in allCards.Skip(System.Math.Min(anchorIndex, targetIndex)).Take(System.Math.Abs(anchorIndex - targetIndex) + 1))
                    {
                        _selectedGuids.Add(selected.Mod.Guid);
                    }
                }
            }
            else if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (!_selectedGuids.Remove(card.Mod.Guid)) _selectedGuids.Add(card.Mod.Guid);
                _selectionAnchorGuid = card.Mod.Guid;
            }
            else
            {
                _selectedGuids.Clear();
                _selectedGuids.Add(card.Mod.Guid);
                _selectionAnchorGuid = card.Mod.Guid;
            }

            var selectedInDisplayOrder = Items
                .Where(item => _selectedGuids.Contains(item.Mod.Guid, StringComparer.OrdinalIgnoreCase))
                .Select(item => item.Mod.Guid)
                .ToList();
            _selection?.Replace(SelectionScope, selectedInDisplayOrder);
            RefreshSelectionFlags();
        }

        public bool RenameMod(ModCardViewModel? card, string newName)
        {
            if (card == null || string.IsNullOrWhiteSpace(newName) || newName == card.Mod.Name) return false;
            var ok = _library.Rename(card.Mod.Guid, newName);
            if (ok) _notifications?.Show($"已重命名：{newName.Trim()}");
            Refresh();
            return ok;
        }

        public async Task RenameModAsync(ModCardViewModel? card, string newName)
        {
            if (card == null || string.IsNullOrWhiteSpace(newName) || newName == card.Mod.Name) return;
            try
            {
                if (await _library.RenameAsync(card.Mod.Guid, newName))
                    _notifications?.Show($"已重命名：{newName.Trim()}");
                Refresh();
            }
            catch (System.Exception ex) { _notifications?.Show($"重命名失败：{ex.Message}", NotificationLevel.Error); }
        }

        public void UpdateDescription(ModCardViewModel? card, string? description)
        {
            if (card == null) return;
            card.Mod.Description = description ?? string.Empty;
            _library.Add(card.Mod);
            _library.Save();
            _notifications?.Show($"已更新备注：{card.Mod.Name}");
            Refresh();
        }

        public async Task UpdateDescriptionAsync(ModCardViewModel? card, string? description)
        {
            if (card == null) return;
            try
            {
                card.Mod.Description = description ?? string.Empty;
                if (await _library.AddAsync(card.Mod))
                    _notifications?.Show($"已更新备注：{card.Mod.Name}");
                Refresh();
            }
            catch (System.Exception ex) { _notifications?.Show($"更新备注失败：{ex.Message}", NotificationLevel.Error); }
        }

        public void UpdateIcon(ModCardViewModel? card, string sourceImagePath)
            => _ = UpdateIconAsync(card, sourceImagePath);

        private async Task UpdateIconAsync(ModCardViewModel? card, string sourceImagePath)
        {
            if (card == null || string.IsNullOrWhiteSpace(sourceImagePath)) return;
            var modDir = _library.ResolveAbsolutePath(card.Mod.SourcePath);
            if (string.IsNullOrWhiteSpace(modDir) || !System.IO.Directory.Exists(modDir)) return;
            var destination = System.IO.Path.Combine(modDir, "icon" + System.IO.Path.GetExtension(sourceImagePath).ToLowerInvariant());
            try
            {
                await Task.Run(() => System.IO.File.Copy(sourceImagePath, destination, overwrite: true)).ConfigureAwait(false);
                card.Mod.Image = destination;
                await _library.AddAsync(card.Mod).ConfigureAwait(false);
                RunOnUiThread(() =>
                {
                    if (_disposed) return;
                    _notifications?.Show($"已更新图标：{card.Mod.Name}");
                    Refresh();
                    QueueThumbnailRefresh();
                });
            }
            catch (System.Exception ex)
            {
                RunOnUiThread(() => _notifications?.Show($"更新图标失败：{ex.Message}", NotificationLevel.Error));
            }
        }

        public void Refresh()
        {
            var all = _library.All().ToList();
            if (_hideSelectedProfileMembers && _profiles?.SelectedProfile is not null)
            {
                var selectedIds = _profiles!.SelectedProfile!.Entries.Select(entry => entry.NodeId.Value.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                all = all.Where(mod => !selectedIds.Contains(mod.Guid)).ToList();
            }
            var q = (_query ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                all = all.Where(mod => ModSearchMatcher.IsMatch(mod.Name, mod.Description, _library.GetDerivedData(mod.Guid)?.AssetSummary, q)).ToList();
            }
			if (ShowOnlyOutdated)
			{
				all = all.Where(mod => _library.GetDerivedData(mod.Guid)?.UnitCompatibility.IsOutdated == true).ToList();
			}
            var cards = all
                .OrderBy(mod => mod.Name, System.StringComparer.CurrentCultureIgnoreCase)
                .Select(mod =>
                {
                    var derived = _library.GetDerivedData(mod.Guid);
                    _userStatuses.TryGetValue(mod.Guid, out var status);
                    _thumbnailSources.TryGetValue(mod.Guid, out var thumbnailSource);
                    return new ModCardViewModel(mod, IsSelected(mod.Guid), derived?.AssetSummary, derived?.UnitCompatibility, status, thumbnailSource);
                })
                .ToList();
            Items.ReplaceWith(cards);
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(ItemCountText));
        }

        public void RefreshFromShell() => QueueStatusRefresh();

        private async void QueueSearchRefresh()
        {
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = new CancellationTokenSource();
            var cancellationToken = _searchCancellation.Token;
            try
            {
                await Task.Delay(180, cancellationToken);
                if (!_disposed && !cancellationToken.IsCancellationRequested && _searchCancellation?.Token == cancellationToken) Refresh();
            }
            catch (OperationCanceledException) { }
        }

        private void RefreshUserStatuses()
        {
            if (_profiles is null || _derivedState is null) return;
            var statuses = _derivedState.ProjectStatuses(_profiles.SelectedProfileId);
            _userStatuses.Clear();
            foreach (var pair in statuses) _userStatuses[pair.Key.Value.ToString("N")] = pair.Value;
        }

        private void QueueStatusRefresh()
        {
            if (_disposed) return;
            RefreshUserStatuses();
            Refresh();
        }

        private async void QueueThumbnailRefresh()
        {
            var version = _lifecycleVersion;
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _thumbnailCancellation = cancellation;
            var cancellationToken = cancellation.Token;
            try
            {
                // 先固定本次刷新快照，避免异步请求期间库同步修改底层字典。
                var mods = await Task.Run(() => _library.All().ToList(), cancellationToken).ConfigureAwait(false);
                foreach (var mod in mods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await Task.Run(
                        () => _library.RequestThumbnailAsync(mod.Guid, "Library", cancellationToken: cancellationToken).AsTask(),
                        cancellationToken).ConfigureAwait(false);
                    if (result.Data is { } facts)
                    {
                        await ThumbnailService.EnsureThumbnailAsync(facts, 72, cancellationToken);
                        _thumbnailSources[mod.Guid] = facts.SourcePath;
                    }
                }
                if (IsCurrentThumbnailRefresh(version, cancellation))
                    RunOnUiThread(Refresh);
            }
            catch (OperationCanceledException) { }
        }

        private void OnSelectionChanged(object? sender, EventArgs e) => SyncSelectionFromCoordinator();

        private void OnProfileChanged(object? sender, EventArgs e) => RunOnUiThread(QueueStatusRefresh);

        private void OnLibraryContentFactsChanged(object? sender, ModContentFactsChangedEventArgs e)
            => RunOnUiThread(RefreshAndQueueThumbnailRefresh);

        private void RefreshAndQueueThumbnailRefresh()
        {
            if (_disposed) return;
            Refresh();
            QueueThumbnailRefresh();
        }

        private bool IsCurrentThumbnailRefresh(int version, CancellationTokenSource cancellation)
            => !_disposed && version == _lifecycleVersion && ReferenceEquals(_thumbnailCancellation, cancellation) && !cancellation.IsCancellationRequested;

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) action();
            else _ = dispatcher.InvokeAsync(action);
        }

        private async void RemoveMod(ModCardViewModel? card)
        {
            if (card == null) return;
            var confirm = System.Windows.MessageBox.Show($"确定删除 Mod“{card.Mod.Name}”？\n这会同时删除库中的已存储文件。", "删除 Mod", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
            try
            {
                ThumbnailService.CancelPendingGeneration();
                await _library.RemoveAsync(card.Mod.Guid);
                _notifications?.Show($"已删除：{card.Mod.Name}");
            }
            catch (System.Exception ex)
            {
                _notifications?.Show($"删除失败：{ex.Message}", NotificationLevel.Error);
            }
            Refresh();
        }

        private void AddToProfile(ModCardViewModel? card)
        {
            if (card == null || _profiles == null) return;
            if (card.Mod.IsDecoration)
            {
                _notifications?.Show("装饰 Mod 不能加入配置；合并器接入后将在目标主 Mod 中启用。", NotificationLevel.Info);
                return;
            }
            if (System.Windows.Application.Current?.MainWindow?.DataContext is ShellViewModel shell)
            {
                _ = shell.AddModToSelectedProfileAsync(card.Mod.Guid, card.Mod.Name);
            }
        }

        private void OpenFolder(ModCardViewModel? card)
        {
            if (card == null) return;
            try
            {
                var abs = _library.ResolveAbsolutePath(card.Mod.SourcePath);
                if (!System.IO.Directory.Exists(abs)) System.IO.Directory.CreateDirectory(abs);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = abs, UseShellExecute = true });
            }
            catch { }
        }

        private void ToggleSelection(object? parameter)
        {
            if (parameter is not ModCardViewModel card) return;
            if (!_selectedGuids.Remove(card.Mod.Guid)) _selectedGuids.Add(card.Mod.Guid);
            _selection?.Replace(SelectionScope, _selectedGuids);
            RefreshSelectionFlags();
        }

        private bool IsSelected(string guid) => _selectedGuids.Any(id => string.Equals(id, guid, System.StringComparison.OrdinalIgnoreCase));

        private void SyncSelectionFromCoordinator()
        {
            if (_selection == null) return;
            _selectedGuids.Clear();
            if (string.Equals(_selection.Scope, SelectionScope, System.StringComparison.OrdinalIgnoreCase))
            {
                foreach (var id in _selection.SelectedIds) _selectedGuids.Add(id);
            }
            RefreshSelectionFlags();
        }

        private void RefreshSelectionFlags()
        {
            foreach (var card in Items)
            {
                card.IsSelected = IsSelected(card.Mod.Guid);
            }
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifecycleVersion++;
            _selection?.SelectionChanged -= OnSelectionChanged;
            if (_profiles != null) _profiles.Changed -= OnProfileChanged;
            _library.ModContentFactsChanged -= OnLibraryContentFactsChanged;
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = null;
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation?.Dispose();
            _thumbnailCancellation = null;
        }
    }

    public class ModCardViewModel : BaseViewModel
    {
        public HD2ModManager.Models.ModEntity Mod { get; }
        private string? _thumbnailSourcePath;
        public ModAssetSummary? AssetSummary { get; }
        public string Name => Mod.Name;
        public string AssetSummaryText => ModAssetSummaryFormatter.Format(AssetSummary);
        public string? ImagePath => ThumbnailService.GetExistingThumbnailPath(_thumbnailSourcePath, 72);
        public bool ShowImage => true;
        public string? Description => Mod.Description;
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
        public ModUserStatus? UserStatus { get; }
        public ModUnitCompatibilityReport? UnitCompatibility { get; }
        public bool IsModelOutdated => UnitCompatibility?.IsOutdated == true;
        public string ModelCompatibilitySummary => UnitCompatibility?.Summary ?? "模型版本尚未检测。";
        public string UserStatusTitle => UserStatus?.Title ?? "状态未知";
        public string UserStatusSummary => UserStatus?.Summary ?? "正在读取状态。";
        public bool HasUserStatus => UserStatus is not null;

        public void SetThumbnailSource(string? sourcePath)
        {
            if (string.Equals(_thumbnailSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                OnPropertyChanged(nameof(ImagePath));
                return;
            }

            _thumbnailSourcePath = sourcePath;
            OnPropertyChanged(nameof(ImagePath));
        }

        public ModCardViewModel(HD2ModManager.Models.ModEntity mod, bool isSelected = false, ModAssetSummary? assetSummary = null, ModUnitCompatibilityReport? unitCompatibility = null, ModUserStatus? userStatus = null, string? thumbnailSourcePath = null)
        {
            Mod = mod;
            _thumbnailSourcePath = thumbnailSourcePath ?? mod.Image;
            AssetSummary = assetSummary;
			UnitCompatibility = unitCompatibility;
            UserStatus = userStatus;
            _isSelected = isSelected;
        }

    }

}

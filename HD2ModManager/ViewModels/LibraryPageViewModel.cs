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
        private string? _selectionAnchorGuid;
        private CancellationTokenSource? _searchCancellation;
        private CancellationTokenSource? _thumbnailCancellation;

        public BulkObservableCollection<ModCardViewModel> Items { get; } = new();

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

        public RelayCommand RefreshCommand { get; }
        public RelayCommand RemoveModCommand { get; }
        public RelayCommand ToggleSelectionCommand { get; }
        public RelayCommand AddToProfileCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand RenameCommand { get; }
        public RelayCommand EditDescriptionCommand { get; }
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
            if (_selection != null) _selection.SelectionChanged += (_, _) => SyncSelectionFromCoordinator();
            if (_profiles != null) _profiles.Changed += (_, _) => QueueStatusRefresh();
            if (_derivedState != null) _derivedState.SnapshotChanged += (_, _) => RunOnUiThread(QueueStatusRefresh);
            RefreshCommand = new RelayCommand(Refresh);
            RemoveModCommand = new RelayCommand(() => { /* parameter passed via CommandParameter not used here */ });
            ToggleSelectionCommand = new RelayCommand(ToggleSelection);
            AddToProfileCommand = new RelayCommand(parameter => AddToProfile(parameter as ModCardViewModel));
            OpenFolderCommand = new RelayCommand(parameter => OpenFolder(parameter as ModCardViewModel));
            RenameCommand = new RelayCommand(_ => { });
            EditDescriptionCommand = new RelayCommand(_ => { });
            EditImageCommand = new RelayCommand(_ => { });
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

            _selection?.Replace(SelectionScope, _selectedGuids);
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

        public void UpdateDescription(ModCardViewModel? card, string? description)
        {
            if (card == null) return;
            card.Mod.Description = description ?? string.Empty;
            _library.Add(card.Mod);
            _library.Save();
            _notifications?.Show($"已更新备注：{card.Mod.Name}");
            Refresh();
        }

        public void UpdateIcon(ModCardViewModel? card, string sourceImagePath)
        {
            if (card == null || string.IsNullOrWhiteSpace(sourceImagePath)) return;
            var modDir = _library.ResolveAbsolutePath(card.Mod.SourcePath);
            if (string.IsNullOrWhiteSpace(modDir) || !System.IO.Directory.Exists(modDir)) return;
            var destination = System.IO.Path.Combine(modDir, "icon" + System.IO.Path.GetExtension(sourceImagePath).ToLowerInvariant());
            System.IO.File.Copy(sourceImagePath, destination, overwrite: true);
            card.Mod.Image = destination;
            _notifications?.Show($"已更新图标：{card.Mod.Name}");
            Refresh();
            QueueThumbnailRefresh();
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
            var cards = all
                .OrderBy(mod => mod.Name, System.StringComparer.CurrentCultureIgnoreCase)
                .Select(mod =>
                {
                    var derived = _library.GetDerivedData(mod.Guid);
                    _userStatuses.TryGetValue(mod.Guid, out var status);
                    return new ModCardViewModel(mod, IsSelected(mod.Guid), derived?.AssetSummary, status);
                })
                .ToList();
            Items.ReplaceWith(cards);
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(ItemCountText));
        }

        private async void QueueSearchRefresh()
        {
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = new CancellationTokenSource();
            var cancellationToken = _searchCancellation.Token;
            try
            {
                await Task.Delay(180, cancellationToken);
                if (!cancellationToken.IsCancellationRequested) Refresh();
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
            RefreshUserStatuses();
            Refresh();
        }

        private async void QueueThumbnailRefresh()
        {
            if (!SettingsService.GetEnableLibraryImages()) return;
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation?.Dispose();
            _thumbnailCancellation = new CancellationTokenSource();
            try
            {
                var generated = await ThumbnailService.EnsureThumbnailsAsync(_library.All().Select(mod => mod.Image), 72, _thumbnailCancellation.Token);
                if (generated && !_thumbnailCancellation.IsCancellationRequested) Refresh();
            }
            catch (OperationCanceledException) { }
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) action();
            else _ = dispatcher.InvokeAsync(action);
        }

        private void RemoveMod(ModCardViewModel? card)
        {
            if (card == null) return;
            var confirm = System.Windows.MessageBox.Show($"确定删除 Mod“{card.Mod.Name}”？\n这会同时删除库中的已存储文件。", "删除 Mod", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
            try
            {
                ThumbnailService.CancelPendingGeneration();
                _library.Remove(card.Mod.Guid);
                _library.Save();
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
        public ModAssetSummary? AssetSummary { get; }
        public string Name => Mod.Name;
        public string AssetSummaryText => ModAssetSummaryFormatter.Format(AssetSummary);
        public string? ImagePath => ThumbnailService.GetExistingThumbnailPath(Mod.Image, 72);
        public bool ShowImage => SettingsService.GetEnableLibraryImages();
        public string? Description => Mod.Description;
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
        public ModUserStatus? UserStatus { get; }
        public string UserStatusTitle => UserStatus?.Title ?? "状态未知";
        public string UserStatusSummary => UserStatus?.Summary ?? "正在读取状态。";
        public bool HasUserStatus => UserStatus is not null;

        public ModCardViewModel(HD2ModManager.Models.ModEntity mod, bool isSelected = false, ModAssetSummary? assetSummary = null, ModUserStatus? userStatus = null)
        {
            Mod = mod;
            AssetSummary = assetSummary;
            UserStatus = userStatus;
            _isSelected = isSelected;
        }

    }

}

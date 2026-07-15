using System.Collections.ObjectModel;
using System.Linq;
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

        public ObservableCollection<ModCardViewModel> Items { get; } = new();

        private string _query = string.Empty;
        public string Query { get => _query; set { _query = value; Refresh(); } }
        public string EmptyMessage => _hideSelectedProfileMembers && _profiles?.SelectedProfile is not null ? "所有 Mod 都已加入此配置。" : "模组库中没有可显示的 Mod。";

        public RelayCommand RefreshCommand { get; }
        public RelayCommand RemoveModCommand { get; }
        public RelayCommand ToggleSelectionCommand { get; }
        public RelayCommand AddToProfileCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand RepairModCommand { get; }
        public RelayCommand RepairAllOutdatedCommand { get; }
        public RelayCommand RenameCommand { get; }
        public RelayCommand EditDescriptionCommand { get; }
        public RelayCommand EditImageCommand { get; }
        public RelayCommand RemoveCommand { get; }
        private bool _isCompact = true;
        public bool IsCompact { get => _isCompact; set { _isCompact = value; OnPropertyChanged(nameof(IsCompact)); } }

        public LibraryPageViewModel(ModLibraryService library, DerivedStateCoordinator? derivedState = null, SelectionCoordinator? selection = null, ProfileService? profiles = null, NotificationService? notifications = null, bool hideSelectedProfileMembers = false)
        {
            Title = "Library";
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
            RepairModCommand = new RelayCommand(parameter => RepairMod(parameter as ModCardViewModel), parameter => (parameter as ModCardViewModel)?.CanRepair == true);
            RepairAllOutdatedCommand = new RelayCommand(_ => RepairAllOutdated(), _ => Items.Any(i => i.CanRepair));
            RenameCommand = new RelayCommand(_ => { });
            EditDescriptionCommand = new RelayCommand(_ => { });
            EditImageCommand = new RelayCommand(_ => { });
            RemoveCommand = new RelayCommand(parameter => RemoveMod(parameter as ModCardViewModel));
            Refresh();
            QueueStatusRefresh();
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
            Items.Clear();
            foreach (var mod in all.OrderBy(m => m.Name, System.StringComparer.CurrentCultureIgnoreCase))
            {
                var derived = _library.GetDerivedData(mod.Guid);
                _userStatuses.TryGetValue(mod.Guid, out var status);
                Items.Add(new ModCardViewModel(mod, IsSelected(mod.Guid), derived?.AssetSummary, derived?.UnitCompatibility, status));
            }
            RepairAllOutdatedCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(EmptyMessage));
        }

        private async Task RefreshUserStatusesAsync()
        {
            if (_profiles is null || _derivedState is null) return;
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

        private async void RepairMod(ModCardViewModel? card)
        {
            if (card == null || !card.CanRepair) return;
            try
            {
                var result = await _library.RepairModUnitsAsync(card.Mod.Guid);
                _notifications?.Show(result.SummaryText, result.Success ? NotificationLevel.Info : NotificationLevel.Error);
            }
            catch (System.Exception ex)
            {
                _notifications?.Show($"修复失败：{ex.Message}", NotificationLevel.Error);
            }
            Refresh();
        }

        private async void RepairAllOutdated()
        {
            try
            {
                var results = await _library.RepairAllOutdatedUnitsAsync();
                var success = results.Count(r => r.Success);
                var units = results.Sum(r => r.UpdatedUnitCount);
                var failed = results.Count - success;
                var message = failed > 0 ? $"已修复 {units} 个 unit，{failed} 个 Mod 失败。" : $"已修复 {units} 个 unit。";
                _notifications?.Show(message, failed > 0 ? NotificationLevel.Error : NotificationLevel.Info);
            }
            catch (System.Exception ex)
            {
                _notifications?.Show($"批量修复失败：{ex.Message}", NotificationLevel.Error);
            }
            Refresh();
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
            if (_profiles.AddModToSelected(card.Mod.Guid))
            {
                _notifications?.Show($"已加入正在编辑的配置：{card.Mod.Name}");
            }
            else
            {
                _notifications?.Show("无法加入配置，可能尚未选择配置或该 Mod 已存在。", NotificationLevel.Info);
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
    }

    public class ModCardViewModel : BaseViewModel
    {
        public HD2ModManager.Models.ModEntity Mod { get; }
        public ModAssetSummary? AssetSummary { get; }
        public ModUnitCompatibilityReport? UnitCompatibility { get; }
        public string Name => Mod.Name;
        public string AssetSummaryText => ModAssetSummaryFormatter.Format(AssetSummary);
        public string? ImagePath => Mod.Image;
        public string? Description => Mod.Description;
        public bool HasCompatibilityBadge => UnitCompatibility?.HasHighConfidenceOutdated == true;
        public bool CanRepair => UnitCompatibility?.CanRepair == true;
        public string CompatibilityBadgeText => UnitCompatibility?.BadgeText ?? string.Empty;
        public string CompatibilityTooltip => UnitCompatibility is null
            ? string.Empty
            : string.Join("\n", new[]
            {
                UnitCompatibility.SummaryText,
                UnitCompatibility.Issues.FirstOrDefault(i => i.IsHighConfidenceOutdated)?.Message ?? UnitCompatibility.Issues.FirstOrDefault()?.Message ?? string.Empty,
                UnitCompatibility.CanRepair ? "可以执行一键修复。" : string.Empty
            }.Where(s => !string.IsNullOrWhiteSpace(s)));
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
        public ModUserStatus? UserStatus { get; }
        public string UserStatusTitle => UserStatus?.Title ?? "状态未知";
        public string UserStatusSummary => UserStatus?.Summary ?? "正在读取状态。";
        public bool HasUserStatus => UserStatus is not null;

        public ModCardViewModel(HD2ModManager.Models.ModEntity mod, bool isSelected = false, ModAssetSummary? assetSummary = null, ModUnitCompatibilityReport? unitCompatibility = null, ModUserStatus? userStatus = null)
        {
            Mod = mod;
            AssetSummary = assetSummary;
            UnitCompatibility = unitCompatibility;
            UserStatus = userStatus;
            _isSelected = isSelected;
        }

    }

}

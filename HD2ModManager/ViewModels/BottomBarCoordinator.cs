using System;
using System.Collections.Generic;
using System.Linq;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    // 作用：集中派生多选操作与临时编辑控件，供窗口底部上下文操作栏统一呈现。
    public sealed class BottomBarCoordinator : BaseViewModel
    {
        private readonly SelectionCoordinator _selection;
        private readonly ModLibraryService _library;
        private readonly ProfileService _profiles;
        private readonly NotificationService _notifications;
        private readonly Action _refresh;
        private bool _isLibraryProfileCompanionVisible;
        private string? _editModId;
        private string? _editMode;
        private string _editText = string.Empty;
        private string? _selectedProfile;
        private bool _suppressSelectionCancel;

        public BottomBarCoordinator(SelectionCoordinator selection, ModLibraryService library, ProfileService profiles, NotificationService notifications, Action refresh)
        {
            _selection = selection;
            _library = library;
            _profiles = profiles;
            _notifications = notifications;
            _refresh = refresh;
            ConfirmEditCommand = new RelayCommand(ConfirmEdit);
            CancelEditCommand = new RelayCommand(CancelEdit);
            BeginMoveCommand = new RelayCommand(_ => BeginMove());
            BeginInsertCommand = new RelayCommand(_ => BeginInsert());
            _selection.SelectionChanged += (_, _) =>
            {
                if (!_suppressSelectionCancel) CancelEdit();
                if (_suppressSelectionCancel) return;
                RefreshState();
            };
        }

        public RelayCommand ConfirmEditCommand { get; }
        public RelayCommand CancelEditCommand { get; }
        public RelayCommand BeginMoveCommand { get; }
        public RelayCommand BeginInsertCommand { get; }
        public event EventHandler? StructureChanged;
        public bool HasSelection => _selection.HasSelection;
        public bool HasTemporaryEditor => !string.IsNullOrWhiteSpace(_editMode);
        public bool IsPositionEditor => _editMode is "Move" or "Insert";
        public bool HasContent => HasSelection || HasTemporaryEditor;
        public bool IsLibrarySelection => HasSelection && string.Equals(_selection.Scope, "Library", StringComparison.OrdinalIgnoreCase);
        public bool IsProfileSelection => HasSelection && string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase);
        public bool ShowAddToProfile => IsLibrarySelection && _isLibraryProfileCompanionVisible;
        public bool ShowDelete => IsLibrarySelection;
        public bool ShowRemove => IsProfileSelection;
        public bool ShowMove => IsProfileSelection;
        public bool ShowInsert => IsLibrarySelection;
        public string SelectionSummary => _selection.Summary;
        public string EditLabel => _editMode is "Move" or "Insert" ? string.Empty
            : _editMode == "CreateProfile" ? "新建配置："
            : _editMode == "RenameProfile" ? "重命名配置："
            : string.Equals(_editMode, "Description", StringComparison.Ordinal) ? "修改备注：" : "修改名称：";
        public string PositionLabel => _editMode == "Move" ? "移动到："
            : _editMode == "Insert" ? "插入到：" : string.Empty;
        public string PositionHint => _editMode == "Move"
            ? $"允许范围：1..{_profiles.SelectedProfile?.Entries.Count ?? 0}"
            : _editMode == "Insert"
                ? $"允许范围：1..{((_profiles.SelectedProfile?.Entries.Count ?? 0) + 1)}"
                : string.Empty;
        public string EditConfirmText => "确定";
        public bool IsProfileSwitchEditor => _editMode == "SwitchProfile";
        public bool IsTextEditor => HasTemporaryEditor && !IsProfileSwitchEditor;
        public IEnumerable<string> ProfileOptions => _profiles.All().Select(profile => profile.Name);
        public string? SelectedProfile
        {
            get => _selectedProfile;
            set => SetField(ref _selectedProfile, value);
        }
        public string EditText
        {
            get => _editText;
            set
            {
                if ((_editMode == "Move" || _editMode == "Insert") && int.TryParse(value, out var number))
                {
                    var max = _editMode == "Move" ? _profiles.SelectedProfile?.Entries.Count ?? 0 : (_profiles.SelectedProfile?.Entries.Count ?? 0) + 1;
                    value = Math.Clamp(number, 1, Math.Max(1, max)).ToString();
                }
                SetField(ref _editText, value);
            }
        }

        public void SetLibraryProfileCompanionVisible(bool visible)
        {
            if (_isLibraryProfileCompanionVisible == visible) return;
            _isLibraryProfileCompanionVisible = visible;
            OnPropertyChanged(nameof(ShowAddToProfile));
        }

        public void BeginNameEdit(string modId, string currentValue) => BeginEdit(modId, "Name", currentValue);
        public void BeginDescriptionEdit(string modId, string currentValue) => BeginEdit(modId, "Description", currentValue);
        public void BeginCreateProfile() => BeginEdit(null, "CreateProfile", string.Empty);
        public void BeginRenameProfile() => BeginEdit(null, "RenameProfile", _profiles.SelectedKey ?? string.Empty);
        public void BeginSwitchProfile()
        {
            _editModId = null;
            _editMode = "SwitchProfile";
            SelectedProfile = _profiles.SelectedKey;
            _editText = string.Empty;
            RefreshState();
        }

        public void BeginMove()
        {
            if (!IsProfileSelection || _profiles.SelectedProfile is null) return;
            BeginPositionEdit("Move", _selection.SelectedIds);
        }

        public void BeginInsert()
        {
            if (!IsLibrarySelection || _profiles.SelectedProfile is null) return;
            BeginPositionEdit("Insert", _selection.SelectedIds);
        }

        public void CancelEdit()
        {
            if (!HasTemporaryEditor) return;
            EndEditWithoutRefresh();
            RefreshState();
        }

        private void EndEditWithoutRefresh()
        {
            _editModId = null;
            _editMode = null;
            _editText = string.Empty;
            SelectedProfile = null;
            OnPropertyChanged(nameof(EditText));
        }

        private void BeginEdit(string? modId, string mode, string currentValue)
        {
            _editModId = modId;
            _editMode = mode;
            _editText = currentValue ?? string.Empty;
            OnPropertyChanged(nameof(EditText));
            RefreshState();
        }

        private void BeginPositionEdit(string mode, IEnumerable<string> ids)
        {
            var selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var profileOrder = _profiles.SelectedProfile is { } profile
                ? _profiles.GetSortedEntries(profile).Select(entry => entry.NodeId.Value.ToString("N"))
                : Enumerable.Empty<string>();
            var orderedIds = profileOrder.Where(selected.Contains).Concat(ids.Where(id => selected.Contains(id))).Distinct(StringComparer.OrdinalIgnoreCase);
            _editModId = string.Join("|", orderedIds);
            _editMode = mode;
            _editText = string.Empty;
            _suppressSelectionCancel = true;
            try { _selection.Clear(); }
            finally { _suppressSelectionCancel = false; }
            OnPropertyChanged(nameof(EditText));
            RefreshState();
        }

        private void ConfirmEdit(object? _)
        {
            if (_editMode == "SwitchProfile")
            {
                if (!string.IsNullOrWhiteSpace(SelectedProfile)) _profiles.Select(SelectedProfile);
                EndEditWithoutRefresh();
                RefreshState();
                _refresh();
                return;
            }
            if (_editMode == "CreateProfile")
            {
                _profiles.CreateNew(EditText);
                EndEditWithoutRefresh();
                RefreshState();
                _refresh();
                return;
            }
            if (_editMode == "RenameProfile")
            {
                var oldName = _profiles.SelectedKey;
                if (!string.IsNullOrWhiteSpace(oldName) && !string.IsNullOrWhiteSpace(EditText))
                    _profiles.Rename(oldName, EditText.Trim());
                EndEditWithoutRefresh();
                RefreshState();
                _refresh();
                return;
            }
            if (_editMode is "Move" or "Insert")
            {
                ConfirmPositionEdit();
                return;
            }
            if (string.IsNullOrWhiteSpace(_editModId) || string.IsNullOrWhiteSpace(_editMode)) return;
            var mod = _library.Get(_editModId);
            if (mod is null) { CancelEdit(); return; }
            if (string.Equals(_editMode, "Name", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(EditText)) return;
                if (!_library.Rename(mod.Guid, EditText.Trim())) return;
                _notifications.Show($"已重命名：{EditText.Trim()}");
            }
            else
            {
                mod.Description = EditText;
                _library.Add(mod);
                _library.Save();
                _notifications.Show($"已更新备注：{mod.Name}");
            }
            CancelEdit();
            _refresh();
        }

        private void ConfirmPositionEdit()
        {
            if (!int.TryParse(EditText?.Trim(), out var requested)) return;
            var ids = (_editModId ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
            var profile = _profiles.SelectedProfile;
            if (profile is null || ids.Count == 0) { CancelEdit(); return; }
            var ordered = _profiles.GetSortedEntries(profile).Select(entry => entry.NodeId.Value.ToString("N")).ToList();
            if (_editMode == "Move")
            {
                var max = ordered.Count;
                var target = Math.Clamp(requested, 1, max);
                var selected = ids.Where(ordered.Contains).ToList();
                ordered.RemoveAll(selected.Contains);
                target = Math.Min(target, ordered.Count + 1);
                ordered.InsertRange(target - 1, selected);
            }
            else
            {
                var target = Math.Clamp(requested, 1, ordered.Count + 1);
                var additions = ids.Where(id => !ordered.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
                ordered.InsertRange(target - 1, additions);
            }
            if (_profiles.ReplaceSelectedEntries(ordered))
            {
                _notifications.Show(_editMode == "Move" ? "已移动配置项。" : "已插入配置项。");
                CancelEdit();
                _refresh();
            }
        }

        private void RefreshState()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasTemporaryEditor));
            OnPropertyChanged(nameof(IsPositionEditor));
            OnPropertyChanged(nameof(IsProfileSwitchEditor));
            OnPropertyChanged(nameof(IsTextEditor));
            OnPropertyChanged(nameof(ProfileOptions));
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(IsLibrarySelection));
            OnPropertyChanged(nameof(IsProfileSelection));
            OnPropertyChanged(nameof(ShowAddToProfile));
            OnPropertyChanged(nameof(ShowDelete));
            OnPropertyChanged(nameof(ShowRemove));
            OnPropertyChanged(nameof(ShowMove));
            OnPropertyChanged(nameof(ShowInsert));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(EditLabel));
            OnPropertyChanged(nameof(PositionLabel));
            OnPropertyChanged(nameof(PositionHint));
            StructureChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
using System;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    // 作用：集中派生多选操作与临时编辑控件，供窗口底部上下文操作栏统一呈现。
    public sealed class BottomBarCoordinator : BaseViewModel
    {
        private readonly SelectionCoordinator _selection;
        private readonly ModLibraryService _library;
        private readonly NotificationService _notifications;
        private readonly Action _refresh;
        private bool _isLibraryProfileCompanionVisible;
        private string? _editModId;
        private string? _editMode;
        private string _editText = string.Empty;

        public BottomBarCoordinator(SelectionCoordinator selection, ModLibraryService library, NotificationService notifications, Action refresh)
        {
            _selection = selection;
            _library = library;
            _notifications = notifications;
            _refresh = refresh;
            ConfirmEditCommand = new RelayCommand(ConfirmEdit);
            CancelEditCommand = new RelayCommand(CancelEdit);
            _selection.SelectionChanged += (_, _) =>
            {
                CancelEdit();
                RefreshState();
            };
        }

        public RelayCommand ConfirmEditCommand { get; }
        public RelayCommand CancelEditCommand { get; }
        public bool HasSelection => _selection.HasSelection;
        public bool HasTemporaryEditor => !string.IsNullOrWhiteSpace(_editMode);
        public bool HasContent => HasSelection || HasTemporaryEditor;
        public bool IsLibrarySelection => HasSelection && string.Equals(_selection.Scope, "Library", StringComparison.OrdinalIgnoreCase);
        public bool IsProfileSelection => HasSelection && string.Equals(_selection.Scope, "Profile", StringComparison.OrdinalIgnoreCase);
        public bool ShowAddToProfile => IsLibrarySelection && _isLibraryProfileCompanionVisible;
        public bool ShowDelete => IsLibrarySelection;
        public bool ShowRemove => IsProfileSelection;
        public string SelectionSummary => _selection.Summary;
        public string EditLabel => string.Equals(_editMode, "Description", StringComparison.Ordinal) ? "修改备注：" : "修改名称：";
        public string EditConfirmText => "确定";
        public string EditText { get => _editText; set => SetField(ref _editText, value); }

        public void SetLibraryProfileCompanionVisible(bool visible)
        {
            if (_isLibraryProfileCompanionVisible == visible) return;
            _isLibraryProfileCompanionVisible = visible;
            OnPropertyChanged(nameof(ShowAddToProfile));
        }

        public void BeginNameEdit(string modId, string currentValue) => BeginEdit(modId, "Name", currentValue);
        public void BeginDescriptionEdit(string modId, string currentValue) => BeginEdit(modId, "Description", currentValue);

        public void CancelEdit()
        {
            if (!HasTemporaryEditor) return;
            _editModId = null;
            _editMode = null;
            _editText = string.Empty;
            OnPropertyChanged(nameof(EditText));
            RefreshState();
        }

        private void BeginEdit(string modId, string mode, string currentValue)
        {
            _editModId = modId;
            _editMode = mode;
            _editText = currentValue ?? string.Empty;
            OnPropertyChanged(nameof(EditText));
            RefreshState();
        }

        private void ConfirmEdit(object? _)
        {
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

        private void RefreshState()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasTemporaryEditor));
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(IsLibrarySelection));
            OnPropertyChanged(nameof(IsProfileSelection));
            OnPropertyChanged(nameof(ShowAddToProfile));
            OnPropertyChanged(nameof(ShowDelete));
            OnPropertyChanged(nameof(ShowRemove));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(EditLabel));
        }
    }
}
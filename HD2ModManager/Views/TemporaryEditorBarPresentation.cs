using System.Windows.Input;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：冻结一次底栏展示状态，供双层内容交叉淡化使用。
    public sealed class TemporaryEditorBarPresentation : BaseViewModel
    {
        private readonly BottomBarCoordinator _bottomBar;
        public TemporaryEditorBarPresentation(BottomBarCoordinator bottomBar)
        {
            _bottomBar = bottomBar;
            ConfirmEditCommand = bottomBar.ConfirmEditCommand;
            CancelEditCommand = bottomBar.CancelEditCommand;
            BeginMoveCommand = bottomBar.BeginMoveCommand;
            BeginInsertCommand = bottomBar.BeginInsertCommand;
            bottomBar.PropertyChanged += (_, eventArgs) => OnPropertyChanged(eventArgs.PropertyName);
        }
        public bool HasTemporaryEditor => _bottomBar.HasTemporaryEditor;
        public bool IsPositionEditor => _bottomBar.IsPositionEditor;
        public bool IsTextEditor => _bottomBar.IsTextEditor;
        public bool IsProfileSwitchEditor => _bottomBar.IsProfileSwitchEditor;
        public string EditLabel => _bottomBar.EditLabel;
        public string PositionLabel => _bottomBar.PositionLabel;
        public string PositionHint => _bottomBar.PositionHint;
        public System.Collections.Generic.IEnumerable<string> ProfileOptions => _bottomBar.ProfileOptions;
        public string EditConfirmText => _bottomBar.EditConfirmText;
        public string EditText { get => _bottomBar.EditText; set => _bottomBar.EditText = value; }
        public string? SelectedProfile { get => _bottomBar.SelectedProfile; set => _bottomBar.SelectedProfile = value; }
        public ICommand ConfirmEditCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand BeginMoveCommand { get; }
        public ICommand BeginInsertCommand { get; }
    }
}

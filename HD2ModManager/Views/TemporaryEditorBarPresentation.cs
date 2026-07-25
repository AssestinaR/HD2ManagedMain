using System.Collections.Generic;
using System.Windows.Input;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：冻结一次底栏展示状态，供双层内容交叉淡化使用。
    public sealed class TemporaryEditorBarPresentation
    {
        private readonly BottomBarCoordinator _bottomBar;
        public TemporaryEditorBarPresentation(BottomBarCoordinator bottomBar, ICommand selectionPrimaryCommand, ICommand selectionDeleteCommand, ICommand cancelSelectionCommand)
        {
            _bottomBar = bottomBar;
            SelectionPrimaryCommand = selectionPrimaryCommand;
            SelectionDeleteCommand = selectionDeleteCommand;
            CancelSelectionCommand = cancelSelectionCommand;
            HasTemporaryEditor = bottomBar.HasTemporaryEditor;
            IsPositionEditor = bottomBar.IsPositionEditor;
            IsTextEditor = bottomBar.IsTextEditor;
            IsProfileSwitchEditor = bottomBar.IsProfileSwitchEditor;
            HasSelection = bottomBar.HasSelection;
            ShowAddToProfile = bottomBar.ShowAddToProfile;
            ShowMove = bottomBar.ShowMove;
            ShowInsert = bottomBar.ShowInsert;
            ShowDelete = bottomBar.ShowDelete;
            ShowRemove = bottomBar.ShowRemove;
            EditLabel = bottomBar.EditLabel;
            PositionLabel = bottomBar.PositionLabel;
            PositionHint = bottomBar.PositionHint;
            SelectionSummary = bottomBar.SelectionSummary;
            ProfileOptions = bottomBar.ProfileOptions.ToArray();
            EditConfirmText = bottomBar.EditConfirmText;
            ConfirmEditCommand = bottomBar.ConfirmEditCommand;
            CancelEditCommand = bottomBar.CancelEditCommand;
            BeginMoveCommand = bottomBar.BeginMoveCommand;
            BeginInsertCommand = bottomBar.BeginInsertCommand;
        }
        public bool HasTemporaryEditor { get; }
        public bool IsPositionEditor { get; }
        public bool IsTextEditor { get; }
        public bool IsProfileSwitchEditor { get; }
        public bool HasSelection { get; }
        public bool ShowAddToProfile { get; }
        public bool ShowMove { get; }
        public bool ShowInsert { get; }
        public bool ShowDelete { get; }
        public bool ShowRemove { get; }
        public string EditLabel { get; }
        public string PositionLabel { get; }
        public string PositionHint { get; }
        public string SelectionSummary { get; }
        public IEnumerable<string> ProfileOptions { get; }
        public string EditConfirmText { get; }
        public string EditText { get => _bottomBar.EditText; set => _bottomBar.EditText = value; }
        public string? SelectedProfile { get => _bottomBar.SelectedProfile; set => _bottomBar.SelectedProfile = value; }
        public ICommand ConfirmEditCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand BeginMoveCommand { get; }
        public ICommand BeginInsertCommand { get; }
        public ICommand SelectionPrimaryCommand { get; }
        public ICommand SelectionDeleteCommand { get; }
        public ICommand CancelSelectionCommand { get; }
    }
}

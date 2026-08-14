using System.Windows.Input;

namespace HD2ModManager.ViewModels;

public enum ModListSelectionPolicy
{
    None,
    Single,
    Multiple,
    ReadOnly,
}

public interface IModListSelectable
{
    string SelectionKey { get; }
    bool IsSelected { get; set; }
}

public sealed class ModListSelectionRequestEventArgs(object item, IReadOnlyList<string> selectedKeys, ModifierKeys modifiers) : EventArgs
{
    public object Item { get; } = item;
    public IReadOnlyList<string> SelectedKeys { get; } = selectedKeys;
    public ModifierKeys Modifiers { get; } = modifiers;
}

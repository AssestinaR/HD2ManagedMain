using System.Windows;

namespace HD2ModManager.Views;

// Handles the reverse direction: a selected profile row can be dropped on the library to remove it from that profile.
internal static class ModListExternalProfileRemovalCoordinator
{
    private static WeakReference<ModListPanel>? _target;
    private static ModListPanel? _source;
    private static IReadOnlyList<string>? _selectedKeys;

    public static void RegisterTarget(ModListPanel target) => _target = new WeakReference<ModListPanel>(target);

    public static void UnregisterTarget(ModListPanel target)
    {
        if (_target?.TryGetTarget(out var current) == true && ReferenceEquals(current, target)) _target = null;
        if (ReferenceEquals(_source, target)) Cancel(target);
    }

    public static bool TryBegin(ModListPanel source, IReadOnlyList<string> selectedKeys)
    {
        if (_target is null || !_target.TryGetTarget(out var target) || !target.AllowExternalProfileRemovalDrop || selectedKeys.Count == 0) return false;
        _source = source;
        _selectedKeys = selectedKeys;
        target.SetExternalProfileDropOverlay(true);
        return true;
    }

    public static bool Complete(ModListPanel source, Point screenPoint)
    {
        if (!ReferenceEquals(_source, source)) return false;
        var accepted = false;
        if (_target?.TryGetTarget(out var target) == true)
        {
            target.SetExternalProfileDropOverlay(false);
            if (_selectedKeys is { Count: > 0 } keys && target.ContainsScreenPointForRemoval(screenPoint))
            {
                target.RaiseExternalProfileRemovalRequested(keys);
                accepted = true;
            }
        }
        Clear();
        return accepted;
    }

    public static void Cancel(ModListPanel source)
    {
        if (!ReferenceEquals(_source, source)) return;
        if (_target?.TryGetTarget(out var target) == true) target.SetExternalProfileDropOverlay(false);
        Clear();
    }

    private static void Clear()
    {
        _source = null;
        _selectedKeys = null;
    }
}

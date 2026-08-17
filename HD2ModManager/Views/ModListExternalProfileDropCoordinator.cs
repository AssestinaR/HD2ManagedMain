using System.Windows;

namespace HD2ModManager.Views;

// Coordinates the simple library-to-profile drop without involving WPF's data-object drag pipeline.
internal static class ModListExternalProfileDropCoordinator
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
        if (_target is null || !_target.TryGetTarget(out var target) || !target.AllowExternalProfileDrop || selectedKeys.Count == 0) return false;
        _source = source;
        _selectedKeys = selectedKeys;
        target.SetExternalProfileDropOverlay(true);
        return true;
    }

    public static void Complete(ModListPanel source, Point screenPoint)
    {
        if (!ReferenceEquals(_source, source)) return;
        var keys = _selectedKeys;
        if (_target?.TryGetTarget(out var target) == true)
        {
            target.SetExternalProfileDropOverlay(false);
            if (keys is { Count: > 0 } && target.ContainsScreenPoint(screenPoint))
                target.RaiseExternalProfileDropRequested(keys);
        }
        Clear();
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

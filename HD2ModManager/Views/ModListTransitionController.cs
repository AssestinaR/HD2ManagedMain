using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HD2ModManager.Services;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

// Owns the list-level transition state so collection updates cannot re-enter
// the panel while a previous layout transition is still being rendered.
internal sealed class ModListTransitionController : IDisposable
{
    private readonly FrameworkElement _owner;
    private readonly ItemsControl _itemsList;
    private readonly Canvas _overlay;
    private readonly DispatcherTimer _timer = new();
    private INotifyCollectionChanged? _observedItems;
    private IListTransitionNotifier? _transitionNotifier;
    private ListTransitionBatch? _pendingBatch;
    private Dictionary<string, ListTransitionSnapshot> _before = new(StringComparer.OrdinalIgnoreCase);
    private ListVisualSnapshot? _beforeListSnapshot;
    private Dictionary<string, ListTransitionSnapshot>? _queuedBefore;
    private ListVisualSnapshot? _queuedListSnapshot;
    private ListTransitionBatch? _queuedBatch;
    private bool _hasSnapshot;
    private bool _isScheduled;
    private bool _isPlaying;
    private bool _disposed;

    private static double AnimationSpeed => SettingsService.GetModListAnimationSpeedMultiplier();

    private static TimeSpan Duration(double milliseconds)
        => TimeSpan.FromMilliseconds(milliseconds / AnimationSpeed);

    public ModListTransitionController(FrameworkElement owner, ItemsControl itemsList, Canvas overlay)
    {
        _owner = owner;
        _itemsList = itemsList;
        _overlay = overlay;
        _timer.Tick += OnTimerTick;
    }

    public void Attach(IEnumerable? source)
    {
        DetachSource();
        CancelPendingTransitions();
        _overlay.Children.Clear();
        if (_disposed) return;

        _observedItems = source as INotifyCollectionChanged;
        _transitionNotifier = source as IListTransitionNotifier;
        if (_observedItems is not null) _observedItems.CollectionChanged += OnCollectionChanged;
        if (_transitionNotifier is not null) _transitionNotifier.TransitionStarting += OnTransitionStarting;
    }

    public void Detach()
    {
        DetachSource();
        CancelPendingTransitions();
        _overlay.Children.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        _timer.Tick -= OnTimerTick;
    }

    private void DetachSource()
    {
        if (_observedItems is not null) _observedItems.CollectionChanged -= OnCollectionChanged;
        if (_transitionNotifier is not null) _transitionNotifier.TransitionStarting -= OnTransitionStarting;
        _observedItems = null;
        _transitionNotifier = null;
    }

    private void OnTransitionStarting(object? sender, ListTransitionBatch batch)
    {
        if (_disposed) return;

        if (!batch.Animate)
        {
            // A presentation-only Refresh commonly follows a real transition
            // (for example profile status projection). It must not cancel the
            // pending insert/reorder/remove motion.
            return;
        }

        if (_isPlaying)
        {
            // Keep the first visual state and the latest collection result. A
            // burst of refreshes therefore produces one coherent next motion.
            if (_queuedBefore is null)
            {
                _queuedBefore = CaptureSnapshot();
                _queuedListSnapshot = CaptureListSnapshot();
            }
            _queuedBatch = batch;
            return;
        }

        _pendingBatch = batch;
        if (_hasSnapshot) return;
        _before = CaptureSnapshot();
        _beforeListSnapshot = CaptureListSnapshot();
        _hasSnapshot = true;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed || _pendingBatch?.Animate != true || _isPlaying) return;
        ScheduleAfterLayout();
    }

    private void ScheduleAfterLayout()
    {
        if (_isScheduled || _isPlaying || !_hasSnapshot) return;
        _isScheduled = true;
        _itemsList.LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_isScheduled || _isPlaying) return;
        if (_itemsList.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
            return;

        _isScheduled = false;
        _itemsList.LayoutUpdated -= OnLayoutUpdated;
        _ = _owner.Dispatcher.InvokeAsync(PlayPending, DispatcherPriority.Render);
    }

    private void PlayPending()
    {
        if (_disposed || !_hasSnapshot || !_owner.IsLoaded) return;

        var batch = _pendingBatch;
        _pendingBatch = null;
        var before = _before;
        var beforeListSnapshot = _beforeListSnapshot;
        _before = new Dictionary<string, ListTransitionSnapshot>(StringComparer.OrdinalIgnoreCase);
        _beforeListSnapshot = null;
        _hasSnapshot = false;

        if (batch is { Animate: false }) return;

        var animated = false;
        var afterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _itemsList.Items)
        {
            if (_itemsList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container || !container.IsVisible) continue;
            var key = GetItemKey(item);
            if (key is null) continue;
            afterKeys.Add(key);
            var position = container.TransformToAncestor(_itemsList).Transform(new Point());
            animated |= before.TryGetValue(key, out var old)
                ? AnimateMovedContainer(container, old.Position.Y - position.Y)
                : AnimateAddedContainer(container);
        }

        if (afterKeys.Count == 0 && beforeListSnapshot is not null)
        {
            animated |= AnimateRemovedListSnapshot(beforeListSnapshot);
        }
        else
        {
            foreach (var removed in before.Values.Where(snapshot => !afterKeys.Contains(snapshot.Key)))
                animated |= AnimateRemovedSnapshot(removed);
        }

        if (!animated)
        {
            StartQueuedTransitionIfNeeded();
            return;
        }

        _isPlaying = true;
        _timer.Stop();
        _timer.Interval = Duration(230);
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _isPlaying = false;
        StartQueuedTransitionIfNeeded();
    }

    private void StartQueuedTransitionIfNeeded()
    {
        if (_queuedBefore is null) return;
        _before = _queuedBefore;
        _queuedBefore = null;
        _beforeListSnapshot = _queuedListSnapshot;
        _queuedListSnapshot = null;
        _pendingBatch = _queuedBatch;
        _queuedBatch = null;
        _hasSnapshot = true;
        _ = _owner.Dispatcher.InvokeAsync(PlayPending, DispatcherPriority.Render);
    }

    private void CancelPendingTransitions()
    {
        _timer.Stop();
        _itemsList.LayoutUpdated -= OnLayoutUpdated;
        _overlay.Children.Clear();
        _before.Clear();
        _beforeListSnapshot = null;
        _queuedBefore = null;
        _queuedListSnapshot = null;
        _pendingBatch = null;
        _queuedBatch = null;
        _hasSnapshot = false;
        _isScheduled = false;
        _isPlaying = false;
        ResetVisibleContainers();
    }

    private void ResetVisibleContainers()
    {
        foreach (var item in _itemsList.Items)
        {
            if (_itemsList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container) continue;
            container.BeginAnimation(UIElement.OpacityProperty, null);
            container.Opacity = 1;
            container.RenderTransform = Transform.Identity;
        }
    }

    private Dictionary<string, ListTransitionSnapshot> CaptureSnapshot()
    {
        var snapshots = new Dictionary<string, ListTransitionSnapshot>(StringComparer.OrdinalIgnoreCase);
        if (!_owner.IsLoaded) return snapshots;
        if (_itemsList.Items.Count == 0) return snapshots;

        foreach (var item in _itemsList.Items)
        {
            if (_itemsList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container || !container.IsVisible) continue;
            var key = GetItemKey(item);
            if (key is null) continue;
            var position = container.TransformToAncestor(_itemsList).Transform(new Point());
            if (container.ActualWidth <= 0 || container.ActualHeight <= 0) continue;
            snapshots[key] = new ListTransitionSnapshot(
                key,
                position,
                new Size(container.ActualWidth, container.ActualHeight),
                CaptureContainerBitmap(container));
        }
        return snapshots;
    }

    private ListVisualSnapshot? CaptureListSnapshot()
    {
        if (!_owner.IsLoaded || _itemsList.ActualWidth <= 0 || _itemsList.ActualHeight <= 0) return null;
        var bitmap = CaptureContainerBitmap(_itemsList);
        return bitmap is null ? null : new ListVisualSnapshot(new Size(_itemsList.ActualWidth, _itemsList.ActualHeight), bitmap);
    }

    private static bool AnimateMovedContainer(FrameworkElement container, double deltaY)
    {
        if (Math.Abs(deltaY) < 0.5) return false;
        var transform = new TranslateTransform(0, deltaY);
        container.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Duration(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        return true;
    }

    private static bool AnimateAddedContainer(FrameworkElement container)
    {
        container.BeginAnimation(UIElement.OpacityProperty, null);
        container.Opacity = 0;
        var transform = new TranslateTransform(0, 5);
        container.RenderTransform = transform;
        container.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, Duration(120)));
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Duration(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        return true;
    }

    private bool AnimateRemovedSnapshot(ListTransitionSnapshot snapshot)
    {
        if (snapshot.Bitmap is null) return false;
        var ghost = new Image
        {
            Source = snapshot.Bitmap,
            Width = snapshot.Size.Width,
            Height = snapshot.Size.Height,
            Opacity = 1,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(ghost, snapshot.Position.X);
        Canvas.SetTop(ghost, snapshot.Position.Y);
        _overlay.Children.Add(ghost);
        var animation = new DoubleAnimation(0, Duration(120)) { BeginTime = Duration(20) };
        animation.Completed += (_, _) => _overlay.Children.Remove(ghost);
        ghost.BeginAnimation(UIElement.OpacityProperty, animation);
        return true;
    }

    private bool AnimateRemovedListSnapshot(ListVisualSnapshot snapshot)
    {
        var ghost = new Image
        {
            Source = snapshot.Bitmap,
            Width = snapshot.Size.Width,
            Height = snapshot.Size.Height,
            Opacity = 1,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(ghost, 0);
        Canvas.SetTop(ghost, 0);
        _overlay.Children.Add(ghost);
        var animation = new DoubleAnimation(0, Duration(140)) { BeginTime = Duration(20) };
        animation.Completed += (_, _) => _overlay.Children.Remove(ghost);
        ghost.BeginAnimation(UIElement.OpacityProperty, animation);
        return true;
    }

    private static BitmapSource? CaptureContainerBitmap(FrameworkElement container)
    {
        try
        {
            var width = Math.Max(1, (int)Math.Ceiling(container.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(container.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(container);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetItemKey(object? item)
    {
        if (item is null) return null;
        foreach (var propertyName in new[] { "Guid", "ModId", "Id" })
        {
            if (item.GetType().GetProperty(propertyName)?.GetValue(item)?.ToString() is { Length: > 0 } value) return value;
        }
        var mod = item.GetType().GetProperty("Mod")?.GetValue(item);
        return mod?.GetType().GetProperty("Guid")?.GetValue(mod)?.ToString();
    }

    public void ResetAnimatedState()
    {
        CancelPendingTransitions();
        foreach (var item in _itemsList.Items)
        {
            if (_itemsList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container) continue;
            container.BeginAnimation(UIElement.OpacityProperty, null);
            container.Opacity = 1;
            container.RenderTransform = Transform.Identity;
        }
    }

    private sealed record ListTransitionSnapshot(string Key, Point Position, Size Size, BitmapSource? Bitmap);
    private sealed record ListVisualSnapshot(Size Size, BitmapSource Bitmap);
}

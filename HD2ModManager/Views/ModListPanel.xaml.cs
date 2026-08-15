using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

// Shared visual shell. Host pages retain all Mod-specific commands and transactions.
public partial class ModListPanel : UserControl
{
    private readonly Dictionary<Border, SelectionIndicatorSubscription> _selectionIndicatorSubscriptions = new();
    private INotifyCollectionChanged? _observedItems;
    private IListTransitionNotifier? _transitionNotifier;
    private ListTransitionBatch? _pendingTransitionBatch;
    private Dictionary<string, ListTransitionSnapshot> _beforeTransition = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _transitionTimer = new();
    private bool _transitionScheduled;
    private bool _isTransitionAnimationRunning;
    private string? _selectionAnchorKey;
    private readonly DispatcherTimer _dragAutoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DragCandidate? _dragCandidate;
    private Point _dragStartPoint;
    private Point? _dragPointerOnScreen;
    private ScrollViewer? _dragAutoScrollViewer;
    private int _dragAutoScrollDirection;
    private double _dragAutoScrollStep;
    private bool _isInternalDragActive;
    private InternalDragPayload? _activeInternalDragPayload;
    private Cursor? _previousOverrideCursor;
    private DateTime _dragWheelCooldownUntilUtc;
    private ScrollViewer? _smoothScrollViewer;
    private double _smoothScrollTarget;
    private DateTime _smoothScrollLastFrameUtc;

    public ModListPanel()
    {
        InitializeComponent();
        _transitionTimer.Interval = TimeSpan.FromMilliseconds(230);
        _transitionTimer.Tick += OnTransitionTimerTick;
        _dragAutoScrollTimer.Tick += OnDragAutoScrollTick;
        Loaded += (_, _) => ObserveItemsSource(ItemsSource);
        Unloaded += (_, _) =>
        {
            _transitionTimer.Stop();
            EndInternalDrag();
            StopDragAutoScroll();
            StopSmoothScroll();
            ItemsList.LayoutUpdated -= OnItemsListLayoutUpdated;
            _transitionScheduled = false;
            ObserveItemsSource(null);
        };
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(ModListPanel), new PropertyMetadata(null, OnItemsSourceChanged));
    public static readonly DependencyProperty HeaderTitleProperty = DependencyProperty.Register(nameof(HeaderTitle), typeof(string), typeof(ModListPanel), new PropertyMetadata("模组"));
    public static readonly DependencyProperty HeaderSummaryProperty = DependencyProperty.Register(nameof(HeaderSummary), typeof(string), typeof(ModListPanel), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty EmptyMessageProperty = DependencyProperty.Register(nameof(EmptyMessage), typeof(string), typeof(ModListPanel), new PropertyMetadata("没有可显示的 Mod。"));
    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(nameof(ShowHeader), typeof(bool), typeof(ModListPanel), new PropertyMetadata(true));
    public static readonly DependencyProperty ShowSelectionCheckboxProperty = DependencyProperty.Register(nameof(ShowSelectionCheckbox), typeof(bool), typeof(ModListPanel), new PropertyMetadata(false));
    public static readonly DependencyProperty SelectionPolicyProperty = DependencyProperty.Register(nameof(SelectionPolicy), typeof(ModListSelectionPolicy), typeof(ModListPanel), new PropertyMetadata(ModListSelectionPolicy.None));
    public static readonly DependencyProperty AllowInternalReorderProperty = DependencyProperty.Register(nameof(AllowInternalReorder), typeof(bool), typeof(ModListPanel), new PropertyMetadata(false));
    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(ModListPanel), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(ModListPanel), new PropertyMetadata(ScrollBarVisibility.Auto));
    public static readonly DependencyProperty RowActionsProperty = DependencyProperty.Register(nameof(RowActions), typeof(ModListRowAction), typeof(ModListPanel), new PropertyMetadata(ModListRowAction.None, OnRowActionsChanged));
    public static readonly DependencyProperty SearchActionsTemplateProperty = DependencyProperty.Register(nameof(SearchActionsTemplate), typeof(DataTemplate), typeof(ModListPanel), new PropertyMetadata(null, OnSearchActionsTemplateChanged));
    private static readonly DependencyPropertyKey HasRowActionsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasRowActions), typeof(bool), typeof(ModListPanel), new PropertyMetadata(false));
    public static readonly DependencyProperty HasRowActionsProperty = HasRowActionsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasSearchActionsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasSearchActions), typeof(bool), typeof(ModListPanel), new PropertyMetadata(false));
    public static readonly DependencyProperty HasSearchActionsProperty = HasSearchActionsPropertyKey.DependencyProperty;

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string HeaderTitle { get => (string)GetValue(HeaderTitleProperty); set => SetValue(HeaderTitleProperty, value); }
    public string HeaderSummary { get => (string)GetValue(HeaderSummaryProperty); set => SetValue(HeaderSummaryProperty, value); }
    public string EmptyMessage { get => (string)GetValue(EmptyMessageProperty); set => SetValue(EmptyMessageProperty, value); }
    public bool ShowHeader { get => (bool)GetValue(ShowHeaderProperty); set => SetValue(ShowHeaderProperty, value); }
    public bool ShowSelectionCheckbox { get => (bool)GetValue(ShowSelectionCheckboxProperty); set => SetValue(ShowSelectionCheckboxProperty, value); }
    public ModListSelectionPolicy SelectionPolicy { get => (ModListSelectionPolicy)GetValue(SelectionPolicyProperty); set => SetValue(SelectionPolicyProperty, value); }
    public bool AllowInternalReorder { get => (bool)GetValue(AllowInternalReorderProperty); set => SetValue(AllowInternalReorderProperty, value); }
    public string SearchText { get => (string)GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public ScrollBarVisibility VerticalScrollBarVisibility { get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty); set => SetValue(VerticalScrollBarVisibilityProperty, value); }
    public ModListRowAction RowActions { get => (ModListRowAction)GetValue(RowActionsProperty); set => SetValue(RowActionsProperty, value); }
    public DataTemplate? SearchActionsTemplate { get => (DataTemplate?)GetValue(SearchActionsTemplateProperty); set => SetValue(SearchActionsTemplateProperty, value); }
    public bool HasRowActions => (bool)GetValue(HasRowActionsProperty);
    public bool HasSearchActions => (bool)GetValue(HasSearchActionsProperty);

    public event EventHandler<ModListRowEventArgs>? RowClicked;
    public event EventHandler<ModListRowEventArgs>? RowRightClicked;
    public event EventHandler<ModListSelectionRequestEventArgs>? SelectionRequested;
    public event EventHandler<ModListRowActionEventArgs>? RowActionInvoked;
    public event EventHandler<ModListInternalReorderEventArgs>? InternalReorderRequested;
    public event EventHandler? BackgroundClicked;

    private void OnToggleSearchClick(object sender, RoutedEventArgs e)
    {
        if (HeaderSearchBox.Visibility == Visibility.Visible)
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            fadeOut.Completed += (_, _) =>
            {
                HeaderSearchBox.Visibility = Visibility.Collapsed;
                HeaderTitleElement.Visibility = Visibility.Visible;
                HeaderSummaryElement.Visibility = Visibility.Visible;
                HeaderTitleElement.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                HeaderSummaryElement.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            };
            HeaderSearchBox.BeginAnimation(OpacityProperty, fadeOut);
            var actionFadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            actionFadeOut.Completed += (_, _) => HeaderSearchActions.Visibility = Visibility.Collapsed;
            HeaderSearchActions.BeginAnimation(OpacityProperty, actionFadeOut);
            return;
        }

        var titleFadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        titleFadeOut.Completed += (_, _) => HeaderTitleElement.Visibility = Visibility.Collapsed;
        HeaderTitleElement.BeginAnimation(OpacityProperty, titleFadeOut);
        var summaryFadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        summaryFadeOut.Completed += (_, _) => HeaderSummaryElement.Visibility = Visibility.Collapsed;
        HeaderSummaryElement.BeginAnimation(OpacityProperty, summaryFadeOut);
            if (HasSearchActions)
            {
                HeaderSearchActions.Visibility = Visibility.Visible;
                HeaderSearchActions.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            }
        HeaderSearchBox.Visibility = Visibility.Visible;
        HeaderSearchBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        HeaderSearchBox.Focus();
    }

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null || FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null) return;
        var item = (sender as FrameworkElement)?.DataContext;
        if (item is IModListSelectable selectable && SelectionPolicy is not ModListSelectionPolicy.None and not ModListSelectionPolicy.ReadOnly)
        {
            var selectedKeys = ResolveSelection(selectable, Keyboard.Modifiers);
            SelectionRequested?.Invoke(this, new ModListSelectionRequestEventArgs(item, selectedKeys, Keyboard.Modifiers));
        }
        else
        {
            RowClicked?.Invoke(this, new ModListRowEventArgs(item, Keyboard.Modifiers));
        }
        e.Handled = true;
    }

    private IReadOnlyList<string> ResolveSelection(IModListSelectable clicked, ModifierKeys modifiers)
    {
        var selectableItems = ItemsList.Items.OfType<IModListSelectable>().ToList();
        if (SelectionPolicy == ModListSelectionPolicy.Single)
        {
            _selectionAnchorKey = clicked.SelectionKey;
            return [clicked.SelectionKey];
        }

        var selected = selectableItems.Where(item => item.IsSelected).Select(item => item.SelectionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && !string.IsNullOrWhiteSpace(_selectionAnchorKey))
        {
            var anchorIndex = selectableItems.FindIndex(item => string.Equals(item.SelectionKey, _selectionAnchorKey, StringComparison.OrdinalIgnoreCase));
            var clickedIndex = selectableItems.FindIndex(item => string.Equals(item.SelectionKey, clicked.SelectionKey, StringComparison.OrdinalIgnoreCase));
            if (anchorIndex >= 0 && clickedIndex >= 0)
            {
                selected.Clear();
                foreach (var item in selectableItems.Skip(Math.Min(anchorIndex, clickedIndex)).Take(Math.Abs(anchorIndex - clickedIndex) + 1))
                    selected.Add(item.SelectionKey);
            }
        }
        else if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!selected.Remove(clicked.SelectionKey)) selected.Add(clicked.SelectionKey);
            _selectionAnchorKey = clicked.SelectionKey;
        }
        else
        {
            selected.Clear();
            selected.Add(clicked.SelectionKey);
            _selectionAnchorKey = clicked.SelectionKey;
        }

        return selectableItems.Where(item => selected.Contains(item.SelectionKey)).Select(item => item.SelectionKey).ToList();
    }

    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
        RowRightClicked?.Invoke(this, new ModListRowEventArgs((sender as FrameworkElement)?.DataContext, Keyboard.Modifiers));
        e.Handled = true;
    }

    private void OnRowActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: { } item, Tag: ModListRowAction action }) return;
        RowActionInvoked?.Invoke(this, new ModListRowActionEventArgs(item, action));
        e.Handled = true;
    }

    private void OnListBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null || FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null) return;
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null)
        {
            if (AllowInternalReorder
                && FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is IModListSelectable { IsSelected: true } selectable)
            {
                var selectedKeys = ItemsList.Items.OfType<IModListSelectable>()
                    .Where(item => item.IsSelected)
                    .Select(item => item.SelectionKey)
                    .ToList();
                if (selectedKeys.Count != 0)
                {
                    _dragCandidate = new DragCandidate(selectable.SelectionKey, selectedKeys);
                    _dragStartPoint = e.GetPosition(ItemsList);
                }
            }
            // The panel owns row selection, so prevent Selector's drag-selection auto-scroll.
            e.Handled = true;
            return;
        }
        BackgroundClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemsListPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isInternalDragActive)
        {
            EndInternalDrag(e.GetPosition(ItemsList));
            e.Handled = true;
            return;
        }

        _dragCandidate = null;
    }

    private void OnItemsListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(ItemsList);
        if (_isInternalDragActive)
        {
            UpdateInternalDrag(position);
            return;
        }

        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var candidate = _dragCandidate;
        _dragCandidate = null;
        BeginInternalDrag(candidate, position);
    }

    private void BeginInternalDrag(DragCandidate candidate, Point position)
    {
        _isInternalDragActive = true;
        _activeInternalDragPayload = new InternalDragPayload(candidate.SelectedKeys);
        _previousOverrideCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.SizeAll;
        Mouse.Capture(ItemsList, CaptureMode.Element);
        UpdateInternalDrag(position);
    }

    private void UpdateInternalDrag(Point position)
    {
        _dragPointerOnScreen = ItemsList.PointToScreen(position);
        UpdateDropInsertionIndicator(position);
        UpdateDragAutoScroll();
    }

    private void EndInternalDrag(Point? dropPosition = null)
    {
        var payload = _activeInternalDragPayload;
        _activeInternalDragPayload = null;
        _dragCandidate = null;
        _isInternalDragActive = false;
        try
        {
            if (payload is not null && dropPosition is { } position)
                InternalReorderRequested?.Invoke(this, new ModListInternalReorderEventArgs(payload.SelectedKeys, GetInsertionIndex(position)));
        }
        finally
        {
            if (Mouse.Captured == ItemsList) Mouse.Capture(null);
            Mouse.OverrideCursor = _previousOverrideCursor;
            _previousOverrideCursor = null;
            HideDropInsertionIndicator();
            StopDragAutoScroll();
        }
    }

    private void OnItemsListLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isInternalDragActive) EndInternalDrag();
    }

    private int GetInsertionIndex(Point position)
    {
        var realized = GetRealizedContainers();
        if (realized.Count == 0) return ItemsList.Items.Count;
        foreach (var row in realized)
        {
            var rowPosition = row.Container.TransformToAncestor(ItemsList).Transform(new Point());
            if (position.Y < rowPosition.Y + (row.Container.ActualHeight / 2)) return row.Index;
        }
        return realized[^1].Index + 1;
    }

    private void UpdateDropInsertionIndicator(Point position)
    {
        var insertionIndex = GetInsertionIndex(position);
        var realized = GetRealizedContainers();
        if (realized.Count == 0) return;
        var before = realized.FirstOrDefault(row => row.Index >= insertionIndex);
        var top = before is not null
            ? before.Container.TransformToAncestor(ItemsList).Transform(new Point()).Y - 1
            : realized[^1].Container.TransformToAncestor(ItemsList).Transform(new Point()).Y + realized[^1].Container.ActualHeight - 1;
        Canvas.SetLeft(DropInsertionIndicator, 8);
        Canvas.SetTop(DropInsertionIndicator, Math.Max(0, top));
        DropInsertionIndicator.Width = Math.Max(0, ItemsList.ActualWidth - 16);
        DropInsertionIndicator.Visibility = Visibility.Visible;
    }

    private List<RealizedRow> GetRealizedContainers()
    {
        var rows = new List<RealizedRow>();
        for (var index = 0; index < ItemsList.Items.Count; index++)
        {
            if (ItemsList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container && container.IsVisible)
                rows.Add(new RealizedRow(index, container));
        }
        return rows;
    }

    private void HideDropInsertionIndicator() => DropInsertionIndicator.Visibility = Visibility.Collapsed;

    private void UpdateDragAutoScroll()
    {
        if (!TryResolveDragAutoScroll(out var scrollViewer, out var direction, out var step))
        {
            StopDragAutoScroll();
            return;
        }

        _dragAutoScrollViewer = scrollViewer;
        _dragAutoScrollDirection = direction;
        _dragAutoScrollStep = step;
        if (!_dragAutoScrollTimer.IsEnabled) _dragAutoScrollTimer.Start();
    }

    private void OnDragAutoScrollTick(object? sender, EventArgs e)
    {
        if (_dragAutoScrollDirection == 0)
        {
            StopDragAutoScroll();
            return;
        }

        if (DateTime.UtcNow < _dragWheelCooldownUntilUtc) return;

        if (_dragPointerOnScreen is not { } pointerOnScreen)
        {
            StopDragAutoScroll();
            return;
        }

        var position = ItemsList.PointFromScreen(pointerOnScreen);
        UpdateDragAutoScroll();
        if (_dragAutoScrollViewer is null) return;
        _dragAutoScrollViewer.ScrollToVerticalOffset(Math.Clamp(
            _dragAutoScrollViewer.VerticalOffset + (_dragAutoScrollDirection * _dragAutoScrollStep),
            0,
            _dragAutoScrollViewer.ScrollableHeight));
        UpdateDropInsertionIndicator(position);
    }

    private void StopDragAutoScroll()
    {
        _dragAutoScrollDirection = 0;
        _dragAutoScrollStep = 0;
        _dragAutoScrollViewer = null;
        _dragPointerOnScreen = null;
        _dragWheelCooldownUntilUtc = default;
        _dragAutoScrollTimer.Stop();
    }

    private bool TryResolveDragAutoScroll(out ScrollViewer? scrollViewer, out int direction, out double step)
    {
        const double edgeSize = 32;
        if (_dragPointerOnScreen is not { } pointerOnScreen)
        {
            scrollViewer = null;
            direction = 0;
            step = 0;
            return false;
        }

        foreach (var candidate in GetDragScrollViewers())
        {
            if (candidate.ActualHeight <= 0 || candidate.ScrollableHeight <= 0) continue;
            var pointer = candidate.PointFromScreen(pointerOnScreen);
            if (pointer.Y < -edgeSize || pointer.Y > candidate.ActualHeight + edgeSize) continue;
            var topDistance = pointer.Y;
            var bottomDistance = candidate.ActualHeight - pointer.Y;
            direction = topDistance < edgeSize ? -1 : bottomDistance < edgeSize ? 1 : 0;
            if (direction == 0) continue;
            if (direction < 0 && candidate.VerticalOffset <= 0.5) continue;
            if (direction > 0 && candidate.VerticalOffset >= candidate.ScrollableHeight - 0.5) continue;

            var distance = direction < 0 ? topDistance : bottomDistance;
            var pressure = Math.Clamp((edgeSize - distance) / edgeSize, 0, 1);
            scrollViewer = candidate;
            step = 1.5 + (10.5 * pressure * pressure);
            return true;
        }

        scrollViewer = null;
        direction = 0;
        step = 0;
        return false;
    }

    private IEnumerable<ScrollViewer> GetDragScrollViewers()
    {
        var yielded = new HashSet<ScrollViewer>();
        if (FindDescendant<ScrollViewer>(ItemsList) is { } inner && yielded.Add(inner))
            yield return inner;

        for (DependencyObject? current = ItemsList; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer candidate && yielded.Add(candidate))
                yield return candidate;
        }
    }

    private void OnItemsListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        if (_isInternalDragActive)
            _dragWheelCooldownUntilUtc = DateTime.UtcNow.AddMilliseconds(260);
        HandleSmoothScrollWheel(e);
    }

    private void HandleSmoothScrollWheel(MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        if (TryQueueSmoothScroll(e.Delta)) e.Handled = true;
    }

    private bool TryQueueSmoothScroll(int wheelDelta)
    {
        var offsetDelta = -(wheelDelta / 120d) * 84;
        var direction = Math.Sign(offsetDelta);
        var scrollViewer = GetDragScrollViewers().FirstOrDefault(candidate => CanScroll(candidate, direction));
        if (scrollViewer is null) return false;
        QueueSmoothScroll(scrollViewer, offsetDelta);
        return true;
    }

    private static bool CanScroll(ScrollViewer scrollViewer, int direction)
        => scrollViewer.ScrollableHeight > 0
            && (direction < 0
                ? scrollViewer.VerticalOffset > 0.5
                : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 0.5);

    private void QueueSmoothScroll(ScrollViewer scrollViewer, double offsetDelta)
    {
        if (!ReferenceEquals(_smoothScrollViewer, scrollViewer))
        {
            StopSmoothScroll();
            _smoothScrollViewer = scrollViewer;
            _smoothScrollTarget = scrollViewer.VerticalOffset;
        }

        _smoothScrollTarget = Math.Clamp(_smoothScrollTarget + offsetDelta, 0, scrollViewer.ScrollableHeight);
        if (_smoothScrollLastFrameUtc == default)
        {
            _smoothScrollLastFrameUtc = DateTime.UtcNow;
            CompositionTarget.Rendering += OnSmoothScrollRendering;
        }
    }

    private void OnSmoothScrollRendering(object? sender, EventArgs e)
    {
        if (_smoothScrollViewer is null)
        {
            StopSmoothScroll();
            return;
        }

        var now = DateTime.UtcNow;
        var elapsedMilliseconds = Math.Clamp((now - _smoothScrollLastFrameUtc).TotalMilliseconds, 1, 50);
        _smoothScrollLastFrameUtc = now;
        var current = _smoothScrollViewer.VerticalOffset;
        var distance = _smoothScrollTarget - current;
        if (Math.Abs(distance) < 0.2)
        {
            _smoothScrollViewer.ScrollToVerticalOffset(_smoothScrollTarget);
            StopSmoothScroll();
            return;
        }

        var factor = 1 - Math.Exp(-elapsedMilliseconds / 70d);
        _smoothScrollViewer.ScrollToVerticalOffset(Math.Clamp(current + (distance * factor), 0, _smoothScrollViewer.ScrollableHeight));
    }

    private void StopSmoothScroll()
    {
        if (_smoothScrollLastFrameUtc != default)
            CompositionTarget.Rendering -= OnSmoothScrollRendering;
        _smoothScrollViewer = null;
        _smoothScrollTarget = 0;
        _smoothScrollLastFrameUtc = default;
    }

    private static void OnItemsSourceChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is ModListPanel panel && panel.IsLoaded)
            panel.ObserveItemsSource(args.NewValue as IEnumerable);
    }

    private void ObserveItemsSource(IEnumerable? source)
    {
        if (_observedItems is not null) _observedItems.CollectionChanged -= OnItemsCollectionChanged;
        if (_transitionNotifier is not null) _transitionNotifier.TransitionStarting -= OnItemsTransitionStarting;
        _observedItems = source as INotifyCollectionChanged;
        _transitionNotifier = source as IListTransitionNotifier;
        if (_observedItems is not null) _observedItems.CollectionChanged += OnItemsCollectionChanged;
        if (_transitionNotifier is not null) _transitionNotifier.TransitionStarting += OnItemsTransitionStarting;
    }

    private void OnItemsTransitionStarting(object? sender, ListTransitionBatch batch)
    {
        _pendingTransitionBatch = batch;
        if (batch.Animate) CaptureTransitionSnapshot();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_beforeTransition.Count == 0) CaptureTransitionSnapshot();
        if (_beforeTransition.Count != 0) QueueTransitionAfterLayout();
    }

    private void CaptureTransitionSnapshot()
    {
        if (!IsLoaded || ItemsList.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated) return;
        var snapshots = new Dictionary<string, ListTransitionSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ItemsList.Items)
        {
            if (ItemsList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container || !container.IsVisible) continue;
            var key = GetItemKey(item);
            if (key is null) continue;
            var position = container.TransformToAncestor(ItemsList).Transform(new Point());
            if (container.ActualWidth <= 0 || container.ActualHeight <= 0) continue;
            snapshots[key] = new ListTransitionSnapshot(key, position, new Size(container.ActualWidth, container.ActualHeight), CaptureContainerBitmap(container));
        }
        _beforeTransition = snapshots;
    }

    private void QueueTransitionAfterLayout()
    {
        if (_transitionScheduled || _isTransitionAnimationRunning) return;
        _transitionScheduled = true;
        ItemsList.LayoutUpdated += OnItemsListLayoutUpdated;
    }

    private void OnItemsListLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_transitionScheduled) return;
        if (_isTransitionAnimationRunning) return;
        if (ItemsList.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated) return;

        _transitionScheduled = false;
        ItemsList.LayoutUpdated -= OnItemsListLayoutUpdated;
        _ = Dispatcher.InvokeAsync(PlayPendingTransition, DispatcherPriority.Render);
    }

    private void PlayPendingTransition()
    {
        if (_beforeTransition.Count == 0 || !IsLoaded) return;
        var batch = _pendingTransitionBatch;
        _pendingTransitionBatch = null;
        if (batch is { Animate: false })
        {
            _beforeTransition.Clear();
            return;
        }
        var before = _beforeTransition;
        _beforeTransition = new Dictionary<string, ListTransitionSnapshot>(StringComparer.OrdinalIgnoreCase);
        var animated = false;
        var afterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ItemsList.Items)
        {
            if (ItemsList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container || !container.IsVisible) continue;
            var key = GetItemKey(item);
            if (key is null) continue;
            afterKeys.Add(key);
            var position = container.TransformToAncestor(ItemsList).Transform(new Point());
            animated |= before.TryGetValue(key, out var old)
                ? AnimateMovedContainer(container, old.Position.Y - position.Y)
                : AnimateAddedContainer(container);
        }

        foreach (var removed in before.Values.Where(snapshot => !afterKeys.Contains(snapshot.Key)).Take(12))
            animated |= AnimateRemovedSnapshot(removed);

        if (!animated) return;
        _isTransitionAnimationRunning = true;
        _transitionTimer.Stop();
        _transitionTimer.Start();
    }

    private void OnTransitionTimerTick(object? sender, EventArgs e)
    {
        _transitionTimer.Stop();
        _isTransitionAnimationRunning = false;
        if (_beforeTransition.Count != 0) QueueTransitionAfterLayout();
    }

    private static bool AnimateMovedContainer(FrameworkElement container, double deltaY)
    {
        if (Math.Abs(deltaY) < 0.5) return false;
        var transform = new TranslateTransform(0, deltaY);
        container.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        return true;
    }

    private static bool AnimateAddedContainer(FrameworkElement container)
    {
        container.Opacity = 0;
        container.RenderTransform = new TranslateTransform(0, 5);
        container.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        ((TranslateTransform)container.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        return true;
    }

    private bool AnimateRemovedSnapshot(ListTransitionSnapshot snapshot)
    {
        if (snapshot.Bitmap is null) return false;
        var ghost = new Image { Source = snapshot.Bitmap, Width = snapshot.Size.Width, Height = snapshot.Size.Height, Opacity = 1, IsHitTestVisible = false };
        Canvas.SetLeft(ghost, snapshot.Position.X);
        Canvas.SetTop(ghost, snapshot.Position.Y);
        TransitionOverlay.Children.Add(ghost);
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)) { BeginTime = TimeSpan.FromMilliseconds(20) };
        animation.Completed += (_, _) => TransitionOverlay.Children.Remove(ghost);
        ghost.BeginAnimation(OpacityProperty, animation);
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
        catch { return null; }
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

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T ancestor) return ancestor;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? current) where T : DependencyObject
    {
        if (current is null) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
        {
            var child = VisualTreeHelper.GetChild(current, index);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void OnSelectionIndicatorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border indicator || _selectionIndicatorSubscriptions.ContainsKey(indicator)) return;
        var subscription = new SelectionIndicatorSubscription();
        _selectionIndicatorSubscriptions.Add(indicator, subscription);
        indicator.DataContextChanged += OnSelectionIndicatorDataContextChanged;
        AttachSelectionIndicator(indicator, subscription, indicator.DataContext);
    }

    private void OnSelectionIndicatorUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border indicator || !_selectionIndicatorSubscriptions.Remove(indicator, out var subscription)) return;
        indicator.DataContextChanged -= OnSelectionIndicatorDataContextChanged;
        DetachSelectionIndicator(subscription);
    }

    private void OnSelectionIndicatorDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Border indicator || !_selectionIndicatorSubscriptions.TryGetValue(indicator, out var subscription)) return;
        DetachSelectionIndicator(subscription);
        AttachSelectionIndicator(indicator, subscription, e.NewValue);
    }

    private void AttachSelectionIndicator(Border indicator, SelectionIndicatorSubscription subscription, object? sourceObject)
    {
        subscription.Source = sourceObject as INotifyPropertyChanged;
        subscription.IsSelected = ReadIsSelected(sourceObject);
        SetSelectionIndicator(indicator, subscription.IsSelected, animate: false);
        if (subscription.Source is null) return;

        subscription.Handler = (_, args) =>
        {
            if (!string.Equals(args.PropertyName, "IsSelected", StringComparison.Ordinal)) return;
            var selected = ReadIsSelected(sourceObject);
            if (selected == subscription.IsSelected) return;
            subscription.IsSelected = selected;
            _ = Dispatcher.InvokeAsync(() => SetSelectionIndicator(indicator, selected, animate: true));
        };
        subscription.Source.PropertyChanged += subscription.Handler;
    }

    private static void DetachSelectionIndicator(SelectionIndicatorSubscription subscription)
    {
        if (subscription.Source is not null && subscription.Handler is not null) subscription.Source.PropertyChanged -= subscription.Handler;
        subscription.Source = null;
        subscription.Handler = null;
    }

    private static bool ReadIsSelected(object? source)
        => source?.GetType().GetProperty("IsSelected")?.GetValue(source) is true;

    private static void SetSelectionIndicator(Border indicator, bool selected, bool animate)
    {
        var scale = indicator.RenderTransform as ScaleTransform;
        if (scale is null || scale.IsFrozen)
        {
            scale = new ScaleTransform();
            indicator.RenderTransform = scale;
        }

        indicator.BeginAnimation(OpacityProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (!animate)
        {
            indicator.Opacity = selected ? 1 : 0;
            scale.ScaleY = selected ? 1 : 0;
            return;
        }

        indicator.Opacity = selected ? 0 : 1;
        scale.ScaleY = selected ? 0 : 1;
        indicator.BeginAnimation(OpacityProperty, new DoubleAnimation(selected ? 1 : 0, TimeSpan.FromMilliseconds(selected ? 60 : 80))
        {
            BeginTime = selected ? TimeSpan.Zero : TimeSpan.FromMilliseconds(40),
            EasingFunction = new CubicEase { EasingMode = selected ? EasingMode.EaseOut : EasingMode.EaseIn }
        });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(selected ? 1 : 0, TimeSpan.FromMilliseconds(selected ? 200 : 120))
        {
            EasingFunction = new CubicEase { EasingMode = selected ? EasingMode.EaseOut : EasingMode.EaseIn }
        });
    }

    private static void OnRowActionsChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        => target.SetValue(HasRowActionsPropertyKey, (ModListRowAction)args.NewValue != ModListRowAction.None);

    private static void OnSearchActionsTemplateChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        => target.SetValue(HasSearchActionsPropertyKey, args.NewValue is DataTemplate);

    private sealed class SelectionIndicatorSubscription
    {
        public INotifyPropertyChanged? Source { get; set; }
        public PropertyChangedEventHandler? Handler { get; set; }
        public bool IsSelected { get; set; }
    }

    private sealed record DragCandidate(string PressedKey, IReadOnlyList<string> SelectedKeys);

    private sealed record InternalDragPayload(IReadOnlyList<string> SelectedKeys);

    private sealed record RealizedRow(int Index, FrameworkElement Container);

    private sealed record ListTransitionSnapshot(string Key, Point Position, Size Size, BitmapSource? Bitmap);
}

public sealed class ModListRowEventArgs(object? item, ModifierKeys modifiers) : EventArgs
{
    public object? Item { get; } = item;
    public ModifierKeys Modifiers { get; } = modifiers;
}

[Flags]
public enum ModListRowAction
{
    None = 0,
    Rename = 1 << 0,
    Details = 1 << 1,
    AddToProfile = 1 << 2,
    OpenFolder = 1 << 3,
    MoveUp = 1 << 4,
    MoveDown = 1 << 5,
    RemoveFromProfile = 1 << 6,
    DeleteFromLibrary = 1 << 7,
    ToggleDecoration = 1 << 8,
}

public sealed class ModListRowActionEventArgs(object item, ModListRowAction action) : EventArgs
{
    public object Item { get; } = item;
    public ModListRowAction Action { get; } = action;
}

public sealed class ModListInternalReorderEventArgs(IReadOnlyList<string> draggedKeys, int insertionIndex) : EventArgs
{
    public IReadOnlyList<string> DraggedKeys { get; } = draggedKeys;
    public int InsertionIndex { get; } = insertionIndex;
}

public sealed class RowActionVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ModListRowAction actions && parameter is ModListRowAction action && actions.HasFlag(action)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class DecorationRowActionVisibilityConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not ModListRowAction available || parameter is not ModListRowAction requested || !available.HasFlag(requested))
            return Visibility.Collapsed;
        var isDecoration = values[1] is true;
        return requested switch
        {
            ModListRowAction.AddToProfile when isDecoration => Visibility.Collapsed,
            ModListRowAction.ToggleDecoration when !isDecoration => Visibility.Collapsed,
            _ => Visibility.Visible
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

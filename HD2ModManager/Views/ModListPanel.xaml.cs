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

    public ModListPanel()
    {
        InitializeComponent();
        _transitionTimer.Interval = TimeSpan.FromMilliseconds(230);
        _transitionTimer.Tick += OnTransitionTimerTick;
        Loaded += (_, _) => ObserveItemsSource(ItemsSource);
        Unloaded += (_, _) =>
        {
            _transitionTimer.Stop();
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
    public string SearchText { get => (string)GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public ScrollBarVisibility VerticalScrollBarVisibility { get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty); set => SetValue(VerticalScrollBarVisibilityProperty, value); }
    public ModListRowAction RowActions { get => (ModListRowAction)GetValue(RowActionsProperty); set => SetValue(RowActionsProperty, value); }
    public DataTemplate? SearchActionsTemplate { get => (DataTemplate?)GetValue(SearchActionsTemplateProperty); set => SetValue(SearchActionsTemplateProperty, value); }
    public bool HasRowActions => (bool)GetValue(HasRowActionsProperty);
    public bool HasSearchActions => (bool)GetValue(HasSearchActionsProperty);

    public event EventHandler<ModListRowEventArgs>? RowClicked;
    public event EventHandler<ModListRowEventArgs>? RowRightClicked;
    public event EventHandler<ModListRowActionEventArgs>? RowActionInvoked;
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
        RowClicked?.Invoke(this, new ModListRowEventArgs((sender as FrameworkElement)?.DataContext, Keyboard.Modifiers));
        e.Handled = true;
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
            // The panel owns row selection, so prevent Selector's drag-selection auto-scroll.
            e.Handled = true;
            return;
        }
        BackgroundClicked?.Invoke(this, EventArgs.Empty);
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
}

public sealed class ModListRowActionEventArgs(object item, ModListRowAction action) : EventArgs
{
    public object Item { get; } = item;
    public ModListRowAction Action { get; } = action;
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

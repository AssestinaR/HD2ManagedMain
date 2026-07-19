using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：封装可折叠的浮动操作按钮簇，支持默认圆形入口与悬停展开的操作按钮。
    public partial class FloatingActionCluster : UserControl
    {
        public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(nameof(Actions), typeof(IEnumerable), typeof(FloatingActionCluster), new PropertyMetadata(null, OnActionsChanged));
        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(FloatingActionCluster), new PropertyMetadata(false, OnIsExpandedChanged));
        public static readonly DependencyProperty MainContentProperty = DependencyProperty.Register(nameof(MainContent), typeof(object), typeof(FloatingActionCluster), new PropertyMetadata("☰"));
        public static readonly DependencyProperty MainToolTipProperty = DependencyProperty.Register(nameof(MainToolTip), typeof(object), typeof(FloatingActionCluster), new PropertyMetadata("操作"));
        public static readonly DependencyProperty MainBackgroundProperty = DependencyProperty.Register(nameof(MainBackground), typeof(Brush), typeof(FloatingActionCluster), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(30, 99, 214))));
        public static readonly DependencyProperty MainForegroundProperty = DependencyProperty.Register(nameof(MainForeground), typeof(Brush), typeof(FloatingActionCluster), new PropertyMetadata(Brushes.White));
        public static readonly DependencyProperty ButtonDiameterProperty = DependencyProperty.Register(nameof(ButtonDiameter), typeof(double), typeof(FloatingActionCluster), new PropertyMetadata(48d, OnShapePropertyChanged));
        public static readonly DependencyProperty ButtonCornerRadiusProperty = DependencyProperty.Register(nameof(ButtonCornerRadius), typeof(CornerRadius), typeof(FloatingActionCluster), new PropertyMetadata(new CornerRadius(24d)));
        public static readonly DependencyProperty ExpandedWidthProperty = DependencyProperty.Register(nameof(ExpandedWidth), typeof(double), typeof(FloatingActionCluster), new PropertyMetadata(168d, OnShapePropertyChanged));
        public static readonly DependencyProperty ExpandedOuterWidthProperty = DependencyProperty.Register(nameof(ExpandedOuterWidth), typeof(double), typeof(FloatingActionCluster), new PropertyMetadata(184d));
        public static readonly DependencyProperty ExpandedHeightProperty = DependencyProperty.Register(nameof(ExpandedHeight), typeof(double), typeof(FloatingActionCluster), new PropertyMetadata(40d, OnShapePropertyChanged));
        public static readonly DependencyProperty ExpandedCornerRadiusProperty = DependencyProperty.Register(nameof(ExpandedCornerRadius), typeof(CornerRadius), typeof(FloatingActionCluster), new PropertyMetadata(new CornerRadius(20d)));
        public static readonly DependencyProperty ActionSpacingProperty = DependencyProperty.Register(nameof(ActionSpacing), typeof(double), typeof(FloatingActionCluster), new PropertyMetadata(8d, OnShapePropertyChanged));
        public static readonly DependencyProperty ClusterHeightProperty = DependencyProperty.Register(nameof(ClusterHeight), typeof(double), typeof(FloatingActionCluster), new PropertyMetadata(48d));
        public static readonly DependencyProperty LabelDirectionProperty = DependencyProperty.Register(nameof(LabelDirection), typeof(string), typeof(FloatingActionCluster), new PropertyMetadata("Left", OnLabelDirectionChanged));
        public static readonly DependencyProperty ClusterHorizontalAlignmentProperty = DependencyProperty.Register(nameof(ClusterHorizontalAlignment), typeof(HorizontalAlignment), typeof(FloatingActionCluster), new PropertyMetadata(HorizontalAlignment.Right));

        public FloatingActionCluster()
        {
            InitializeComponent();
            UpdateShapeMetrics();
            PreviewMouseDown += (_, _) =>
            {
                if (Application.Current?.MainWindow?.DataContext is ShellViewModel shell)
                    shell.ClearTransientSelection();
            };
        }

        public IEnumerable? Actions { get => (IEnumerable?)GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
        public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
        public object? MainContent { get => GetValue(MainContentProperty); set => SetValue(MainContentProperty, value); }
        public object? MainToolTip { get => GetValue(MainToolTipProperty); set => SetValue(MainToolTipProperty, value); }
        public Brush MainBackground { get => (Brush)GetValue(MainBackgroundProperty); set => SetValue(MainBackgroundProperty, value); }
        public Brush MainForeground { get => (Brush)GetValue(MainForegroundProperty); set => SetValue(MainForegroundProperty, value); }
        public double ButtonDiameter { get => (double)GetValue(ButtonDiameterProperty); set => SetValue(ButtonDiameterProperty, value); }
        public CornerRadius ButtonCornerRadius { get => (CornerRadius)GetValue(ButtonCornerRadiusProperty); private set => SetValue(ButtonCornerRadiusProperty, value); }
        public double ExpandedWidth { get => (double)GetValue(ExpandedWidthProperty); set => SetValue(ExpandedWidthProperty, value); }
        public double ExpandedOuterWidth { get => (double)GetValue(ExpandedOuterWidthProperty); private set => SetValue(ExpandedOuterWidthProperty, value); }
        public double ExpandedHeight { get => (double)GetValue(ExpandedHeightProperty); set => SetValue(ExpandedHeightProperty, value); }
        public CornerRadius ExpandedCornerRadius { get => (CornerRadius)GetValue(ExpandedCornerRadiusProperty); private set => SetValue(ExpandedCornerRadiusProperty, value); }
        public double ActionSpacing { get => (double)GetValue(ActionSpacingProperty); set => SetValue(ActionSpacingProperty, value); }
        public double ClusterHeight { get => (double)GetValue(ClusterHeightProperty); private set => SetValue(ClusterHeightProperty, value); }
        public string LabelDirection { get => (string)GetValue(LabelDirectionProperty); set => SetValue(LabelDirectionProperty, value); }
        public HorizontalAlignment ClusterHorizontalAlignment { get => (HorizontalAlignment)GetValue(ClusterHorizontalAlignmentProperty); private set => SetValue(ClusterHorizontalAlignmentProperty, value); }

        private static void OnLabelDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FloatingActionCluster cluster) cluster.UpdateDirectionMetrics();
        }

        private static void OnActionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FloatingActionCluster cluster)
            {
                return;
            }

            if (e.OldValue is INotifyCollectionChanged oldActions)
            {
                oldActions.CollectionChanged -= cluster.OnActionsCollectionChanged;
            }

            if (e.NewValue is INotifyCollectionChanged newActions)
            {
                newActions.CollectionChanged += cluster.OnActionsCollectionChanged;
            }

            cluster.UpdateShapeMetrics();
        }

        private void OnActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateShapeMetrics();
        }

        private static void OnShapePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FloatingActionCluster cluster) cluster.UpdateShapeMetrics();
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FloatingActionCluster cluster) cluster.UpdateExpansionAnimation((bool)e.NewValue);
        }

        private void UpdateShapeMetrics()
        {
            ButtonCornerRadius = new CornerRadius(Math.Max(0, ButtonDiameter / 2d));
            ExpandedCornerRadius = new CornerRadius(Math.Max(0, ExpandedHeight / 2d));
            ExpandedOuterWidth = Math.Max(GetRequiredExpandedWidth() + 16d, ButtonDiameter + 16d);
            ClusterHeight = GetExpandedActionsHeight() + ButtonDiameter;
        }

        private void UpdateDirectionMetrics()
        {
            ClusterHorizontalAlignment = string.Equals(LabelDirection, "Right", StringComparison.OrdinalIgnoreCase)
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
        }

        private void UpdateExpansionAnimation(bool isExpanded)
        {
            if (isExpanded)
            {
                ActionsHost.IsHitTestVisible = true;
                Dispatcher.BeginInvoke(() => AnimateActionItems(expand: true), DispatcherPriority.Loaded);
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                var animations = AnimateActionItems(expand: false);
                if (animations == 0)
                {
                    DisableActionsHitTestIfCollapsed();
                }
            }, DispatcherPriority.Loaded);
        }

        private double GetExpandedActionsHeight()
        {
            var count = GetActionCount();
            return count <= 0 ? 0d : count * (ButtonDiameter + ActionSpacing);
        }

        private double GetRequiredExpandedWidth()
        {
            var requestedWidth = Actions?.OfType<PageActionViewModel>().Select(action => action.ExpandedWidth).DefaultIfEmpty(ExpandedWidth).Max() ?? ExpandedWidth;
            return Math.Max(ExpandedWidth, requestedWidth);
        }

        private int AnimateActionItems(bool expand)
        {
            ActionsHost.UpdateLayout();

            var count = GetActionCount();
            var duration = TimeSpan.FromMilliseconds(expand ? 260 : 190);
            IEasingFunction easing = expand
                ? new QuinticEase { EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseIn };
            var animationCount = 0;

            for (var i = 0; i < count; i++)
            {
                if (ActionsHost.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter)
                {
                    continue;
                }

                if (presenter.RenderTransform is not TranslateTransform translate || translate.IsFrozen)
                {
                    translate = new TranslateTransform();
                    presenter.RenderTransform = translate;
                }

                var currentOffset = translate.Y;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = currentOffset;

                var targetOffset = expand ? -GetActionOffset(i, count) : 0d;
                var animation = CreateAnimation(targetOffset, duration, easing);
                animation.From = currentOffset;
                if (!expand)
                {
                    animation.Completed += (_, _) =>
                    {
                        translate.BeginAnimation(TranslateTransform.YProperty, null);
                        translate.Y = 0d;
                        DisableActionsHitTestIfCollapsed();
                    };
                }
                else
                {
                    animation.Completed += (_, _) =>
                    {
                        translate.BeginAnimation(TranslateTransform.YProperty, null);
                        translate.Y = targetOffset;
                    };
                }

                translate.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
                animationCount++;
            }

            return animationCount;
        }

        private double GetActionOffset(int index, int count) => (count - index) * (ButtonDiameter + ActionSpacing);

        private int GetActionCount() => Actions?.Cast<object>().Count() ?? 0;

        private static DoubleAnimation CreateAnimation(double to, TimeSpan duration, IEasingFunction easingFunction)
        {
            return new DoubleAnimation
            {
                To = to,
                Duration = duration,
                EasingFunction = easingFunction,
                FillBehavior = FillBehavior.Stop
            };
        }

        private void DisableActionsHitTestIfCollapsed()
        {
            if (!IsExpanded)
            {
                ActionsHost.IsHitTestVisible = false;
            }
        }

        private void OnMainButtonClick(object sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;
    }
}

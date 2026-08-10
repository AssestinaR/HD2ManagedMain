using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

// Visual projection only. Registration and row planning stay in the view-model
// layer; this class owns presenter lifetime and WPF animations.
public partial class BottomBarSurface : UserControl
{
    private readonly Dictionary<string, ContentPresenter> _active = new(StringComparer.Ordinal);
    private object? _preparedContent;
    private ContentPresenter? _preparedPresenter;
    private int _layoutVersion;

    public event EventHandler<Size>? ContentSizeReady;

    public BottomBarSurface()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void Apply(BottomBarLayoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var nextKeys = snapshot.Rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var removed in _active.Values.Where(presenter => !nextKeys.Contains((string)presenter.Tag)).ToArray())
        {
            _active.Remove((string)removed.Tag);
            ActiveRows.Children.Remove(removed);
            OutgoingRows.Children.Add(removed);
            Canvas.SetLeft(removed, 0d);
            Canvas.SetBottom(removed, 0d);
            Fade(removed, removed.Opacity, 0d, 120, () => OutgoingRows.Children.Remove(removed));
        }

        for (var index = 0; index < snapshot.Rows.Count; index++)
        {
            var row = snapshot.Rows[index];
            if (!_active.TryGetValue(row.Key, out var presenter))
            {
                presenter = TakePreparedPresenter(row.Content) ?? CreatePresenter(row.Content);
                presenter.Tag = row.Key;
                presenter.Opacity = 0d;
                _active.Add(row.Key, presenter);
                ActiveRows.Children.Insert(index, presenter);
                Fade(presenter, 0d, 1d, 150);
            }
            else
            {
                presenter.Content = row.Content;
                var currentIndex = ActiveRows.Children.IndexOf(presenter);
                if (currentIndex != index)
                {
                    var translate = (TranslateTransform)presenter.RenderTransform;
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    translate.Y = (currentIndex - index) * (BottomBarLayoutEngine.RowHeight + BottomBarLayoutEngine.RowGap);
                    ActiveRows.Children.RemoveAt(currentIndex);
                    ActiveRows.Children.Insert(index, presenter);
                    var move = new DoubleAnimation(translate.Y, 0d, TimeSpan.FromMilliseconds(180));
                    translate.BeginAnimation(TranslateTransform.YProperty, move);
                }
            }
        }

        Visibility = snapshot.HasContent ? Visibility.Visible : Visibility.Collapsed;
        var version = ++_layoutVersion;
        Dispatcher.BeginInvoke(() => PublishSize(snapshot, version), DispatcherPriority.Loaded);
    }

    public void Prepare(object content)
    {
        if (ReferenceEquals(content, _preparedContent)) return;
        var presenter = CreatePresenter(content);
        presenter.Opacity = 0d;
        ActiveRows.Children.Add(presenter);
        Visibility = Visibility.Hidden;
        Dispatcher.BeginInvoke(() =>
        {
            if (!ActiveRows.Children.Contains(presenter)) return;
            presenter.ApplyTemplate();
            presenter.Measure(new Size(double.PositiveInfinity, BottomBarLayoutEngine.RowHeight));
            ActiveRows.Children.Remove(presenter);
            _preparedContent = content;
            _preparedPresenter = presenter;
            if (_active.Count == 0) Visibility = Visibility.Collapsed;
        }, DispatcherPriority.ContextIdle);
    }

    private ContentPresenter? TakePreparedPresenter(object content)
    {
        if (!ReferenceEquals(content, _preparedContent)) return null;
        var presenter = _preparedPresenter;
        _preparedContent = null;
        _preparedPresenter = null;
        return presenter;
    }

    private static ContentPresenter CreatePresenter(object content)
        => new()
        {
            Content = content,
            DataContext = content,
            Height = BottomBarLayoutEngine.RowHeight,
            RenderTransform = new TranslateTransform()
        };

    private void PublishSize(BottomBarLayoutSnapshot snapshot, int version)
    {
        if (version != _layoutVersion) return;
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        ContentSizeReady?.Invoke(this, new Size(
            Math.Max(snapshot.PreferredWidth, DesiredSize.Width),
            DesiredSize.Height));
    }

    private static void Fade(UIElement element, double from, double to, int milliseconds, Action? completed = null)
    {
        element.BeginAnimation(OpacityProperty, null);
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds));
        if (completed is not null) animation.Completed += (_, _) => completed();
        element.BeginAnimation(OpacityProperty, animation);
    }
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace HD2ModManager.Views
{
    // 作用：描述浮动操作簇中的单个操作按钮，支持图标、说明、命令和扩展内容。
    public sealed class FloatingActionItem : Freezable
    {
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(object), typeof(FloatingActionItem));
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(FloatingActionItem), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(FloatingActionItem));
        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(FloatingActionItem));
        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(FloatingActionItem), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(30, 99, 214))));
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(FloatingActionItem), new PropertyMetadata(Brushes.White));
        public static readonly DependencyProperty ExpandedContentProperty = DependencyProperty.Register(nameof(ExpandedContent), typeof(object), typeof(FloatingActionItem));

        public object? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
        public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
        public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
        public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
        public object? ExpandedContent { get => GetValue(ExpandedContentProperty); set => SetValue(ExpandedContentProperty, value); }

        protected override Freezable CreateInstanceCore() => new FloatingActionItem();
    }
}

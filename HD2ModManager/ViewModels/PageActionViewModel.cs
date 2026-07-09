using System.Windows.Input;
using System.Windows.Media;

namespace HD2ModManager.ViewModels
{
    // 作用：描述页面可注册到浮动操作区的标准动作项。
    public sealed class PageActionViewModel : BaseViewModel
    {
        private bool _isVisible = true;
        private bool _isEnabled = true;

        public PageActionViewModel(object icon, string label, ICommand command, object? commandParameter = null, Brush? background = null, Brush? foreground = null, object? expandedContent = null, int order = 0, string? group = null, string? kind = null)
        {
            Icon = icon;
            Label = label;
            Command = command;
            CommandParameter = commandParameter;
            Background = background ?? new SolidColorBrush(Color.FromRgb(30, 99, 214));
            Foreground = foreground ?? Brushes.White;
            ExpandedContent = expandedContent;
            Order = order;
            Group = group ?? string.Empty;
            Kind = kind ?? string.Empty;
        }

        public object Icon { get; }
        public string Label { get; }
        public ICommand Command { get; }
        public object? CommandParameter { get; }
        public Brush Background { get; }
        public Brush Foreground { get; }
        public object? ExpandedContent { get; }
        public int Order { get; }
        public string Group { get; }
        public string Kind { get; }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetField(ref _isVisible, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetField(ref _isEnabled, value);
        }
    }
}

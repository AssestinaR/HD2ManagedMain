using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LiberTeaManager // root namespace so XAML with clr-namespace:LiberTeaManager works
{
    public static class TreeViewExpandBehavior
    {
        public static readonly DependencyProperty EnableSmartExpandProperty = DependencyProperty.RegisterAttached(
            "EnableSmartExpand",
            typeof(bool),
            typeof(TreeViewExpandBehavior),
            new PropertyMetadata(false, OnEnableSmartExpandChanged));

        public static void SetEnableSmartExpand(DependencyObject element, bool value) => element.SetValue(EnableSmartExpandProperty, value);
        public static bool GetEnableSmartExpand(DependencyObject element) => (bool)element.GetValue(EnableSmartExpandProperty);

        private static void OnEnableSmartExpandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TreeViewItem tvi) return;
            if ((bool)e.NewValue) tvi.PreviewMouseDoubleClick += OnPreviewMouseDoubleClick;
            else tvi.PreviewMouseDoubleClick -= OnPreviewMouseDoubleClick;
        }

        private static void OnPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeViewItem senderItem) { e.Handled = true; return; }
            var sourceItem = FindSourceTreeViewItem(e.OriginalSource as DependencyObject);
            if (sourceItem == null) { e.Handled = true; return; }
            if (!ReferenceEquals(sourceItem, senderItem)) return; // only innermost

            var dc = sourceItem.DataContext;
            switch (dc)
            {
                case MainModItem main:
                    if (main.Options != null && main.Options.Count > 0) sourceItem.IsExpanded = !sourceItem.IsExpanded;
                    e.Handled = true; break;
                case OptionItem opt:
                    if (opt.SubOptions != null && opt.SubOptions.Count > 0) sourceItem.IsExpanded = !sourceItem.IsExpanded;
                    e.Handled = true; break;
                case SubOptionItem:
                    e.Handled = true; break;
                default:
                    e.Handled = true; break;
            }
        }

        private static TreeViewItem? FindSourceTreeViewItem(DependencyObject? origin)
        {
            while (origin != null)
            {
                if (origin is TreeViewItem tvi) return tvi;
                origin = VisualTreeHelper.GetParent(origin);
            }
            return null;
        }
    }
}

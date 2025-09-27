using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace LiberTeaManager.UI.Drag
{
    /// <summary>
    /// Encapsulates TreeView drag / drop visual insertion line logic.
    /// MainWindow delegates related events to this class keeping code slim.
    /// </summary>
    internal sealed class TreeDragManager
    {
        private readonly TreeView _tree;
        private readonly Func<IEnumerable<object>> _selectedProvider;
        private readonly Action<object?, TreePlacement, List<object>> _onDrop;
        private readonly Action<object?> _onHoverTargetChanged;
        private AdornerLayer? _adornerLayer;
        private TreeInsertAdorner? _currentAdorner;
        private TreeViewItem? _adornerItem;

        public enum TreePlacement { None, Before, After, Inside }

        public TreeDragManager(TreeView tree,
            Func<IEnumerable<object>> selectedProvider,
            Action<object?, TreePlacement, List<object>> onDrop,
            Action<object?> onHoverTargetChanged)
        {
            _tree = tree;
            _selectedProvider = selectedProvider;
            _onDrop = onDrop;
            _onHoverTargetChanged = onHoverTargetChanged;
        }

        public void ClearVisual() => RemoveAdorner();

        public void HandleDragOver(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(List<object>))) { e.Effects = DragDropEffects.None; return; }
            var dep = e.OriginalSource as DependencyObject;
            var item = ContainerFrom(dep);
            if (item == null) { e.Effects = DragDropEffects.None; RemoveAdorner(); return; }
            var pos = e.GetPosition(item);
            var placement = CalcPlacement(item, pos);
            _onHoverTargetChanged(item.DataContext);
            DrawAdorner(item, placement);
            e.Effects = DragDropEffects.Move; e.Handled = true;
        }

        public void HandleDrop(DragEventArgs e)
        {
            RemoveAdorner();
            if (!e.Data.GetDataPresent(typeof(List<object>))) return;
            var dep = e.OriginalSource as DependencyObject;
            var item = ContainerFrom(dep);
            var ctx = item?.DataContext;
            var pos = item != null ? e.GetPosition(item) : new Point();
            var placement = item != null ? CalcPlacement(item, pos) : TreePlacement.None;
            var list = e.Data.GetData(typeof(List<object>)) as List<object> ?? new();
            _onDrop(ctx, placement, list);
        }

        private TreeViewItem? ContainerFrom(DependencyObject? dep)
        {
            while (dep != null && dep is not TreeViewItem)
                dep = VisualTreeHelper.GetParent(dep);
            return dep as TreeViewItem;
        }

        private TreePlacement CalcPlacement(TreeViewItem item, Point pos)
        {
            double h = item.ActualHeight;
            if (h <= 4) return TreePlacement.Inside;
            if (pos.Y < h * 0.25) return TreePlacement.Before;
            if (pos.Y > h * 0.75) return TreePlacement.After;
            return TreePlacement.Inside;
        }

        private void DrawAdorner(TreeViewItem item, TreePlacement placement)
        {
            RemoveAdorner();
            if (placement is TreePlacement.Before or TreePlacement.After)
            {
                _adornerLayer ??= AdornerLayer.GetAdornerLayer(item);
                if (_adornerLayer != null)
                {
                    _adornerItem = item;
                    _currentAdorner = new TreeInsertAdorner(item, placement == TreePlacement.After);
                    _adornerLayer.Add(_currentAdorner);
                }
            }
            else if (placement == TreePlacement.Inside)
            {
                // inside no line; rely on background highlight through Tag
            }
        }

        private void RemoveAdorner()
        {
            if (_adornerLayer != null && _currentAdorner != null)
            {
                _adornerLayer.Remove(_currentAdorner);
            }
            _currentAdorner = null; _adornerItem = null;
        }
    }
}
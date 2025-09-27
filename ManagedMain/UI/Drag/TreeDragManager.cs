using System;
using System.Collections.Generic;
using System.Linq;
using SW = System.Windows;
using SWC = System.Windows.Controls;
using SWD = System.Windows.Documents;
using SWI = System.Windows.Input;
using SWM = System.Windows.Media;

namespace ManagedMain.UI.Drag
{
    internal sealed class TreeDragManager
    {
        private readonly SWC.TreeView _tree;
        private readonly Func<IEnumerable<object>> _selectedProvider;
        private readonly Action<object?, TreePlacement, List<object>> _onDrop;
        private readonly Action<object?> _onHoverTargetChanged;
        private SWD.AdornerLayer? _adornerLayer;
        private TreeInsertAdorner? _currentAdorner;

        public enum TreePlacement { None, Before, After, Inside }

        public TreeDragManager(SWC.TreeView tree,
            Func<IEnumerable<object>> selectedProvider,
            Action<object?, TreePlacement, List<object>> onDrop,
            Action<object?> onHoverTargetChanged)
        {
            _tree = tree;
            _selectedProvider = selectedProvider;
            _onDrop = onDrop;
            _onHoverTargetChanged = onHoverTargetChanged;
        }

        // Allow host to clear any visual adorners when drag context changes (e.g., external file drag)
        public void Clear()
        {
            RemoveAdorner();
        }

        public void HandleDragOver(SW.DragEventArgs e)
        {
            var list = ExtractDragged(e.Data);
            if (list.Count == 0) { e.Effects = SW.DragDropEffects.None; RemoveAdorner(); return; }
            var dep = e.OriginalSource as SW.DependencyObject;
            var item = ContainerFrom(dep);
            if (item == null) { e.Effects = SW.DragDropEffects.None; RemoveAdorner(); return; }
            // If hovering over one of the dragged items, show none
            if (list.Contains(item.DataContext)) { e.Effects = SW.DragDropEffects.None; RemoveAdorner(); return; }
            var pos = e.GetPosition(item);
            var (placement, headerRect) = CalcPlacement(item, pos);
            _onHoverTargetChanged(item.DataContext);
            DrawAdorner(item, placement, headerRect);
            e.Effects = SW.DragDropEffects.Move; e.Handled = true;
        }

        public void HandleDrop(SW.DragEventArgs e)
        {
            RemoveAdorner();
            var list = ExtractDragged(e.Data);
            if (list.Count == 0) return;
            var dep = e.OriginalSource as SW.DependencyObject;
            var item = ContainerFrom(dep);
            var ctx = item?.DataContext;
            if (ctx != null && list.Contains(ctx)) return; // ignore drop onto self
            var pos = item != null ? e.GetPosition(item) : new SW.Point();
            var (placement, _) = item != null ? CalcPlacement(item, pos) : (TreePlacement.None, (SW.Rect?)null);
            _onDrop(ctx, placement, list);
        }

        private List<object> ExtractDragged(SW.IDataObject data)
        {
            if (data == null) return new List<object>();
            // Treat as internal drag ONLY if our specific format is present
            if (data.GetDataPresent(typeof(List<object>)))
            {
                if (data.GetData(typeof(List<object>)) is List<object> list && list.Count > 0)
                    return list;
            }
            // Otherwise, it's not our internal drag; do not fall back to current selection
            return new List<object>();
        }

        private static SWC.TreeViewItem? ContainerFrom(SW.DependencyObject? dep)
        {
            while (dep != null && dep is not SWC.TreeViewItem)
                dep = SWM.VisualTreeHelper.GetParent(dep);
            return dep as SWC.TreeViewItem;
        }

        private static (TreePlacement placement, SW.Rect? headerRect) CalcPlacement(SWC.TreeViewItem item, SW.Point pos)
        {
            var headerRect = GetHeaderRect(item);
            if (headerRect.HasValue)
            {
                // If pointer is below header area, treat as After; if above, Before
                if (pos.Y > headerRect.Value.Bottom) return (TreePlacement.After, headerRect);
                if (pos.Y < headerRect.Value.Top) return (TreePlacement.Before, headerRect);
                double h = Math.Max(8, headerRect.Value.Height);
                double yRel = pos.Y - headerRect.Value.Top;
                const double topZone = 0.35;
                const double bottomZone = 0.65;
                if (yRel < h * topZone) return (TreePlacement.Before, headerRect);
                if (yRel > h * bottomZone) return (TreePlacement.After, headerRect);
                return (TreePlacement.Inside, headerRect);
            }
            else
            {
                // Fallback: approximate using a clamped item height (row-like)
                double h = Math.Min(item.ActualHeight, 36);
                if (h <= 4) return (TreePlacement.Inside, null);
                const double topZone = 0.35;
                const double bottomZone = 0.65;
                if (pos.Y < h * topZone) return (TreePlacement.Before, null);
                if (pos.Y > h * bottomZone) return (TreePlacement.After, null);
                return (TreePlacement.Inside, null);
            }
        }

        private static SW.Rect? GetHeaderRect(SWC.TreeViewItem item)
        {
            try
            {
                // Try common names used in our templates
                var fe = FindElementByName(item, "Bd") as SW.FrameworkElement;
                if (fe == null) fe = FindElementByName(item, "PART_Header") as SW.FrameworkElement;
                if (fe == null)
                {
                    // fallback: first Border or ContentPresenter in visual tree
                    fe = FindFirst<SWC.Border>(item) as SW.FrameworkElement ?? FindFirst<SW.FrameworkElement>(item);
                }
                if (fe != null)
                {
                    var p = fe.TransformToAncestor(item).Transform(new SW.Point(0, 0));
                    return new SW.Rect(p, new SW.Size(fe.ActualWidth, fe.ActualHeight));
                }
            }
            catch { }
            return null;
        }

        private static SW.DependencyObject? FindElementByName(SW.DependencyObject root, string name)
        {
            var q = new System.Collections.Generic.Queue<SW.DependencyObject>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var d = q.Dequeue();
                if (d is SW.FrameworkElement fe && fe.Name == name) return fe;
                int c = SWM.VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < c; i++) q.Enqueue(SWM.VisualTreeHelper.GetChild(d, i));
            }
            return null;
        }

        private static T? FindFirst<T>(SW.DependencyObject root) where T : SW.DependencyObject
        {
            var q = new System.Collections.Generic.Queue<SW.DependencyObject>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var d = q.Dequeue();
                if (d is T t) return t;
                int c = SWM.VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < c; i++) q.Enqueue(SWM.VisualTreeHelper.GetChild(d, i));
            }
            return null;
        }

        private void DrawAdorner(SWC.TreeViewItem item, TreePlacement placement, SW.Rect? headerRect)
        {
            RemoveAdorner();
            _adornerLayer ??= SWD.AdornerLayer.GetAdornerLayer(item);
            if (_adornerLayer == null) return;

            switch (placement)
            {
                case TreePlacement.Before:
                    _currentAdorner = new TreeInsertAdorner(item, after: false, inside: false);
                    break;
                case TreePlacement.After:
                    _currentAdorner = new TreeInsertAdorner(item, after: true, inside: false);
                    break;
                case TreePlacement.Inside:
                    _currentAdorner = new TreeInsertAdorner(item, after: false, inside: true, headerRect);
                    break;
                default:
                    _currentAdorner = null; break;
            }
            if (_currentAdorner != null)
                _adornerLayer.Add(_currentAdorner);
        }

        private void RemoveAdorner()
        {
            if (_adornerLayer != null && _currentAdorner != null)
            {
                _adornerLayer.Remove(_currentAdorner);
            }
            _currentAdorner = null;
        }
    }
}

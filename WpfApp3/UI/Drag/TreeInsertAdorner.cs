using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;

namespace LiberTeaManager.UI.Drag
{
    /// <summary>
    /// Adorner drawing an insertion line (before / after) on a TreeViewItem.
    /// </summary>
    internal sealed class TreeInsertAdorner : Adorner
    {
        private readonly bool _after;
        private readonly Pen _pen;
        private readonly double _offsetX;
        public TreeInsertAdorner(TreeViewItem adornedElement, bool after) : base(adornedElement)
        {
            _after = after;
            IsHitTestVisible = false;
            _pen = new Pen(new SolidColorBrush(Color.FromRgb(0x33,0x99,0xFF)),2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _offsetX = 4; // slight indent to avoid left expand arrow
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (AdornedElement is not FrameworkElement fe) return;
            double y = _after ? fe.ActualHeight - 1 : 1;
            var geom = new LineGeometry(new Point(_offsetX, y), new Point(fe.ActualWidth - 4, y));
            dc.DrawGeometry(null, _pen, geom);
        }
    }
}
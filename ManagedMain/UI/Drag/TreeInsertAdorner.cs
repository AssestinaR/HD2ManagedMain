using SW = System.Windows;
using SWC = System.Windows.Controls;
using SWD = System.Windows.Documents;
using SWM = System.Windows.Media;

namespace ManagedMain.UI.Drag
{
    /// <summary>
    /// Drop hint on a TreeViewItem:
    /// - Before/After: thick line with arrow caps
    /// - Inside: rounded rectangle highlight (defaults to whole item; can restrict to headerRect)
    /// </summary>
    internal sealed class TreeInsertAdorner : SWD.Adorner
    {
        private readonly bool _isAfter;
        private readonly bool _isInside;
        private readonly SWM.Pen _linePen;
        private readonly SWM.Brush _insideFill;
        private readonly SWM.Pen _insideBorder;
        private readonly double _offsetX;
        private readonly SW.Rect? _headerRect;

        public TreeInsertAdorner(SWC.TreeViewItem adornedElement, bool after, bool inside = false, SW.Rect? headerRect = null) : base(adornedElement)
        {
            _isAfter = after;
            _isInside = inside;
            _headerRect = headerRect;
            IsHitTestVisible = false;

            var lineColor = SWM.Color.FromRgb(0x33, 0x99, 0xFF);
            _linePen = new SWM.Pen(new SWM.SolidColorBrush(lineColor), 3.0)
            {
                StartLineCap = SWM.PenLineCap.Round,
                EndLineCap = SWM.PenLineCap.Round
            };
            _linePen.Freeze();

            var fillColor = SWM.Color.FromArgb(48, 0x33, 0x99, 0xFF); // translucent
            var borderColor = SWM.Color.FromRgb(0x33, 0x99, 0xFF);
            _insideFill = new SWM.SolidColorBrush(fillColor);
            _insideBorder = new SWM.Pen(new SWM.SolidColorBrush(borderColor), 1.5) { DashStyle = SWM.DashStyles.Solid };
            _insideBorder.Freeze();

            _offsetX = 14; // indent to avoid left expand arrow and tree glyphs
        }

        protected override void OnRender(SWM.DrawingContext dc)
        {
            base.OnRender(dc);
            if (AdornedElement is not SW.FrameworkElement fe) return;
            var width = fe.ActualWidth;
            if (width <= 0 || fe.ActualHeight <= 0) return;

            if (_isInside)
            {
                // Use headerRect when available; fall back to whole item with margins
                if (_headerRect.HasValue)
                {
                    var r = _headerRect.Value;
                    var rect = new SW.Rect(r.X + 2, r.Y + 2, r.Width - 4, Math.Max(6, r.Height - 4));
                    var geom = new SWM.RectangleGeometry(rect, 6, 6);
                    dc.DrawGeometry(_insideFill, _insideBorder, geom);
                }
                else
                {
                    var rect = new SW.Rect(_offsetX, 2, width - _offsetX - 6, fe.ActualHeight - 4);
                    var geom = new SWM.RectangleGeometry(rect, 6, 6);
                    dc.DrawGeometry(_insideFill, _insideBorder, geom);
                }
                return;
            }

            // Before / After line with small arrow markers
            double y = _isAfter ? fe.ActualHeight - 1.5 : 1.5;
            var p1 = new SW.Point(_offsetX, y);
            var p2 = new SW.Point(width - 6, y);
            dc.DrawLine(_linePen, p1, p2);

            // Draw small triangles at both ends to emphasize direction
            DrawTriangle(dc, p1, new SW.Vector(0, _isAfter ? -1 : 1));
            DrawTriangle(dc, p2, new SW.Vector(0, _isAfter ? -1 : 1));
        }

        private void DrawTriangle(SWM.DrawingContext dc, SW.Point center, SW.Vector up)
        {
            if (up.X == 0 && up.Y == 0) up = new SW.Vector(0, 1);
            up.Normalize();
            var right = new SW.Vector(up.Y, -up.X); // perpendicular
            const double size = 4.0;
            var a = center + up * size;
            var b = center - up * size + right * size;
            var c = center - up * size - right * size;
            var fig = new SWM.PathFigure { StartPoint = a, IsClosed = true, IsFilled = true };
            fig.Segments.Add(new SWM.LineSegment(b, true));
            fig.Segments.Add(new SWM.LineSegment(c, true));
            var geo = new SWM.PathGeometry();
            geo.Figures.Add(fig);
            dc.DrawGeometry((_linePen.Brush), null, geo);
        }
    }
}

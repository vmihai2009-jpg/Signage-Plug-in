using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BGSignage.Core.Geometry;
using BGSignage.Core.Layout;
using BGSignage.Core.Model;

namespace BGSignage.Engine.UI
{
    /// <summary>
    /// The live preview: the sign as it will be built, in front view or a 3D (oblique) view that shows the
    /// panel thickness, letter depth and stand-off with shadows. Click to select, drag to move, wheel to zoom,
    /// right or middle drag to pan.
    /// </summary>
    internal sealed class PreviewCanvas : FrameworkElement
    {
        SignGeometry _geometry;
        SignDocument _doc;
        readonly List<(PieceRole Role, MaterialSlot Slot, Geometry Shape)> _groups = new List<(PieceRole, MaterialSlot, Geometry)>();
        Geometry _panel;

        double _zoom = 0.4;            // screen pixels per mm
        Vector _pan = new Vector(40, 40);
        bool _fitted;

        string _dragId;
        Point _dragStartMm, _lastMm;
        bool _dragging, _panning;
        Point _panStart;
        Vector _panAtStart;

        public string SelectedId { get; set; }
        public bool ThreeD { get; set; }
        public bool ShowGrid { get; set; } = true;
        public Color WallColour { get; set; } = Color.FromRgb(214, 210, 203);

        public event Action<string> ElementClicked;
        public event Action<string> ElementDoubleClicked;
        public event Action<string> DragStarted;
        /// <summary>Total movement in mm since the drag started (snapped to whole millimetres).</summary>
        public event Action<string, double, double> Dragging;
        public event Action<Point> PointerMoved;

        public PreviewCanvas()
        {
            ClipToBounds = true;
            Focusable = true;
            Cursor = Cursors.Arrow;
        }

        public void Show(SignDocument doc, SignGeometry geometry)
        {
            _doc = doc;
            _geometry = geometry;
            _groups.Clear();
            _panel = Shape(new[] { geometry.PanelOutline });
            foreach (var g in geometry.Pieces.GroupBy(p => (p.Role, p.Material)))
            {
                var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                foreach (var piece in g)
                    foreach (var island in SignBuilder.Islands(piece.Loops))
                        group.Children.Add(Shape(Oriented(island)));
                group.Freeze();
                _groups.Add((g.Key.Role, g.Key.Material, group));
            }
            // Letters on top of rules, rules on top of nothing.
            _groups.Sort((a, b) => Order(a.Role).CompareTo(Order(b.Role)));
            if (!_fitted && ActualWidth > 0) { FitToView(); _fitted = true; }
            InvalidateVisual();
        }

        static int Order(PieceRole r) => r == PieceRole.Rule ? 0 : 1;

        /// <summary>Outer loop one way round, holes the other, so overlapping pieces add up instead of cancelling.</summary>
        static IEnumerable<Vec2[]> Oriented(List<Vec2[]> island)
        {
            for (int i = 0; i < island.Count; i++)
            {
                bool positive = Polygons.SignedArea(island[i]) > 0;
                bool wantPositive = i == 0;
                yield return positive == wantPositive ? island[i] : island[i].Reverse().ToArray();
            }
        }

        static Geometry Shape(IEnumerable<Vec2[]> loops)
        {
            var sg = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var ctx = sg.Open())
                foreach (var loop in loops)
                {
                    if (loop == null || loop.Length < 3) continue;
                    ctx.BeginFigure(new Point(loop[0].X, loop[0].Y), true, true);
                    ctx.PolyLineTo(loop.Skip(1).Select(p => new Point(p.X, p.Y)).ToList(), false, false);
                }
            sg.Freeze();
            return sg;
        }

        public void FitToView()
        {
            if (_geometry == null || ActualWidth < 10 || ActualHeight < 10) return;
            var r = _geometry.PanelRect.Include(_geometry.ContentBounds);
            if (r.IsEmpty) return;
            double margin = 40;
            double depthAllowance = ThreeD ? 60 : 0;
            _zoom = Math.Min((ActualWidth - 2 * margin) / Math.Max(r.Width + depthAllowance, 1), (ActualHeight - 2 * margin) / Math.Max(r.Height + depthAllowance, 1));
            _zoom = Math.Max(0.02, Math.Min(20, _zoom));
            _pan = new Vector(ActualWidth / 2 - r.Center.X * _zoom, ActualHeight / 2 - r.Center.Y * _zoom);
            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            if (!_fitted && _geometry != null) { FitToView(); _fitted = true; }
        }

        Matrix SignToScreen => new Matrix(_zoom, 0, 0, _zoom, _pan.X, _pan.Y);
        Point ToMm(Point screen) => new Point((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

        static Brush Fill(MaterialRef m, double shade = 1)
        {
            var c = m == null ? Color.FromRgb(60, 60, 60) : Color.FromRgb(m.R, m.G, m.B);
            var b = new SolidColorBrush(Color.FromRgb((byte)(c.R * shade), (byte)(c.G * shade), (byte)(c.B * shade)));
            b.Freeze();
            return b;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(new SolidColorBrush(WallColour), null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_geometry == null || _doc == null) return;
            var build = _doc.Build;

            if (ShowGrid) DrawGrid(dc);

            dc.PushTransform(new MatrixTransform(SignToScreen));
            bool panelOn = _doc.Panel.Enabled;
            double panelT = panelOn ? build.PanelThickness : 0;

            if (!ThreeD)
            {
                if (panelOn) dc.DrawGeometry(Fill(build.PanelMaterial), new Pen(Fill(build.PanelMaterial, 0.8), 1 / _zoom), _panel);
                else dc.DrawGeometry(null, Dashed(Color.FromArgb(90, 0, 0, 0)), _panel);
                foreach (var g in _groups) dc.DrawGeometry(Fill(build.For(g.Slot)), null, g.Shape);
            }
            else
            {
                // Oblique view from the upper left: deeper things shift up-left, shadows fall down-right.
                Vector depthDir = new Vector(-0.55, -0.4);
                void Extrude(Geometry shape, MaterialRef m, double back, double front, bool shadow)
                {
                    if (shadow)
                    {
                        var shadowBrush = new SolidColorBrush(Color.FromArgb(46, 0, 0, 0));
                        double lift = front - (panelOn ? panelT : 0);
                        for (int k = 1; k <= 3; k++)
                        {
                            var o = new Vector(0.35, 0.55) * Math.Max(lift, 2) * k / 3 + depthDir * (panelOn ? panelT : 0);
                            dc.PushTransform(new TranslateTransform(o.X, o.Y));
                            dc.DrawGeometry(shadowBrush, null, shape);
                            dc.Pop();
                        }
                    }
                    double depthPx = (front - back) * _zoom;
                    int steps = (int)Math.Max(1, Math.Min(40, Math.Ceiling(depthPx)));
                    var side = Fill(m, 0.62);
                    for (int i = 0; i < steps; i++)
                    {
                        var o = depthDir * (back + (front - back) * i / steps);
                        dc.PushTransform(new TranslateTransform(o.X, o.Y));
                        dc.DrawGeometry(side, null, shape);
                        dc.Pop();
                    }
                    var f = depthDir * front;
                    dc.PushTransform(new TranslateTransform(f.X, f.Y));
                    dc.DrawGeometry(Fill(m), new Pen(Fill(m, 0.85), 0.6 / _zoom), shape);
                    dc.Pop();
                }

                if (panelOn) Extrude(_panel, build.PanelMaterial, 0, Math.Max(panelT, 0.5), true);
                foreach (var g in _groups)
                {
                    bool rule = g.Role == PieceRole.Rule;
                    double back = panelT + (rule ? build.RuleStandoff : build.LetterStandoff);
                    double front = back + Math.Max(rule ? build.RuleDepth : build.LetterDepth, 0.5);
                    Extrude(g.Shape, build.For(g.Slot), back, front, true);
                }
            }

            // Frames and selection.
            foreach (var kv in _geometry.ElementBoxes)
            {
                bool selected = kv.Key == SelectedId;
                if (!selected && _doc.Rules.Any(r => r.Id == kv.Key)) continue;
                var box = Shape(new[] { kv.Value });
                dc.DrawGeometry(null, selected ? Dashed(Color.FromRgb(0, 120, 215), 1.6) : Dashed(Color.FromArgb(60, 0, 90, 200)), box);
            }
            dc.Pop();

            // Size label.
            var pr = _geometry.PanelRect;
            var label = new FormattedText($"{pr.Width:0} × {pr.Height:0} mm" + (panelOn ? $"  ·  panel {build.PanelThickness:0.#} mm" : "  ·  no backpanel"),
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.DimGray, 1.0);
            dc.DrawText(label, new Point(10, ActualHeight - 22));
        }

        Pen Dashed(Color c, double px = 1)
        {
            var pen = new Pen(new SolidColorBrush(c), px / _zoom) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            pen.Freeze();
            return pen;
        }

        void DrawGrid(DrawingContext dc)
        {
            // 10 mm minor / 100 mm major grid, thinned out when zoomed out.
            double minor = 10;
            while (minor * _zoom < 8) minor *= 10;
            var pMinor = new Pen(new SolidColorBrush(Color.FromArgb(22, 0, 0, 0)), 1);
            var pMajor = new Pen(new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)), 1);
            var a = ToMm(new Point(0, 0)); var b = ToMm(new Point(ActualWidth, ActualHeight));
            for (double x = Math.Floor(a.X / minor) * minor; x <= b.X; x += minor)
            {
                double sx = x * _zoom + _pan.X;
                dc.DrawLine(Math.Abs(x % (minor * 10)) < 1e-6 ? pMajor : pMinor, new Point(sx, 0), new Point(sx, ActualHeight));
            }
            for (double y = Math.Floor(a.Y / minor) * minor; y <= b.Y; y += minor)
            {
                double sy = y * _zoom + _pan.Y;
                dc.DrawLine(Math.Abs(y % (minor * 10)) < 1e-6 ? pMajor : pMinor, new Point(0, sy), new Point(ActualWidth, sy));
            }
        }

        string HitTest(Point mm)
        {
            if (_geometry == null) return null;
            var p = new Vec2(mm.X, mm.Y);
            if (SelectedId != null && _geometry.ElementBoxes.TryGetValue(SelectedId, out var sel) && Polygons.Contains(sel, p)) return SelectedId;
            // Rules first (they are thin), then the smallest frame that contains the point.
            foreach (var r in _doc.Rules)
                if (_geometry.ElementBoxes.TryGetValue(r.Id, out var box) && Polygons.Contains(box, p)) return r.Id;
            return _geometry.ElementBoxes
                .Where(kv => Polygons.Contains(kv.Value, p))
                .OrderBy(kv => Math.Abs(Polygons.SignedArea(kv.Value)))
                .Select(kv => kv.Key).FirstOrDefault();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            var pos = e.GetPosition(this);
            if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
            {
                _panning = true; _panStart = pos; _panAtStart = _pan;
                CaptureMouse();
                Cursor = Cursors.ScrollAll;
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;
            var mm = ToMm(pos);
            var id = HitTest(mm);
            if (e.ClickCount == 2 && id != null) { ElementDoubleClicked?.Invoke(id); return; }
            ElementClicked?.Invoke(id);
            if (id != null)
            {
                _dragId = id; _dragStartMm = mm; _lastMm = mm; _dragging = false;
                CaptureMouse();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var pos = e.GetPosition(this);
            PointerMoved?.Invoke(ToMm(pos));
            if (_panning)
            {
                _pan = _panAtStart + (pos - _panStart);
                InvalidateVisual();
                return;
            }
            if (_dragId == null || e.LeftButton != MouseButtonState.Pressed) return;
            var mm = ToMm(pos);
            if (!_dragging)
            {
                if ((pos - new Point(_dragStartMm.X * _zoom + _pan.X, _dragStartMm.Y * _zoom + _pan.Y)).Length < 4) return;
                _dragging = true;
                DragStarted?.Invoke(_dragId);
            }
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            double dx = Math.Round((mm.X - _dragStartMm.X) / step) * step;
            double dy = Math.Round((mm.Y - _dragStartMm.Y) / step) * step;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0; }
            Dragging?.Invoke(_dragId, dx, dy);
            _lastMm = mm;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right))
            {
                _panning = false; ReleaseMouseCapture(); Cursor = Cursors.Arrow; return;
            }
            if (e.ChangedButton == MouseButton.Left)
            {
                _dragId = null; _dragging = false;
                if (IsMouseCaptured) ReleaseMouseCapture();
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(this);
            var mm = ToMm(pos);
            double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            _zoom = Math.Max(0.02, Math.Min(20, _zoom * factor));
            _pan = new Vector(pos.X - mm.X * _zoom, pos.Y - mm.Y * _zoom);
            InvalidateVisual();
            e.Handled = true;
        }
    }
}

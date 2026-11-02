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
    ///
    /// Drawn in layers so the mouse stays responsive: the sign is drawn once (in millimetres) and only moved or
    /// scaled when you pan or zoom; what you are dragging sits on its own layer that just slides; selection
    /// outlines, the selection box, labels and the scale bar are a light overlay redrawn on every mouse move.
    /// </summary>
    internal sealed class PreviewCanvas : FrameworkElement
    {
        SignGeometry _geometry;
        SignDocument _doc;
        readonly List<(PieceRole Role, MaterialRef Mat, Geometry Shape, bool Live)> _groups = new List<(PieceRole, MaterialRef, Geometry, bool)>();
        Geometry _panel;

        readonly DrawingVisual _gridV = new DrawingVisual(), _signV = new DrawingVisual(), _liveV = new DrawingVisual(), _overlayV = new DrawingVisual();
        readonly VisualCollection _visuals;

        // Being dragged: drawn on the live layer, shifted by the offset (mm) until the drag ends.
        HashSet<string> _liveIds = new HashSet<string>();
        Vector _liveOffset;

        double _zoom = 0.4;            // screen pixels per mm
        Vector _pan = new Vector(40, 40);
        bool _fitted;

        string _dragId;
        Point _dragStartMm, _lastMm;
        bool _dragging, _panning;
        Point _panStart;
        Vector _panAtStart;

        public string SelectedId { get; set; }
        /// <summary>Everything selected, for the highlight.</summary>
        public ICollection<string> MultiIds { get; set; } = new HashSet<string>();
        /// <summary>A selection box was dragged over empty space: the elements it touches (true = Shift, add to the selection).</summary>
        public event Action<List<string>, bool> MarqueeSelected;

        bool _marquee;
        Point _marqueeStart, _marqueeEnd; // mm
        public bool ThreeD { get; set; }
        public bool ShowGrid { get; set; } = true;
        /// <summary>Grid spacing in mm (minor lines; every 10th line is darker).</summary>
        public double GridMm { get; set; } = 10;
        /// <summary>True while the current drag started with Alt held (the window drags a copy).</summary>
        public bool DragIsCopy { get; private set; }
        public Color WallColour { get; set; } = Color.FromRgb(214, 210, 203);

        public event Action<string> ElementClicked;
        public event Action<string> ElementDoubleClicked;
        /// <summary>A drag is starting. The window may answer with <see cref="RetargetDrag"/> (Alt-drag drags a copy).</summary>
        public event Action<string> DragStarted;
        public event Action DragEnded;
        public event Action ZoomChanged;
        /// <summary>After the sign is redrawn, with <see cref="LastDrawMs"/> set.</summary>
        public event Action Drawn;
        public double Zoom => _zoom;
        public long LastDrawMs { get; private set; }
        /// <summary>Total movement in mm since the drag started (snapped to whole millimetres).</summary>
        public event Action<string, double, double> Dragging;
        public event Action<Point> PointerMoved;

        /// <summary>Drags another element from here on, e.g. the copy made by Alt-drag.</summary>
        public void RetargetDrag(string id) { if (_dragId != null && id != null) _dragId = id; }

        public PreviewCanvas()
        {
            ClipToBounds = true;
            Focusable = true;
            Cursor = Cursors.Arrow;
            _visuals = new VisualCollection(this) { _gridV, _signV, _liveV, _overlayV };
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];

        public void Show(SignDocument doc, SignGeometry geometry)
        {
            _doc = doc;
            _geometry = geometry;
            BuildGroups();
            if (!_fitted && ActualWidth > 0) { _fitted = true; FitToView(); }
            Redraw();
        }

        void BuildGroups()
        {
            _groups.Clear();
            if (_geometry == null || _doc == null) return;
            _panel = Shape(new[] { _geometry.PanelOutline });
            // One drawing group per role and material: each layer shows in its own material's colour.
            foreach (var g in _geometry.Pieces.GroupBy(p =>
            {
                var m = _doc.MaterialFor(p.ElementId, p.Material) ?? new MaterialRef("?", 60, 60, 60);
                return (p.Role, Key: $"{m.Name}|{m.R},{m.G},{m.B}", Live: p.ElementId != null && _liveIds.Contains(p.ElementId));
            }))
            {
                var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                foreach (var piece in g)
                    foreach (var island in SignBuilder.Islands(piece.Loops))
                        group.Children.Add(Shape(Oriented(island)));
                group.Freeze();
                var first = g.First();
                _groups.Add((g.Key.Role, _doc.MaterialFor(first.ElementId, first.Material), group, g.Key.Live));
            }
            // Letters on top of rules, rules on top of nothing.
            _groups.Sort((a, b) => Order(a.Role).CompareTo(Order(b.Role)));
        }

        /// <summary>Redraws everything (after a change to the sign, the 3D switch or the grid).</summary>
        public void Redraw()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            DrawSignLayer(_signV, live: false);
            DrawSignLayer(_liveV, live: true);
            LastDrawMs = clock.ElapsedMilliseconds;
            ApplyView();
            Drawn?.Invoke();
        }

        /// <summary>Only the selection outlines changed: redraws just the light overlay.</summary>
        public void RefreshSelection() => DrawOverlay();

        /// <summary>The things being dragged move on their own layer, so the rest is not redrawn.</summary>
        public void BeginLive(IEnumerable<string> ids)
        {
            _liveIds = new HashSet<string>(ids ?? Enumerable.Empty<string>());
            _liveOffset = new Vector();
            BuildGroups();
            Redraw();
        }

        public void SetLiveOffset(double dx, double dy)
        {
            _liveOffset = new Vector(dx, dy);
            ApplyView();
        }

        public void EndLive()
        {
            _liveIds = new HashSet<string>();
            _liveOffset = new Vector();
        }

        static int Order(PieceRole r) => r == PieceRole.Rule ? 0 : 1;

        /// <summary>Outer loop one way round, holes the other, so overlapping pieces add up instead of cancelling.</summary>
        static IEnumerable<Vec2[]> Oriented(List<Vec2[]> island)
        {
            for (int i = 0; i < island.Count; i++)
            {
                bool positive = Polygons.SignedArea(island[i]) > 0;
                bool wantPositive = i == 0;
                yield return positive == wantPositive ? island[i] : Enumerable.Reverse(island[i]).ToArray();
            }
        }

        static Geometry Shape(IEnumerable<Vec2[]> loops, Func<Vec2, Point> map = null)
        {
            map = map ?? (v => new Point(v.X, v.Y));
            var sg = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var ctx = sg.Open())
                foreach (var loop in loops)
                {
                    if (loop == null || loop.Length < 3) continue;
                    ctx.BeginFigure(map(loop[0]), true, true);
                    ctx.PolyLineTo(loop.Skip(1).Select(map).ToList(), false, false);
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
            ApplyView();
            ZoomChanged?.Invoke();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            if (!_fitted && _geometry != null) { _fitted = true; FitToView(); }
            ApplyView();
        }

        Matrix SignToScreen => new Matrix(_zoom, 0, 0, _zoom, _pan.X, _pan.Y);
        Point ToMm(Point screen) => new Point((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);
        Point ToScreen(Vec2 mm) => new Point(mm.X * _zoom + _pan.X, mm.Y * _zoom + _pan.Y);

        static readonly Dictionary<int, Brush> Brushes_ = new Dictionary<int, Brush>();

        /// <summary>One frozen brush per colour and shade, reused on every redraw.</summary>
        static Brush Fill(MaterialRef m, double shade = 1)
        {
            var c = m == null ? Color.FromRgb(60, 60, 60) : Color.FromRgb(m.R, m.G, m.B);
            var shaded = Color.FromRgb((byte)(c.R * shade), (byte)(c.G * shade), (byte)(c.B * shade));
            int key = (shaded.R << 16) | (shaded.G << 8) | shaded.B;
            if (Brushes_.TryGetValue(key, out var b)) return b;
            b = new SolidColorBrush(shaded);
            b.Freeze();
            Brushes_[key] = b;
            return b;
        }

        static readonly Brush ShadowBrush = Frozen(new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)));
        static readonly Brush MarqueeBrush = Frozen(new SolidColorBrush(Color.FromArgb(28, 0, 120, 215)));
        static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        /// <summary>Only the wall colour: it is also what catches the mouse.</summary>
        protected override void OnRender(DrawingContext dc) =>
            dc.DrawRectangle(Fill(new MaterialRef("wall", WallColour.R, WallColour.G, WallColour.B)), null, new Rect(0, 0, ActualWidth, ActualHeight));

        /// <summary>Pan and zoom only move the drawn layers; the grid and overlay follow.</summary>
        void ApplyView()
        {
            var m = SignToScreen;
            _signV.Transform = new MatrixTransform(m);
            var live = Matrix.Identity;
            live.Translate(_liveOffset.X, _liveOffset.Y);
            live.Append(m);
            _liveV.Transform = new MatrixTransform(live);
            DrawGridLayer();
            DrawOverlay();
        }

        /// <summary>The sign in millimetres: the static part, or what is being dragged.</summary>
        void DrawSignLayer(DrawingVisual visual, bool live)
        {
            using (var dc = visual.RenderOpen())
            {
                if (_geometry == null || _doc == null) return;
                var build = _doc.Build;
                bool panelOn = _doc.Panel.Enabled;
                double panelT = panelOn ? build.PanelThickness : 0;
                var groups = _groups.Where(g => g.Live == live).ToList();

                if (!ThreeD)
                {
                    if (!live && panelOn) dc.DrawGeometry(Fill(build.PanelMaterial), null, _panel);
                    foreach (var g in groups) dc.DrawGeometry(Fill(g.Mat), null, g.Shape);
                    return;
                }

                // Oblique view from the upper left: deeper things shift up-left, shadows fall down-right.
                Vector depthDir = new Vector(-0.55, -0.4);
                void Extrude(Geometry shape, MaterialRef m, double back, double front)
                {
                    double lift = front - (panelOn ? panelT : 0);
                    for (int k = 1; k <= 3; k++)
                    {
                        var o = new Vector(0.35, 0.55) * Math.Max(lift, 2) * k / 3 + depthDir * (panelOn ? panelT : 0);
                        dc.PushTransform(new TranslateTransform(o.X, o.Y));
                        dc.DrawGeometry(ShadowBrush, null, shape);
                        dc.Pop();
                    }
                    int steps = (int)Math.Max(2, Math.Min(10, Math.Ceiling((front - back) / 2)));
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
                    dc.DrawGeometry(Fill(m), null, shape);
                    dc.Pop();
                }

                if (!live && panelOn) Extrude(_panel, build.PanelMaterial, 0, Math.Max(panelT, 0.5));
                foreach (var g in groups)
                {
                    bool rule = g.Role == PieceRole.Rule;
                    double back = panelT + (rule ? build.RuleStandoff : build.LetterStandoff);
                    double front = back + Math.Max(rule ? build.RuleDepth : build.LetterDepth, 0.5);
                    Extrude(g.Shape, g.Mat, back, front);
                }
            }
        }

        /// <summary>Selection outlines, the selection box, the size label and the scale bar, in screen pixels.</summary>
        void DrawOverlay()
        {
            using (var dc = _overlayV.RenderOpen())
            {
                if (_geometry == null || _doc == null) return;
                var build = _doc.Build;
                bool panelOn = _doc.Panel.Enabled;
                if (!panelOn && !ThreeD) dc.DrawGeometry(null, Dashed(Color.FromArgb(90, 0, 0, 0)), Shape(new[] { _geometry.PanelOutline }, ToScreen));

                foreach (var kv in _geometry.ElementBoxes)
                {
                    bool selected = kv.Key == SelectedId || (MultiIds != null && MultiIds.Contains(kv.Key));
                    bool locked = _doc.IsLocked(kv.Key);
                    if (!selected && (locked || _doc.Rules.Any(r => r.Id == kv.Key))) continue;
                    var shift = _liveIds.Contains(kv.Key) ? new Vec2(_liveOffset.X, _liveOffset.Y) : new Vec2(0, 0);
                    var box = Shape(new[] { kv.Value }, v => ToScreen(v + shift));
                    dc.DrawGeometry(null, !selected ? Dashed(Color.FromArgb(60, 0, 90, 200))
                                          : locked ? Dashed(Color.FromRgb(150, 150, 150), 1.6) : Dashed(Color.FromRgb(0, 120, 215), 1.6), box);
                }
                if (_marquee)
                    dc.DrawRectangle(MarqueeBrush, Dashed(Color.FromRgb(0, 120, 215), 1),
                        new Rect(ToScreen(new Vec2(_marqueeStart.X, _marqueeStart.Y)), ToScreen(new Vec2(_marqueeEnd.X, _marqueeEnd.Y))));

                var pr = _geometry.PanelRect;
                var label = new FormattedText($"{pr.Width:0} × {pr.Height:0} mm" + (panelOn ? $"  ·  panel {build.PanelThickness:0.#} mm" : "  ·  no backpanel"),
                    CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.DimGray, 1.0);
                dc.DrawText(label, new Point(10, ActualHeight - 22));
                DrawScaleBar(dc);
            }
        }

        /// <summary>A scale bar in the bottom-right corner, a round length (1, 2 or 5 × 10ⁿ mm) about 120 px long.</summary>
        void DrawScaleBar(DrawingContext dc)
        {
            if (_zoom <= 0 || ActualWidth < 200) return;
            double target = 120 / _zoom, length = 1;
            double p10 = Math.Pow(10, Math.Floor(Math.Log10(target)));
            foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 }) if (m * p10 <= target) length = m * p10;
            double px = length * _zoom;
            string text = length >= 1000 ? $"{length / 1000:0.##} m" : $"{length:0.##} mm";

            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Black, 1.0);
            double right = ActualWidth - 14 - ft.Width / 2, bottom = ActualHeight - 14, h = 6, left = right - px;
            var zero = new FormattedText("0", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Black, 1.0);
            var back = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            dc.DrawRoundedRectangle(back, null, new Rect(left - 10, bottom - h - ft.Height - 6, px + 20 + ft.Width / 2, h + ft.Height + 12), 3, 3);
            var pen = new Pen(Brushes.Black, 1);
            for (int i = 0; i < 4; i++)
                dc.DrawRectangle(i % 2 == 0 ? Brushes.Black : Brushes.White, pen, new Rect(left + px * i / 4, bottom - h, px / 4, h));
            dc.DrawText(zero, new Point(left - zero.Width / 2, bottom - h - zero.Height - 1));
            dc.DrawText(ft, new Point(right - ft.Width / 2, bottom - h - ft.Height - 1));
        }

        /// <summary>A dashed pen, its width in screen pixels.</summary>
        static Pen Dashed(Color c, double px = 1)
        {
            var pen = new Pen(new SolidColorBrush(c), px) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            pen.Freeze();
            return pen;
        }

        void DrawGridLayer()
        {
            using (var dc = _gridV.RenderOpen())
            {
                if (!ShowGrid || _zoom <= 0) return;
                // Minor lines at the grid spacing, every 10th darker; thinned out when zoomed out.
                double minor = GridMm > 0 ? GridMm : 10;
                while (minor * _zoom < 6) minor *= 10;
                var pMinor = new Pen(new SolidColorBrush(Color.FromArgb(22, 0, 0, 0)), 1); pMinor.Freeze();
                var pMajor = new Pen(new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)), 1); pMajor.Freeze();
                var a = ToMm(new Point(0, 0)); var b = ToMm(new Point(ActualWidth, ActualHeight));
                for (long i = (long)Math.Floor(a.X / minor); i * minor <= b.X; i++)
                {
                    double sx = i * minor * _zoom + _pan.X;
                    dc.DrawLine(i % 10 == 0 ? pMajor : pMinor, new Point(sx, 0), new Point(sx, ActualHeight));
                }
                for (long i = (long)Math.Floor(a.Y / minor); i * minor <= b.Y; i++)
                {
                    double sy = i * minor * _zoom + _pan.Y;
                    dc.DrawLine(i % 10 == 0 ? pMajor : pMinor, new Point(0, sy), new Point(ActualWidth, sy));
                }
            }
        }

        string HitTest(Point mm)
        {
            if (_geometry == null) return null;
            var p = new Vec2(mm.X, mm.Y);
            // Locked elements are not picked: clicks go through to what is underneath.
            if (SelectedId != null && !_doc.IsLocked(SelectedId) && _geometry.ElementBoxes.TryGetValue(SelectedId, out var sel) && Polygons.Contains(sel, p)) return SelectedId;
            // Rules first (they are thin), then the smallest frame that contains the point.
            foreach (var r in _doc.Rules)
                if (!r.Locked && _geometry.ElementBoxes.TryGetValue(r.Id, out var box) && Polygons.Contains(box, p)) return r.Id;
            return _geometry.ElementBoxes
                .Where(kv => !_doc.IsLocked(kv.Key) && Polygons.Contains(kv.Value, p))
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
            if (id != null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) ElementClicked?.Invoke(id);
            if (id != null)
            {
                _dragId = id; _dragStartMm = mm; _lastMm = mm; _dragging = false;
                CaptureMouse();
            }
            else
            {
                // Empty space: drag a selection box.
                _marquee = true; _marqueeStart = _marqueeEnd = mm;
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
                ApplyView();
                return;
            }
            if (_marquee)
            {
                if (e.LeftButton != MouseButtonState.Pressed) { _marquee = false; DrawOverlay(); return; }
                _marqueeEnd = ToMm(pos);
                DrawOverlay();
                return;
            }
            if (_dragId == null || e.LeftButton != MouseButtonState.Pressed) return;
            var mm = ToMm(pos);
            if (!_dragging)
            {
                if ((pos - new Point(_dragStartMm.X * _zoom + _pan.X, _dragStartMm.Y * _zoom + _pan.Y)).Length < 4) return;
                _dragging = true;
                DragIsCopy = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
                if (DragIsCopy) Cursor = Cursors.Cross;
                DragStarted?.Invoke(_dragId);
            }
            // Like InDesign: Shift keeps the move horizontal or vertical (whichever is larger). Ctrl = 10 mm steps.
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 10 : 1;
            double dx = Math.Round((mm.X - _dragStartMm.X) / step) * step;
            double dy = Math.Round((mm.Y - _dragStartMm.Y) / step) * step;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0; }
            Dragging?.Invoke(_dragId, dx, dy);
            _lastMm = mm;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right))
            {
                _panning = false; ReleaseMouseCapture(); Cursor = Cursors.Arrow; return;
            }
            if (e.ChangedButton == MouseButton.Left && _marquee)
            {
                _marquee = false;
                if (IsMouseCaptured) ReleaseMouseCapture();
                var r = new Rect(_marqueeStart, _marqueeEnd);
                DrawOverlay();
                if (r.Width * _zoom < 4 && r.Height * _zoom < 4 || _geometry == null) return; // a click, not a box
                var hits = _geometry.ElementBoxes
                    .Where(kv => !_doc.IsLocked(kv.Key) && Touches(kv.Value, r))
                    .Select(kv => kv.Key).ToList();
                MarqueeSelected?.Invoke(hits, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                return;
            }
            if (e.ChangedButton == MouseButton.Left)
            {
                bool wasDragging = _dragging;
                _dragId = null; _dragging = false; DragIsCopy = false;
                if (wasDragging) DragEnded?.Invoke();
                Cursor = Cursors.Arrow;
                if (IsMouseCaptured) ReleaseMouseCapture();
            }
        }

        /// <summary>True when an element's outline overlaps the box (touching is enough, as in InDesign).</summary>
        static bool Touches(Vec2[] outline, Rect box)
        {
            var b = Rect2.Of(outline);
            return b.MaxX >= box.Left && b.MinX <= box.Right && b.MaxY >= box.Top && b.MinY <= box.Bottom;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(this);
            var mm = ToMm(pos);
            double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            _zoom = Math.Max(0.02, Math.Min(20, _zoom * factor));
            _pan = new Vector(pos.X - mm.X * _zoom, pos.Y - mm.Y * _zoom);
            ApplyView();
            ZoomChanged?.Invoke();
            e.Handled = true;
        }
    }
}

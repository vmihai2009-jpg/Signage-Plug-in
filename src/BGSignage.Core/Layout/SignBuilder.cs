using System;
using System.Collections.Generic;
using System.Linq;
using BGSignage.Core.Geometry;
using BGSignage.Core.Model;

namespace BGSignage.Core.Layout
{
    /// <summary>Everything the preview draws and the Revit family is built from.</summary>
    public sealed class SignGeometry
    {
        /// <summary>The backpanel's extent (always present: when the design has no panel it is the content plus padding,
        /// so the family can still switch a panel on).</summary>
        public Rect2 PanelRect { get; set; }
        public Vec2[] PanelOutline { get; set; }
        public List<Piece> Pieces { get; } = new List<Piece>();
        /// <summary>Outline of each frame and rule, by id, for selecting and dragging in the preview.</summary>
        public Dictionary<string, Vec2[]> ElementBoxes { get; } = new Dictionary<string, Vec2[]>();
        public Rect2 ContentBounds { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>The family origin on the sign, from <see cref="Construction.Origin"/>.</summary>
        public Vec2 Origin(OriginPoint origin)
        {
            var r = PanelRect;
            switch (origin)
            {
                case OriginPoint.BottomCenter: return new Vec2(r.Center.X, r.MaxY);
                case OriginPoint.BottomLeft: return new Vec2(r.MinX, r.MaxY);
                case OriginPoint.TopLeft: return new Vec2(r.MinX, r.MinY);
                default: return r.Center;
            }
        }
    }

    public static class SignBuilder
    {
        public static SignGeometry Build(SignDocument doc, IFontProvider fonts, double toleranceMm = 0.2)
        {
            doc.Normalise();
            var g = new SignGeometry();
            foreach (var frame in doc.Frames)
            {
                var fl = TextLayout.Layout(frame, fonts, toleranceMm);
                g.Pieces.AddRange(fl.Pieces);
                g.ElementBoxes[frame.Id] = fl.Box;
                foreach (var w in fl.Warnings) if (!g.Warnings.Contains(w)) g.Warnings.Add(w);
            }
            foreach (var rule in doc.Rules)
            {
                var (a, b) = RuleEnds(rule);
                var bar = Polygons.Bar(a, b, Math.Max(rule.WeightMm, 0.1));
                g.Pieces.Add(new Piece
                {
                    Role = rule.Material == MaterialSlot.Rules ? PieceRole.Rule : PieceRole.Letter,
                    Material = rule.Material, Label = rule.Name, ElementId = rule.Id, Loops = { bar },
                });
                // A thin rule is hard to click: give it a wider pick box.
                g.ElementBoxes[rule.Id] = Polygons.Bar(a, b, Math.Max(rule.WeightMm, 12));
            }

            foreach (var graphic in doc.Graphics) AddGraphic(g, graphic);

            var content = Rect2.Empty;
            foreach (var p in g.Pieces) content = content.Include(p.Bounds);
            g.ContentBounds = content;

            var panel = doc.Panel;
            Rect2 rect;
            if (panel.AutoFit || !panel.Enabled)
                rect = content.IsEmpty ? new Rect2(0, 0, Math.Max(panel.Width, 10), Math.Max(panel.Height, 10))
                                       : content.Inflate(Math.Max(0, panel.PaddingX), Math.Max(0, panel.PaddingY));
            else
                rect = new Rect2(0, 0, Math.Max(panel.Width, 1), Math.Max(panel.Height, 1));
            g.PanelRect = rect;
            g.PanelOutline = panel.Shape == PanelShape.Ellipse ? Polygons.Ellipse(rect) : Polygons.RoundedRectangle(rect, panel.CornerRadius);
            return g;
        }

        /// <summary>The transform from a graphic's own coordinates to the sign.</summary>
        public static Affine GraphicTransform(GraphicElement e)
        {
            double s = Math.Max(e.ScalePercent, 0.01) / 100;
            var t = Affine.Scale(e.Mirror ? -s : s, s);
            if (e.Mirror) t = t.Then(Affine.Translate(e.NativeWidth * s, 0));
            return t.Then(Affine.Rotate(e.RotationDeg)).Then(Affine.Translate(e.X, e.Y));
        }

        static void AddGraphic(SignGeometry g, GraphicElement e)
        {
            var t = GraphicTransform(e);
            var role = e.Material == MaterialSlot.Rules ? PieceRole.Rule : PieceRole.Letter;
            var closed = e.Paths.Where(p => p.Closed).Select(p => t.Apply(p.ToVec2())).Where(l => l.Length >= 3).ToList();
            // Closed shapes nested inside others are holes (even-odd), as in a drawing's hatch or a logo.
            foreach (var island in Islands(closed))
                g.Pieces.Add(new Piece { Role = role, Material = e.Material, Label = e.Name, ElementId = e.Id, Loops = island });
            if (e.StrokeMm > 0)
                foreach (var path in e.Paths.Where(p => !p.Closed))
                {
                    var pts = t.Apply(path.ToVec2());
                    for (int i = 0; i + 1 < pts.Length; i++)
                        if (Vec2.Dist(pts[i], pts[i + 1]) > 1e-6)
                            g.Pieces.Add(new Piece { Role = role, Material = e.Material, Label = e.Name + " line", ElementId = e.Id, Loops = { Polygons.Bar(pts[i], pts[i + 1], e.StrokeMm) } });
                }
            var box = t.Apply(Polygons.Rectangle(0, 0, Math.Max(e.NativeWidth, 1), Math.Max(e.NativeHeight, 1)));
            g.ElementBoxes[e.Id] = box;
        }

        /// <summary>The two ends of a rule's centre line in sign coordinates (y down; angles anticlockwise).</summary>
        public static (Vec2, Vec2) RuleEnds(RuleElement r)
        {
            double a = r.AngleDeg * Math.PI / 180;
            var start = new Vec2(r.X, r.Y);
            return (start, start + new Vec2(Math.Cos(a), -Math.Sin(a)) * r.Length);
        }

        /// <summary>Groups each piece's loops into separate solids: an outer loop with the holes directly inside it.</summary>
        public static List<List<Vec2[]>> Islands(IReadOnlyList<Vec2[]> loops)
        {
            var list = loops.Where(l => l != null && l.Length >= 3).ToList();
            int n = list.Count;
            var depth = new int[n];
            var parent = Enumerable.Repeat(-1, n).ToArray();
            var area = list.Select(l => Math.Abs(Polygons.SignedArea(l))).ToArray();
            for (int i = 0; i < n; i++)
            {
                double best = double.PositiveInfinity;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || area[j] <= area[i]) continue;
                    if (!Polygons.Contains(list[j], list[i][0])) continue;
                    depth[i]++;
                    if (area[j] < best) { best = area[j]; parent[i] = j; }
                }
            }
            var islands = new Dictionary<int, List<Vec2[]>>();
            for (int i = 0; i < n; i++)
                if (depth[i] % 2 == 0) islands[i] = new List<Vec2[]> { list[i] };
            for (int i = 0; i < n; i++)
                if (depth[i] % 2 == 1 && parent[i] >= 0 && islands.TryGetValue(parent[i], out var isl)) isl.Add(list[i]);
            return islands.Values.ToList();
        }
    }
}

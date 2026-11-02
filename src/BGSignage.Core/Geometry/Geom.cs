using System;
using System.Collections.Generic;

namespace BGSignage.Core.Geometry
{
    /// <summary>A 2D point or vector in millimetres. Sign coordinates: x to the right, y DOWN (like a page).</summary>
    public struct Vec2 : IEquatable<Vec2>
    {
        public double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
        public double Length => Math.Sqrt(X * X + Y * Y);
        public static double Dist(Vec2 a, Vec2 b) => (a - b).Length;
        public bool Equals(Vec2 o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() * 31 + Y.GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    /// <summary>2D affine transform: x' = A x + C y + E, y' = B x + D y + F.</summary>
    public struct Affine
    {
        public double A, B, C, D, E, F;
        public Affine(double a, double b, double c, double d, double e, double f) { A = a; B = b; C = c; D = d; E = e; F = f; }

        public static Affine Identity => new Affine(1, 0, 0, 1, 0, 0);
        public static Affine Translate(double x, double y) => new Affine(1, 0, 0, 1, x, y);
        public static Affine Scale(double sx, double sy) => new Affine(sx, 0, 0, sy, 0, 0);

        /// <summary>
        /// Rotation that LOOKS anticlockwise on screen for a positive angle (as in InDesign),
        /// in y-down sign coordinates.
        /// </summary>
        public static Affine Rotate(double degrees)
        {
            double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            return new Affine(c, -s, s, c, 0, 0);
        }

        /// <summary>Rotation about a point (anticlockwise on screen for a positive angle).</summary>
        public static Affine RotateAbout(double degrees, Vec2 p) =>
            Translate(-p.X, -p.Y).Then(Rotate(degrees)).Then(Translate(p.X, p.Y));

        /// <summary>Slant like italic: a positive angle leans the tops of letters to the right (y-down, baseline at y=0).</summary>
        public static Affine Skew(double degrees) => new Affine(1, 0, -Math.Tan(degrees * Math.PI / 180), 1, 0, 0);

        /// <summary>This transform followed by <paramref name="next"/>.</summary>
        public Affine Then(Affine next) => new Affine(
            next.A * A + next.C * B,
            next.B * A + next.D * B,
            next.A * C + next.C * D,
            next.B * C + next.D * D,
            next.A * E + next.C * F + next.E,
            next.B * E + next.D * F + next.F);

        public Vec2 Apply(Vec2 p) => new Vec2(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

        public Vec2[] Apply(IReadOnlyList<Vec2> loop)
        {
            var r = new Vec2[loop.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Apply(loop[i]);
            return r;
        }

        /// <summary>True when the transform mirrors (flips) shapes, which reverses loop direction.</summary>
        public bool Mirrors => A * D - B * C < 0;
    }

    public struct Rect2
    {
        public double MinX, MinY, MaxX, MaxY;
        public Rect2(double minX, double minY, double maxX, double maxY) { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        public static Rect2 Empty => new Rect2(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
        public bool IsEmpty => MinX > MaxX || MinY > MaxY;
        public double Width => IsEmpty ? 0 : MaxX - MinX;
        public double Height => IsEmpty ? 0 : MaxY - MinY;
        public Vec2 Center => new Vec2((MinX + MaxX) / 2, (MinY + MaxY) / 2);

        public Rect2 Include(Vec2 p) => new Rect2(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));
        public Rect2 Include(Rect2 r) => r.IsEmpty ? this : IsEmpty ? r : new Rect2(Math.Min(MinX, r.MinX), Math.Min(MinY, r.MinY), Math.Max(MaxX, r.MaxX), Math.Max(MaxY, r.MaxY));
        public Rect2 Inflate(double dx, double dy) => IsEmpty ? this : new Rect2(MinX - dx, MinY - dy, MaxX + dx, MaxY + dy);
        public bool Contains(Vec2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

        public static Rect2 Of(IEnumerable<Vec2> pts)
        {
            var r = Empty;
            foreach (var p in pts) r = r.Include(p);
            return r;
        }
        public override string ToString() => $"[{MinX:0.##},{MinY:0.##} - {MaxX:0.##},{MaxY:0.##}]";
    }

    public static class Polygons
    {
        /// <summary>Signed area (positive when the loop runs anticlockwise in y-up terms).</summary>
        public static double SignedArea(IReadOnlyList<Vec2> loop)
        {
            double a = 0;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                a += (loop[j].X * loop[i].Y) - (loop[i].X * loop[j].Y);
            return a / 2;
        }

        public static Vec2[] Rectangle(double x0, double y0, double x1, double y1) => new[]
        {
            new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1)
        };

        /// <summary>A bar of the given thickness centred on the segment a-b (square ends).</summary>
        public static Vec2[] Bar(Vec2 a, Vec2 b, double thickness)
        {
            var d = b - a;
            double len = d.Length;
            if (len <= 0) return Rectangle(a.X, a.Y - thickness / 2, a.X, a.Y + thickness / 2);
            var n = new Vec2(-d.Y / len, d.X / len) * (thickness / 2);
            return new[] { a + n, b + n, b - n, a - n };
        }

        /// <summary>A rectangle with rounded corners, as a polyline (arcs split into short segments).</summary>
        public static Vec2[] RoundedRectangle(Rect2 r, double radius, int segmentsPerCorner = 8)
        {
            radius = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
            if (radius < 1e-6) return Rectangle(r.MinX, r.MinY, r.MaxX, r.MaxY);
            var pts = new List<Vec2>();
            void Corner(double cx, double cy, double startDeg)
            {
                for (int i = 0; i <= segmentsPerCorner; i++)
                {
                    double t = (startDeg + 90.0 * i / segmentsPerCorner) * Math.PI / 180;
                    pts.Add(new Vec2(cx + radius * Math.Cos(t), cy + radius * Math.Sin(t)));
                }
            }
            Corner(r.MaxX - radius, r.MinY + radius, 270);
            Corner(r.MaxX - radius, r.MaxY - radius, 0);
            Corner(r.MinX + radius, r.MaxY - radius, 90);
            Corner(r.MinX + radius, r.MinY + radius, 180);
            return pts.ToArray();
        }

        public static Vec2[] Ellipse(Rect2 r, int segments = 72)
        {
            var c = r.Center;
            double rx = r.Width / 2, ry = r.Height / 2;
            var pts = new Vec2[segments];
            for (int i = 0; i < segments; i++)
            {
                double t = 2 * Math.PI * i / segments;
                pts[i] = new Vec2(c.X + rx * Math.Cos(t), c.Y + ry * Math.Sin(t));
            }
            return pts;
        }

        /// <summary>
        /// Prepares a loop for Revit: removes near-straight points (Douglas-Peucker with <paramref name="flatness"/>)
        /// and then any point closer than <paramref name="minSegment"/> to the previous one, so no edge is shorter
        /// than Revit accepts. Returns null when too little is left to make a closed shape.
        /// </summary>
        public static Vec2[] Simplify(IReadOnlyList<Vec2> loop, double minSegment, double flatness)
        {
            if (loop == null || loop.Count < 3) return null;
            var pts = new List<Vec2>(loop);
            if (Vec2.Dist(pts[0], pts[pts.Count - 1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) return null;

            if (flatness > 0) pts = DouglasPeuckerClosed(pts, flatness);

            var kept = new List<Vec2> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
                if (Vec2.Dist(pts[i], kept[kept.Count - 1]) >= minSegment) kept.Add(pts[i]);
            while (kept.Count > 2 && Vec2.Dist(kept[kept.Count - 1], kept[0]) < minSegment) kept.RemoveAt(kept.Count - 1);
            if (kept.Count < 3) return null;
            if (Math.Abs(SignedArea(kept)) < minSegment * minSegment * 0.5) return null;
            return kept.ToArray();
        }

        static List<Vec2> DouglasPeuckerClosed(List<Vec2> pts, double eps)
        {
            // Split the closed loop at the point furthest from the first, simplify both halves.
            int far = 0; double best = -1;
            for (int i = 1; i < pts.Count; i++)
            {
                double d = Vec2.Dist(pts[0], pts[i]);
                if (d > best) { best = d; far = i; }
            }
            if (far == 0) return pts;
            var a = pts.GetRange(0, far + 1);
            var b = pts.GetRange(far, pts.Count - far); b.Add(pts[0]);
            var ra = DouglasPeucker(a, eps);
            var rb = DouglasPeucker(b, eps);
            var result = new List<Vec2>(ra);
            for (int i = 1; i < rb.Count - 1; i++) result.Add(rb[i]);
            return result;
        }

        static List<Vec2> DouglasPeucker(List<Vec2> pts, double eps)
        {
            if (pts.Count < 3) return new List<Vec2>(pts);
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (s, e) = stack.Pop();
                double dmax = 0; int idx = -1;
                for (int i = s + 1; i < e; i++)
                {
                    double d = SegmentDistance(pts[i], pts[s], pts[e]);
                    if (d > dmax) { dmax = d; idx = i; }
                }
                if (idx >= 0 && dmax > eps)
                {
                    keep[idx] = true;
                    stack.Push((s, idx));
                    stack.Push((idx, e));
                }
            }
            var r = new List<Vec2>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) r.Add(pts[i]);
            return r;
        }

        public static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a; double l2 = ab.X * ab.X + ab.Y * ab.Y;
            if (l2 <= 0) return Vec2.Dist(p, a);
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / l2));
            return Vec2.Dist(p, a + ab * t);
        }

        /// <summary>Even-odd point-in-polygon test.</summary>
        public static bool Contains(IReadOnlyList<Vec2> loop, Vec2 p)
        {
            bool inside = false;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                if ((loop[i].Y > p.Y) != (loop[j].Y > p.Y) &&
                    p.X < (loop[j].X - loop[i].X) * (p.Y - loop[i].Y) / (loop[j].Y - loop[i].Y) + loop[i].X)
                    inside = !inside;
            }
            return inside;
        }
    }
}

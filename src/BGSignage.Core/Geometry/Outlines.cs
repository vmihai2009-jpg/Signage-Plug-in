using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace BGSignage.Core.Geometry
{
    /// <summary>
    /// Turns a graphic's filled shapes and drawn lines into clean solid outlines: everything that touches or
    /// overlaps is merged into one shape, so a pictogram drawn with lines becomes one filled figure (one solid in
    /// Revit, with no edges where the lines cross) instead of a pile of overlapping bars.
    /// </summary>
    public static class Outlines
    {
        /// <summary>Decimal places kept, in millimetres (a thousandth of a millimetre).</summary>
        const int Precision = 3;

        /// <param name="fills">Filled shapes: each island is an outer loop followed by the holes inside it.</param>
        /// <param name="lines">Open polylines drawn with <paramref name="weight"/>.</param>
        /// <param name="weight">Line weight in millimetres (0 = the lines are not drawn).</param>
        /// <param name="squareEnds">Line ends stick out half a line weight (so separate strokes meeting at a corner close up);
        /// otherwise they stop at the end point.</param>
        /// <returns>The merged islands, each an outer loop followed by its holes.</returns>
        public static List<List<Vec2[]>> Merge(IEnumerable<IReadOnlyList<Vec2[]>> fills, IEnumerable<Vec2[]> lines, double weight, bool squareEnds)
        {
            var subject = new PathsD();
            foreach (var island in fills)
            {
                for (int i = 0; i < island.Count; i++)
                {
                    if (island[i] == null || island[i].Length < 3) continue;
                    var path = ToPath(island[i]);
                    // Outer loops one way round, holes the other, so a non-zero union keeps the holes open.
                    bool wantPositive = i == 0;
                    if (Clipper.IsPositive(path) != wantPositive) path = Clipper.ReversePath(path);
                    subject.Add(path);
                }
            }

            if (weight > 0)
            {
                var open = new PathsD(lines.Where(l => l != null && l.Length >= 2).Select(ToPath));
                if (open.Count > 0)
                {
                    // Mitred corners where a line turns (cut off at sharp angles), like a pen stroke.
                    var stroked = Clipper.InflatePaths(open, weight / 2, JoinType.Miter,
                        squareEnds ? EndType.Square : EndType.Butt, 2.0, Precision);
                    // The strokes come back as outer loops and holes (where a line closes on itself, as round a
                    // bin or a cup). Keep that: only turn the whole set round if its outers run the other way.
                    bool flip = stroked.Sum(Clipper.Area) < 0;
                    foreach (var p in stroked) subject.Add(flip ? Clipper.ReversePath(p) : p);
                }
            }

            var result = new List<List<Vec2[]>>();
            if (subject.Count == 0) return result;
            var tree = new PolyTreeD();
            Clipper.BooleanOp(ClipType.Union, subject, null, tree, FillRule.NonZero, Precision);
            for (int i = 0; i < tree.Count; i++) Collect(tree[i], result);
            return result;
        }

        /// <summary>An outer loop with the holes directly inside it; shapes inside those holes become islands of their own.</summary>
        static void Collect(PolyPathD outer, List<List<Vec2[]>> result)
        {
            var loop = ToLoop(outer.Polygon);
            if (loop == null) return;
            var island = new List<Vec2[]> { loop };
            for (int i = 0; i < outer.Count; i++)
            {
                var hole = outer[i];
                var h = ToLoop(hole.Polygon);
                if (h != null) island.Add(h);
                for (int j = 0; j < hole.Count; j++) Collect(hole[j], result);
            }
            result.Add(island);
        }

        static PathD ToPath(Vec2[] pts)
        {
            var p = new PathD(pts.Length);
            foreach (var v in pts) p.Add(new PointD(v.X, v.Y));
            return p;
        }

        static Vec2[] ToLoop(PathD p) =>
            p == null || p.Count < 3 ? null : p.Select(q => new Vec2(q.x, q.y)).ToArray();
    }
}

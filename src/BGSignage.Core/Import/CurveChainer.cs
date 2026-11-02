using System.Collections.Generic;
using System.Linq;
using BGSignage.Core.Geometry;

namespace BGSignage.Core.Import
{
    /// <summary>
    /// Joins loose lines and arcs from a drawing end-to-end, so an outline drawn as separate pieces becomes one
    /// closed shape (a solid in the sign). What cannot be closed stays an open line.
    /// </summary>
    public static class CurveChainer
    {
        public static List<(Vec2[] Points, bool Closed)> Chain(IEnumerable<Vec2[]> pieces, double tolerance = 0.05)
        {
            var open = new List<List<Vec2>>();
            var result = new List<(Vec2[], bool)>();
            foreach (var p in pieces)
            {
                if (p == null || p.Length < 2) continue;
                if (p.Length >= 3 && Vec2.Dist(p[0], p[p.Length - 1]) <= tolerance) result.Add((Trim(p), true));
                else open.Add(p.ToList());
            }

            bool joined = true;
            while (joined)
            {
                joined = false;
                for (int i = 0; i < open.Count && !joined; i++)
                    for (int j = 0; j < open.Count && !joined; j++)
                    {
                        if (i == j) continue;
                        var a = open[i]; var b = open[j];
                        if (Vec2.Dist(a[a.Count - 1], b[0]) <= tolerance) { a.AddRange(b.Skip(1)); }
                        else if (Vec2.Dist(a[a.Count - 1], b[b.Count - 1]) <= tolerance) { b.Reverse(); a.AddRange(b.Skip(1)); }
                        else if (Vec2.Dist(a[0], b[b.Count - 1]) <= tolerance) { b.AddRange(a.Skip(1)); open[i] = b; }
                        else if (Vec2.Dist(a[0], b[0]) <= tolerance) { b.Reverse(); b.AddRange(a.Skip(1)); open[i] = b; }
                        else continue;
                        open.RemoveAt(j);
                        joined = true;
                    }
            }
            foreach (var c in open)
            {
                bool closed = c.Count >= 3 && Vec2.Dist(c[0], c[c.Count - 1]) <= tolerance;
                result.Add((closed ? Trim(c.ToArray()) : c.ToArray(), closed));
            }
            return result;
        }

        static Vec2[] Trim(Vec2[] p) => p.Length > 3 && Vec2.Dist(p[0], p[p.Length - 1]) < 1e-9 ? p.Take(p.Length - 1).ToArray() : p;
    }
}

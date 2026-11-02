using System;
using System.Collections.Generic;
using System.Linq;
using BGSignage.Core.Geometry;

namespace BGSignage.Core.Model
{
    /// <summary>
    /// Line pictograms in the style of architectural wayfinding sets: drawn on a 100 × 100 grid with an even
    /// line weight. They are ordinary graphics, so they scale, rotate, mirror and take a material like a logo.
    /// </summary>
    public static class Symbols
    {
        /// <summary>Line weight on the 100-unit grid (5 mm when the symbol is 100 mm tall).</summary>
        public const double Weight = 5;

        public static IReadOnlyList<string> Names { get; } = new[]
        {
            "Toilets", "Male toilet", "Female toilet", "Accessible toilet", "Baby change",
            "People", "Person",
            "Lift", "Stairs", "Bicycle", "Bins", "Café",
            "Arrow right", "Arrow left", "Arrow up", "Arrow down",
            "Arrow up right", "Arrow up left",
        };

        sealed class Drawing
        {
            public readonly List<GraphicPath> Paths = new List<GraphicPath>();

            public Drawing Line(params double[] xy)
            {
                var pts = new Vec2[xy.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vec2(xy[2 * i], xy[2 * i + 1]);
                Paths.Add(GraphicPath.From(pts, false));
                return this;
            }

            /// <summary>A circle drawn with the line weight (outer and inner outline, so it is a ring).</summary>
            public Drawing Ring(double cx, double cy, double r)
            {
                Paths.Add(GraphicPath.From(Circle(cx, cy, r + Weight / 2), true));
                Paths.Add(GraphicPath.From(Circle(cx, cy, Math.Max(0.5, r - Weight / 2)), true));
                return this;
            }

            /// <summary>A solid round head.</summary>
            public Drawing Dot(double cx, double cy, double r)
            {
                Paths.Add(GraphicPath.From(Circle(cx, cy, r), true));
                return this;
            }

            static Vec2[] Circle(double cx, double cy, double r, int n = 48) =>
                Enumerable.Range(0, n).Select(i => new Vec2(cx + r * Math.Cos(2 * Math.PI * i / n), cy + r * Math.Sin(2 * Math.PI * i / n))).ToArray();

            public Drawing Man(double x, double top = 8, double bottom = 92)
            {
                double h = bottom - top, head = h * 0.075, sh = top + h * 0.24, hand = top + h * 0.58, hip = top + h * 0.55;
                Ring(x, top + head, head);
                Line(x - 14, hand, x - 14, sh, x + 14, sh, x + 14, hand);
                Line(x - 6, sh, x - 6, bottom);
                Line(x + 6, sh, x + 6, bottom);
                Line(x - 6, hip, x + 6, hip);
                return this;
            }

            public Drawing Woman(double x, double top = 8, double bottom = 92)
            {
                double h = bottom - top, head = h * 0.075, sh = top + h * 0.24, hand = top + h * 0.55, hem = top + h * 0.66;
                Ring(x, top + head, head);
                Line(x - 16, hand, x - 7, sh, x + 7, sh, x + 16, hand);
                Line(x - 7, sh, x - 15, hem, x + 15, hem, x + 7, sh);
                Line(x - 5, hem, x - 5, bottom);
                Line(x + 5, hem, x + 5, bottom);
                return this;
            }
        }

        /// <summary>A new graphic element with the symbol, about <paramref name="sizeMm"/> tall.</summary>
        public static GraphicElement Create(string name, double sizeMm = 150)
        {
            var d = Draw(name);
            var g = new GraphicElement
            {
                Name = name, Source = "Symbol", NativeWidth = 100, NativeHeight = 100,
                ScalePercent = Math.Round(sizeMm, 2), StrokeMm = Weight, StrokeScales = true,
            };
            g.Paths.AddRange(d.Paths);
            return g;
        }

        static Drawing Draw(string name)
        {
            var d = new Drawing();
            switch (name)
            {
                case "Toilets":
                    d.Man(30).Woman(70);
                    break;
                case "Male toilet": d.Man(50); break;
                case "Female toilet": d.Woman(50); break;
                case "Accessible toilet":
                    d.Ring(42, 14, 7);
                    d.Line(42, 28, 42, 58, 66, 58, 74, 86, 84, 86);
                    d.Line(42, 40, 62, 40);
                    d.Ring(44, 72, 20);
                    break;
                case "Baby change":
                    d.Man(20, 8, 92);
                    d.Line(38, 52, 92, 52);           // changing table
                    d.Line(44, 52, 44, 92);
                    d.Line(86, 52, 86, 92);
                    d.Ring(80, 40, 6);                 // baby
                    d.Line(48, 44, 72, 44);
                    break;
                case "People": d.Man(32).Woman(68); break;
                case "Person": d.Man(50); break;
                case "Lift":
                    d.Line(10, 22, 90, 22, 90, 95, 10, 95, 10, 22);
                    d.Line(50, 22, 50, 95);
                    d.Line(26, 14, 32, 6, 38, 14);     // up
                    d.Line(62, 6, 68, 14, 74, 6);      // down
                    foreach (var x in new[] { 30.0, 70.0 })
                    {
                        d.Ring(x, 38, 5);
                        d.Line(x - 9, 64, x - 9, 48, x + 9, 48, x + 9, 64);
                        d.Line(x - 4, 48, x - 4, 88);
                        d.Line(x + 4, 48, x + 4, 88);
                    }
                    break;
                case "Stairs":
                    d.Line(8, 90, 26, 90, 26, 72, 44, 72, 44, 54, 62, 54, 62, 36, 80, 36, 80, 18, 94, 18);
                    break;
                case "Bicycle":
                    d.Ring(22, 66, 18);
                    d.Ring(78, 66, 18);
                    d.Line(22, 66, 40, 38, 68, 38, 78, 66);
                    d.Line(40, 38, 50, 66, 68, 38);
                    d.Line(22, 66, 50, 66);
                    d.Line(40, 38, 36, 28);
                    d.Line(30, 28, 44, 28);
                    d.Line(68, 38, 64, 24, 74, 24);
                    break;
                case "Bins":
                    foreach (var x in new[] { 6.0, 54.0 })
                    {
                        d.Line(x, 30, x + 40, 30);                         // lid
                        d.Line(x + 14, 30, x + 14, 22, x + 26, 22, x + 26, 30); // handle
                        d.Line(x + 3, 30, x + 7, 92, x + 33, 92, x + 37, 30);  // body
                        d.Line(x + 14, 44, x + 15, 80);
                        d.Line(x + 26, 44, x + 25, 80);
                    }
                    break;
                case "Café":
                    d.Line(18, 40, 22, 74, 58, 74, 62, 40, 18, 40);
                    d.Line(62, 46, 76, 46, 76, 60, 60, 64);
                    d.Line(10, 86, 72, 86);
                    d.Line(30, 14, 30, 30);
                    d.Line(40, 10, 40, 30);
                    d.Line(50, 14, 50, 30);
                    break;
                default:
                    if (name.StartsWith("Arrow")) Arrow(d, name.Substring(5).Trim());
                    else d.Man(50);
                    break;
            }
            return d;
        }

        static void Arrow(Drawing d, string dir)
        {
            // Pointing right, then turned.
            var shaft = new[] { new Vec2(8, 50), new Vec2(90, 50) };
            var head = new[] { new Vec2(58, 18), new Vec2(90, 50), new Vec2(58, 82) };
            double deg;
            switch (dir)
            {
                case "left": deg = 180; break;
                case "up": deg = -90; break;
                case "down": deg = 90; break;
                case "up right": deg = -45; break;
                case "up left": deg = -135; break;
                default: deg = 0; break;
            }
            double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            Vec2 T(Vec2 p) => new Vec2(50 + (p.X - 50) * c - (p.Y - 50) * s, 50 + (p.X - 50) * s + (p.Y - 50) * c);
            d.Paths.Add(GraphicPath.From(shaft.Select(T).ToArray(), false));
            d.Paths.Add(GraphicPath.From(head.Select(T).ToArray(), false));
        }
    }
}

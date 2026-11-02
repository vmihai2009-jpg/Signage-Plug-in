using System.Collections.Generic;
using BGSignage.Core.Geometry;
using BGSignage.Core.Layout;

namespace BGSignage.Tests
{
    /// <summary>
    /// A predictable test font: every glyph is 0.6 em wide (spaces 0.25 em), drawn as a box 0.7 em tall
    /// (the cap height) with a square hole. "A" followed by "V" kerns by -0.1 em.
    /// </summary>
    internal sealed class BoxFont : IFontFace, IFontProvider
    {
        public string Key => "Box";
        public double Ascent => 0.9;
        public double Descent => 0.2;
        public double CapHeight => 0.7;
        public double XHeight => 0.5;
        public double UnderlinePosition => 0.1;
        public double UnderlineThickness => 0.05;
        public double StrikePosition => 0.3;
        public double StrikeThickness => 0.05;
        public bool HasGlyph(char c) => c != '€';
        public double Advance(char c) => char.IsWhiteSpace(c) ? 0.25 : 0.6;
        public double Kerning(char l, char r) => l == 'A' && r == 'V' ? -0.1 : 0;

        public IReadOnlyList<Vec2[]> Outline(char c, double tol) => new List<Vec2[]>
        {
            Polygons.Rectangle(0.05, -0.7, 0.55, 0),
            Polygons.Rectangle(0.2, -0.5, 0.4, -0.2),
        };

        public IFontFace Resolve(string family, int weight, bool italic, int stretch) => this;
    }
}

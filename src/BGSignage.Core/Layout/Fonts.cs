using System.Collections.Generic;
using BGSignage.Core.Geometry;

namespace BGSignage.Core.Layout
{
    /// <summary>
    /// One font cut (family + weight + style + width). All metrics are in em (1 = the type size).
    /// Outlines are in em too: x to the right, y DOWN, origin on the baseline at the left of the glyph.
    /// </summary>
    public interface IFontFace
    {
        /// <summary>Identifies the cut, so outlines and kerning can be cached and pieces grouped.</summary>
        string Key { get; }
        double Ascent { get; }
        double Descent { get; }
        double CapHeight { get; }
        double XHeight { get; }
        /// <summary>Distance below the baseline to the CENTRE of the underline.</summary>
        double UnderlinePosition { get; }
        double UnderlineThickness { get; }
        /// <summary>Distance above the baseline to the centre of the strikethrough.</summary>
        double StrikePosition { get; }
        double StrikeThickness { get; }
        bool HasGlyph(char c);
        double Advance(char c);
        /// <summary>Pair kerning from the font (usually negative), 0 when the font has none.</summary>
        double Kerning(char left, char right);
        /// <summary>
        /// The glyph's closed outlines with overlaps removed, flattened so no point is further than
        /// <paramref name="toleranceEm"/> from the true curve. Holes (as in O, A, B) are separate loops.
        /// </summary>
        IReadOnlyList<Vec2[]> Outline(char c, double toleranceEm);
    }

    public interface IFontProvider
    {
        /// <summary>The closest available cut. Never null: falls back to a default font.</summary>
        IFontFace Resolve(string family, int weight, bool italic, int stretch);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BGSignage.Core.Geometry;
using BGSignage.Core.Model;

namespace BGSignage.Core.Layout
{
    public enum PieceRole { Panel, Letter, Rule }

    /// <summary>One solid of the sign: a letter, an underline, a rule or the panel. Becomes one extrusion in Revit.</summary>
    public sealed class Piece
    {
        public PieceRole Role { get; set; }
        public MaterialSlot Material { get; set; }
        /// <summary>Closed outlines in sign millimetres. Loops inside other loops are holes.</summary>
        public List<Vec2[]> Loops { get; set; } = new List<Vec2[]>();
        /// <summary>What it is, for messages: the character, "underline", "rule"...</summary>
        public string Label { get; set; }
        /// <summary>The frame or rule it belongs to.</summary>
        public string ElementId { get; set; }

        public Rect2 Bounds => Loops.Aggregate(Rect2.Empty, (r, l) => r.Include(Rect2.Of(l)));
    }

    public sealed class FrameLayout
    {
        public string FrameId { get; set; }
        /// <summary>The frame's outline on the sign (4 corners, rotated with the frame), for selecting and moving it.</summary>
        public Vec2[] Box { get; set; }
        public List<Piece> Pieces { get; } = new List<Piece>();
        public List<string> Warnings { get; } = new List<string>();
        public double ColumnWidth { get; set; }
        public double Height { get; set; }
    }

    /// <summary>
    /// Sets the type of one text frame, the way InDesign would: kerning, tracking, scaling, baseline shift,
    /// skew, character rotation, caps, super/subscript, underline/strikethrough, wrapping, alignment,
    /// indents, paragraph spacing and paragraph rules. The top of the frame is the top of the capitals
    /// of the first line ("Cap Height" first-baseline offset), which makes frames easy to line up.
    /// </summary>
    public static class TextLayout
    {
        sealed class Item
        {
            public char Shown;
            public CharacterFormat F;
            public IFontFace Face;
            public double Size;        // em size in mm, after small caps / super / subscript
            public double Lift;        // mm up from the baseline (baseline shift + super/subscript)
            public double GlyphAdvance; // mm, scaled advance without tracking
            public double Track;       // mm added after the glyph (tracking + word spacing)
            public double Kern;        // mm applied before this glyph
            public bool IsSpace;
            public double X;           // set when the line is placed
            public double Advance => GlyphAdvance + Track;
        }

        sealed class Line
        {
            public List<Item> Items = new List<Item>();
            public int VisibleCount;   // items before trailing spaces
            public double NaturalWidth;
            public double Ascent, Descent, CapHeight, Leading;
            public double Baseline;
            public double StartX;
            public bool FirstOfParagraph, LastOfParagraph;
            public ParagraphFormat Para;
        }

        /// <param name="toleranceMm">How closely curves are followed when outlines are flattened.</param>
        public static FrameLayout Layout(TextFrame frame, IFontProvider fonts, double toleranceMm = 0.2)
        {
            frame.Normalise();
            var result = new FrameLayout { FrameId = frame.Id };
            string text = frame.Text ?? "";
            var formats = frame.ExpandFormats();
            var paraTexts = text.Split('\n');
            var faceCache = new Dictionary<string, IFontFace>();
            IFontFace FaceOf(CharacterFormat f)
            {
                string key = $"{f.FontFamily}|{f.Weight}|{f.Italic}|{f.Stretch}";
                if (!faceCache.TryGetValue(key, out var face)) faceCache[key] = face = fonts.Resolve(f.FontFamily, f.Weight, f.Italic, f.Stretch);
                return face;
            }

            // 1. Characters to glyph items, and lines.
            var lines = new List<Line>();
            int offset = 0;
            var missing = new HashSet<string>();
            for (int p = 0; p < paraTexts.Length; p++)
            {
                var para = frame.Paragraphs[p];
                string pt = paraTexts[p];
                // The format of an empty paragraph comes from its newline (or the previous character).
                var paraFormat = pt.Length > 0 ? formats[offset]
                    : offset < formats.Length ? formats[offset]
                    : formats.Length > 0 ? formats[formats.Length - 1] : frame.DefaultFormat;

                var items = new List<Item>();
                for (int i = 0; i < pt.Length; i++)
                {
                    var item = MakeItem(pt[i], formats[offset + i], FaceOf, para, missing);
                    if (item == null) continue;
                    if (items.Count > 0 && item.F.Kerning == KerningMode.Metrics)
                    {
                        var prev = items[items.Count - 1];
                        if (prev.Face.Key == item.Face.Key && Math.Abs(prev.Size - item.Size) < 1e-9 && prev.F.Kerning == KerningMode.Metrics)
                            item.Kern = item.Face.Kerning(prev.Shown, item.Shown) * item.Size * item.F.HorizontalScale / 100;
                    }
                    items.Add(item);
                }
                offset += pt.Length + 1;

                var paraLines = BreakLines(items, frame.Width, para);
                if (paraLines.Count == 0) paraLines.Add(new Line());
                foreach (var l in paraLines) { l.Para = para; Measure(l, paraFormat, FaceOf(paraFormat)); }
                paraLines[0].FirstOfParagraph = true;
                paraLines[paraLines.Count - 1].LastOfParagraph = true;
                lines.AddRange(paraLines);
            }
            foreach (var m in missing) result.Warnings.Add(m);

            // 2. Column width: the frame's, or the widest line when the frame does not wrap.
            double column = frame.Width > 0 ? frame.Width
                : lines.Count == 0 ? 0 : lines.Max(l => l.NaturalWidth + l.Para.LeftIndentMm + l.Para.RightIndentMm + (l.FirstOfParagraph ? l.Para.FirstLineIndentMm : 0));
            result.ColumnWidth = column;

            // 3. Baselines.
            double y = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (i == 0) y = l.CapHeight;
                else
                {
                    y += l.Leading;
                    if (l.FirstOfParagraph) y += lines[i - 1].Para.SpaceAfterMm + l.Para.SpaceBeforeMm;
                }
                l.Baseline = y;
            }

            // 4. Horizontal placement, glyphs, underlines and rules.
            var pieces = new List<Piece>();
            foreach (var l in lines)
            {
                PlaceLine(l, column);
                EmitGlyphs(l, pieces, frame.Id, toleranceMm);
                EmitDecorations(l, pieces, frame.Id);
                EmitParagraphRules(l, column, pieces, frame.Id);
            }

            double height = lines.Count == 0 ? 0 : lines[lines.Count - 1].Baseline + lines[lines.Count - 1].Descent;

            // 5. Into sign coordinates.
            var toSign = Affine.Rotate(frame.RotationDeg).Then(Affine.Translate(frame.X, frame.Y));
            foreach (var piece in pieces)
            {
                for (int i = 0; i < piece.Loops.Count; i++) piece.Loops[i] = toSign.Apply(piece.Loops[i]);
                result.Pieces.Add(piece);
            }
            double boxW = Math.Max(column, 1), boxH = Math.Max(height, 1);
            result.Height = height;
            result.Box = toSign.Apply(Polygons.Rectangle(0, 0, boxW, boxH));
            return result;
        }

        static Item MakeItem(char c, CharacterFormat f, Func<CharacterFormat, IFontFace> faceOf, ParagraphFormat para, HashSet<string> missing)
        {
            if (c == '\r') return null;
            if (c == '\t') c = ' ';
            var face = faceOf(f);
            double size = f.SizeMm, lift = f.BaselineShiftMm;
            char shown = c;
            if (f.Caps == CapsMode.AllCaps) shown = char.ToUpperInvariant(c);
            else if (f.Caps == CapsMode.SmallCaps && char.IsLower(c)) { shown = char.ToUpperInvariant(c); size *= 0.75; }
            if (f.Position == ScriptPosition.Superscript) { lift += f.SizeMm * 0.333; size *= 0.583; }
            else if (f.Position == ScriptPosition.Subscript) { lift -= f.SizeMm * 0.333; size *= 0.583; }

            bool isSpace = char.IsWhiteSpace(shown);
            if (!isSpace && !face.HasGlyph(shown)) missing.Add($"The font {f.FontFamily} has no \"{shown}\"; it is left out.");

            double hs = f.HorizontalScale / 100;
            return new Item
            {
                Shown = shown, F = f, Face = face, Size = size, Lift = lift, IsSpace = isSpace,
                GlyphAdvance = face.Advance(shown) * size * hs,
                Track = f.Tracking / 1000 * f.SizeMm + (isSpace ? para.WordSpacing / 1000 * size : 0),
            };
        }

        static List<Line> BreakLines(List<Item> items, double frameWidth, ParagraphFormat para)
        {
            var lines = new List<Line>();
            int start = 0;
            bool first = true;
            while (start < items.Count)
            {
                double avail = frameWidth > 0
                    ? frameWidth - para.LeftIndentMm - para.RightIndentMm - (first ? para.FirstLineIndentMm : 0)
                    : double.PositiveInfinity;
                int end = start; // exclusive
                int lastBreak = -1; // index after which we may break (a space)
                double width = 0;
                while (end < items.Count)
                {
                    var it = items[end];
                    double w = width + (end > start ? it.Kern : 0) + it.GlyphAdvance;
                    if (!it.IsSpace && w > avail + 1e-9 && end > start)
                    {
                        if (lastBreak >= start) end = lastBreak + 1;
                        break;
                    }
                    width = w + it.Track;
                    if (it.IsSpace) lastBreak = end;
                    end++;
                }
                // Spaces after the break stay at the end of this line (they never start a line).
                while (end < items.Count && items[end].IsSpace) end++;
                if (end == start) end = start + 1;
                var line = new Line();
                line.Items.AddRange(items.GetRange(start, end - start));
                lines.Add(line);
                start = end;
                first = false;
            }
            return lines;
        }

        static void Measure(Line l, CharacterFormat paraFormat, IFontFace paraFace)
        {
            int visible = l.Items.Count;
            while (visible > 0 && l.Items[visible - 1].IsSpace) visible--;
            l.VisibleCount = visible;
            double w = 0;
            for (int i = 0; i < visible; i++)
            {
                var it = l.Items[i];
                w += (i > 0 ? it.Kern : 0) + it.GlyphAdvance + (i < visible - 1 ? it.Track : 0);
            }
            l.NaturalWidth = w;

            if (l.Items.Count == 0)
            {
                double vs = paraFormat.VerticalScale / 100;
                l.Ascent = paraFace.Ascent * paraFormat.SizeMm * vs;
                l.Descent = paraFace.Descent * paraFormat.SizeMm * vs;
                l.CapHeight = paraFace.CapHeight * paraFormat.SizeMm * vs;
                l.Leading = paraFormat.EffectiveLeading;
                return;
            }
            foreach (var it in l.Items)
            {
                double vs = it.F.VerticalScale / 100;
                l.Ascent = Math.Max(l.Ascent, it.Face.Ascent * it.Size * vs + Math.Max(0, it.Lift));
                l.Descent = Math.Max(l.Descent, it.Face.Descent * it.Size * vs - Math.Min(0, it.Lift));
                l.CapHeight = Math.Max(l.CapHeight, it.Face.CapHeight * it.Size * vs + Math.Max(0, it.Lift));
                l.Leading = Math.Max(l.Leading, it.F.EffectiveLeading);
            }
        }

        static void PlaceLine(Line l, double column)
        {
            var p = l.Para;
            double left = p.LeftIndentMm + (l.FirstOfParagraph ? p.FirstLineIndentMm : 0);
            double avail = column - left - p.RightIndentMm;
            double slack = Math.Max(0, avail - l.NaturalWidth);
            double x = left, extraPerSpace = 0, extraPerGap = 0;

            bool justify = (p.Align == TextAlign.Justify && !l.LastOfParagraph) || p.Align == TextAlign.JustifyAll;
            if (justify && slack > 0 && l.VisibleCount > 1)
            {
                int spaces = 0;
                for (int i = 0; i < l.VisibleCount; i++) if (l.Items[i].IsSpace) spaces++;
                if (spaces > 0) extraPerSpace = slack / spaces;
                else extraPerGap = slack / (l.VisibleCount - 1); // a single word: spread the letters
            }
            else if (p.Align == TextAlign.Center) x += slack / 2;
            else if (p.Align == TextAlign.Right) x += slack;

            l.StartX = x;
            for (int i = 0; i < l.Items.Count; i++)
            {
                var it = l.Items[i];
                if (i > 0) x += it.Kern;
                it.X = x;
                x += it.Advance + (it.IsSpace && i < l.VisibleCount ? extraPerSpace : 0) + (i < l.VisibleCount - 1 ? extraPerGap : 0);
            }
        }

        static void EmitGlyphs(Line l, List<Piece> pieces, string frameId, double toleranceMm)
        {
            foreach (var it in l.Items)
            {
                if (it.IsSpace || !it.Face.HasGlyph(it.Shown)) continue;
                double hs = it.F.HorizontalScale / 100, vs = it.F.VerticalScale / 100;
                var outline = it.Face.Outline(it.Shown, toleranceMm / Math.Max(it.Size, 1e-6));
                if (outline == null || outline.Count == 0) continue;

                var t = Affine.Scale(it.Size * hs, it.Size * vs);
                if (Math.Abs(it.F.SkewDeg) > 1e-9) t = t.Then(Affine.Skew(it.F.SkewDeg));
                if (Math.Abs(it.F.RotationDeg) > 1e-9)
                    t = t.Then(Affine.RotateAbout(it.F.RotationDeg, new Vec2(it.GlyphAdvance / 2, -it.Face.CapHeight * it.Size * vs / 2)));
                t = t.Then(Affine.Translate(it.X, l.Baseline - it.Lift));

                var piece = new Piece { Role = PieceRole.Letter, Material = it.F.Material, Label = it.Shown.ToString(), ElementId = frameId };
                foreach (var loop in outline) piece.Loops.Add(t.Apply(loop));
                pieces.Add(piece);
            }
        }

        static void EmitDecorations(Line l, List<Piece> pieces, string frameId)
        {
            void Runs(Func<Item, bool> on, Action<List<Item>> emit)
            {
                var run = new List<Item>();
                for (int i = 0; i < l.VisibleCount; i++)
                {
                    var it = l.Items[i];
                    if (on(it) && (run.Count == 0 || run[0].F.Material == it.F.Material)) { run.Add(it); continue; }
                    if (run.Count > 0) emit(run);
                    run = new List<Item>();
                    if (on(it)) run.Add(it);
                }
                if (run.Count > 0) emit(run);
            }

            Runs(it => it.F.Underline, run =>
            {
                var a = run[0];
                double weight = a.F.UnderlineWeightMm ?? Math.Max(a.Face.UnderlineThickness * a.Size, 0.5);
                double top = a.F.UnderlineOffsetMm ?? a.Face.UnderlinePosition * a.Size - weight / 2;
                double y0 = l.Baseline - a.F.BaselineShiftMm + top;
                AddBar(run, y0, weight, "underline");
            });
            Runs(it => it.F.Strikethrough, run =>
            {
                var a = run[0];
                double weight = a.F.StrikeWeightMm ?? Math.Max(a.Face.StrikeThickness * a.Size, 0.5);
                double centre = a.F.StrikeOffsetMm ?? a.Face.StrikePosition * a.Size;
                double y0 = l.Baseline - a.F.BaselineShiftMm - centre - weight / 2;
                AddBar(run, y0, weight, "strikethrough");
            });

            void AddBar(List<Item> run, double y0, double weight, string label)
            {
                var last = run[run.Count - 1];
                bool lastOfLine = ReferenceEquals(last, l.Items[l.VisibleCount - 1]);
                double x0 = run[0].X, x1 = last.X + (lastOfLine ? last.GlyphAdvance : last.Advance);
                if (x1 - x0 < 1e-6 || weight <= 0) return;
                pieces.Add(new Piece
                {
                    Role = PieceRole.Letter, Material = run[0].F.Material, Label = label, ElementId = frameId,
                    Loops = { Polygons.Rectangle(x0, y0, x1, y0 + weight) },
                });
            }
        }

        static void EmitParagraphRules(Line l, double column, List<Piece> pieces, string frameId)
        {
            var p = l.Para;
            if (l.FirstOfParagraph && p.RuleAbove.On)
            {
                double bottom = l.Baseline - l.CapHeight - p.RuleAbove.OffsetMm;
                AddRule(p.RuleAbove, bottom - p.RuleAbove.WeightMm);
            }
            if (l.LastOfParagraph && p.RuleBelow.On)
                AddRule(p.RuleBelow, l.Baseline + p.RuleBelow.OffsetMm);

            void AddRule(ParagraphRule r, double top)
            {
                double x0, x1;
                double textStart = l.VisibleCount > 0 ? l.Items[0].X : l.StartX;
                double textEnd = l.VisibleCount > 0 ? l.Items[l.VisibleCount - 1].X + l.Items[l.VisibleCount - 1].GlyphAdvance : l.StartX;
                switch (r.Width)
                {
                    case RuleWidthMode.Text: x0 = textStart; x1 = textEnd; break;
                    case RuleWidthMode.Fixed:
                        if (p.Align == TextAlign.Center) { double c = (textStart + textEnd) / 2; x0 = c - r.LengthMm / 2; x1 = c + r.LengthMm / 2; }
                        else if (p.Align == TextAlign.Right) { x1 = textEnd; x0 = x1 - r.LengthMm; }
                        else { x0 = textStart; x1 = x0 + r.LengthMm; }
                        break;
                    default: x0 = 0; x1 = column; break;
                }
                x0 -= r.ExtendLeftMm;
                x1 += r.ExtendRightMm;
                if (x1 - x0 < 1e-6 || r.WeightMm <= 0) return;
                pieces.Add(new Piece
                {
                    Role = r.Material == MaterialSlot.Rules ? PieceRole.Rule : PieceRole.Letter,
                    Material = r.Material, Label = "paragraph rule", ElementId = frameId,
                    Loops = { Polygons.Rectangle(x0, top, x1, top + r.WeightMm) },
                });
            }
        }
    }
}

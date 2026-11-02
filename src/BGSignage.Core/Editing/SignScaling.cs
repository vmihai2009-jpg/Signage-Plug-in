using System;
using System.Collections.Generic;
using BGSignage.Core.Model;

namespace BGSignage.Core.Editing
{
    /// <summary>Resizes a whole sign: positions, type sizes, spacing, rules, graphics and the panel.</summary>
    public static class SignScaling
    {
        /// <param name="factor">1.5 = 150%.</param>
        /// <param name="depths">Also scale the panel thickness, letter and rule depths and stand-offs.</param>
        public static void Scale(SignDocument doc, double factor, bool depths)
        {
            if (doc == null || !(factor > 0) || Math.Abs(factor - 1) < 1e-9) return;
            double S(double v) => v * factor;
            double? SN(double? v) => v.HasValue ? v * factor : null;

            var p = doc.Panel;
            p.Width = S(p.Width); p.Height = S(p.Height);
            p.PaddingX = S(p.PaddingX); p.PaddingY = S(p.PaddingY);
            p.CornerRadius = S(p.CornerRadius);

            if (depths)
            {
                var b = doc.Build;
                b.PanelThickness = S(b.PanelThickness);
                b.LetterDepth = S(b.LetterDepth); b.LetterStandoff = S(b.LetterStandoff);
                b.RuleDepth = S(b.RuleDepth); b.RuleStandoff = S(b.RuleStandoff);
            }

            void Rule(ParagraphRule r)
            {
                if (r == null) return;
                r.WeightMm = S(r.WeightMm); r.OffsetMm = S(r.OffsetMm); r.LengthMm = S(r.LengthMm);
                r.ExtendLeftMm = S(r.ExtendLeftMm); r.ExtendRightMm = S(r.ExtendRightMm);
            }

            foreach (var f in doc.Frames)
            {
                f.X = S(f.X); f.Y = S(f.Y); f.Width = S(f.Width);
                // Formats can be shared between runs: scale each one once.
                var done = new Dictionary<CharacterFormat, CharacterFormat>();
                var formats = f.ExpandFormats();
                for (int i = 0; i < formats.Length; i++)
                {
                    var src = formats[i];
                    if (!done.TryGetValue(src, out var c))
                    {
                        c = src.Clone();
                        c.SizeMm = S(c.SizeMm); c.LeadingMm = S(c.LeadingMm); c.BaselineShiftMm = S(c.BaselineShiftMm);
                        c.UnderlineWeightMm = SN(c.UnderlineWeightMm); c.UnderlineOffsetMm = SN(c.UnderlineOffsetMm);
                        c.StrikeWeightMm = SN(c.StrikeWeightMm); c.StrikeOffsetMm = SN(c.StrikeOffsetMm);
                        done[src] = c;
                    }
                    formats[i] = c;
                }
                if (formats.Length > 0) f.SetFormats(formats);
                else if (f.Runs != null && f.Runs.Count > 0 && f.Runs[0].Format != null)
                {
                    var c = f.Runs[0].Format.Clone();
                    c.SizeMm = S(c.SizeMm); c.LeadingMm = S(c.LeadingMm);
                    f.Runs[0].Format = c;
                }
                for (int i = 0; i < f.Paragraphs.Count; i++)
                {
                    var para = f.Paragraphs[i].Clone();
                    para.LeftIndentMm = S(para.LeftIndentMm); para.RightIndentMm = S(para.RightIndentMm);
                    para.FirstLineIndentMm = S(para.FirstLineIndentMm);
                    para.SpaceBeforeMm = S(para.SpaceBeforeMm); para.SpaceAfterMm = S(para.SpaceAfterMm);
                    Rule(para.RuleAbove); Rule(para.RuleBelow);
                    f.Paragraphs[i] = para;
                }
            }

            foreach (var r in doc.Rules)
            {
                r.X = S(r.X); r.Y = S(r.Y); r.Length = S(r.Length); r.WeightMm = S(r.WeightMm);
            }

            foreach (var g in doc.Graphics)
            {
                g.X = S(g.X); g.Y = S(g.Y); g.ScalePercent = S(g.ScalePercent); g.StrokeMm = S(g.StrokeMm);
            }
        }
    }
}

using System;
using System.Linq;
using BGSignage.Core.Editing;
using BGSignage.Core.Geometry;
using BGSignage.Core.Layout;
using BGSignage.Core.Model;
using Xunit;

namespace BGSignage.Tests
{
    public class LayoutTests
    {
        static readonly BoxFont Font = new BoxFont();
        static CharacterFormat F(double size = 100) => new CharacterFormat { SizeMm = size, Kerning = KerningMode.None };

        static FrameLayout Lay(TextFrame f) => TextLayout.Layout(f, Font);
        static Rect2 LetterBounds(FrameLayout l, int index) => l.Pieces.Where(p => p.Label != "underline" && p.Label != "strikethrough" && p.Label != "paragraph rule").ElementAt(index).Bounds;

        [Fact]
        public void Glyphs_advance_by_font_width_and_top_of_frame_is_cap_height()
        {
            var l = Lay(TextFrame.Create("t", "AB", F(100), new ParagraphFormat(), 0, 0));
            Assert.Equal(2, l.Pieces.Count);
            var a = LetterBounds(l, 0); var b = LetterBounds(l, 1);
            Assert.Equal(5, a.MinX, 6);
            Assert.Equal(65, b.MinX, 6);       // 60 mm advance
            Assert.Equal(0, a.MinY, 6);        // cap top on the frame top
            Assert.Equal(70, a.MaxY, 6);       // baseline at the cap height
        }

        [Fact]
        public void Tracking_and_horizontal_scale_widen_spacing()
        {
            var f = F(100); f.Tracking = 100; f.HorizontalScale = 50;
            var l = Lay(TextFrame.Create("t", "AB", f, new ParagraphFormat(), 0, 0));
            // advance 0.6 em * 100 mm * 50% = 30, plus tracking 100/1000 em = 10
            Assert.Equal(40 + 2.5, LetterBounds(l, 1).MinX, 6);
            Assert.Equal(25, LetterBounds(l, 0).Width, 6);
        }

        [Fact]
        public void Metric_kerning_uses_font_pairs()
        {
            var f = F(100); f.Kerning = KerningMode.Metrics;
            var l = Lay(TextFrame.Create("t", "AV", f, new ParagraphFormat(), 0, 0));
            Assert.Equal(60 - 10 + 5, LetterBounds(l, 1).MinX, 6);
        }

        [Fact]
        public void Centre_and_right_alignment_use_the_frame_width()
        {
            var c = Lay(TextFrame.Create("t", "AB", F(100), new ParagraphFormat { Align = TextAlign.Center }, 0, 0, 220));
            Assert.Equal((220 - 120) / 2.0 + 5, LetterBounds(c, 0).MinX, 6);
            var r = Lay(TextFrame.Create("t", "AB", F(100), new ParagraphFormat { Align = TextAlign.Right }, 0, 0, 220));
            Assert.Equal(220 - 120 + 5, LetterBounds(r, 0).MinX, 6);
        }

        [Fact]
        public void Justify_all_spreads_a_single_word_to_the_full_width()
        {
            var l = Lay(TextFrame.Create("t", "ABC", F(100), new ParagraphFormat { Align = TextAlign.JustifyAll }, 0, 0, 300));
            Assert.Equal(300 - 5, LetterBounds(l, 2).MaxX, 6);
        }

        [Fact]
        public void Long_text_wraps_at_spaces_and_lines_use_leading()
        {
            var f = F(100); f.LeadingMm = 150;
            var l = Lay(TextFrame.Create("t", "AB AB", f, new ParagraphFormat(), 0, 0, 200));
            var second = LetterBounds(l, 2);
            Assert.Equal(5, second.MinX, 6);
            Assert.Equal(150, second.MinY, 6);
        }

        [Fact]
        public void Paragraph_spacing_and_indents()
        {
            var p = new ParagraphFormat { SpaceAfterMm = 30, LeftIndentMm = 20 };
            var l = Lay(TextFrame.Create("t", "A\nB", F(100), p, 0, 0));
            var b = LetterBounds(l, 1);
            Assert.Equal(25, b.MinX, 6);
            Assert.Equal(120 + 30, b.MinY, 6); // auto leading 120% + space after
        }

        [Fact]
        public void Underline_runs_under_the_text_only()
        {
            var f = F(100); f.Underline = true;
            var l = Lay(TextFrame.Create("t", "AB ", f, new ParagraphFormat(), 0, 0));
            var u = l.Pieces.Single(p => p.Label == "underline").Bounds;
            Assert.Equal(0, u.MinX, 6);
            Assert.Equal(120, u.MaxX, 6);
            Assert.Equal(70 + 10 - 2.5, u.MinY, 6);
            Assert.Equal(5, u.Height, 6);
        }

        [Fact]
        public void Paragraph_rule_below_fixed_length_sits_under_the_baseline()
        {
            var p = new ParagraphFormat { RuleBelow = new ParagraphRule { On = true, WeightMm = 4, OffsetMm = 0, Width = RuleWidthMode.Fixed, LengthMm = 500 } };
            var l = Lay(TextFrame.Create("t", "AB", F(100), p, 0, 0));
            var rule = l.Pieces.Single(x => x.Role == PieceRole.Rule).Bounds;
            Assert.Equal(0, rule.MinX, 6); Assert.Equal(500, rule.MaxX, 6);
            Assert.Equal(70, rule.MinY, 6); Assert.Equal(74, rule.MaxY, 6);
        }

        [Fact]
        public void Skew_leans_letters_right_and_rotation_turns_the_frame()
        {
            var f = F(100); f.SkewDeg = 45;
            var a = LetterBounds(Lay(TextFrame.Create("t", "A", f, new ParagraphFormat(), 0, 0)), 0);
            Assert.Equal(5, a.MinX, 6);
            Assert.Equal(55 + 70, a.MaxX, 6);

            var frame = TextFrame.Create("t", "A", F(100), new ParagraphFormat(), 0, 0);
            frame.RotationDeg = 90; // anticlockwise: the line now runs upwards
            var r = LetterBounds(Lay(frame), 0);
            Assert.Equal(-55, r.MinY, 6);
            Assert.Equal(-5, r.MaxY, 6);
            Assert.Equal(0, r.MinX, 6);
            Assert.Equal(70, r.MaxX, 6);
        }

        [Fact]
        public void Superscript_is_smaller_and_raised()
        {
            var f = F(100);
            var frame = TextFrame.Create("t", "A2", f, new ParagraphFormat(), 0, 0);
            TextEditing.ApplyCharacter(frame, 1, 1, x => x.Position = ScriptPosition.Superscript);
            var l = Lay(frame);
            var a = LetterBounds(l, 0); var s = LetterBounds(l, 1);
            Assert.True(s.Height < a.Height * 0.7);
            Assert.True(s.MaxY < a.MaxY - 20);
        }

        [Fact]
        public void Missing_characters_are_reported()
        {
            var l = Lay(TextFrame.Create("t", "A€", F(100), new ParagraphFormat(), 0, 0));
            Assert.Single(l.Warnings);
            Assert.Single(l.Pieces);
        }

        [Fact]
        public void Sign_panel_fits_content_with_padding_and_rules_are_pieces()
        {
            var d = new SignDocument();
            d.Panel.AutoFit = true; d.Panel.PaddingX = 50; d.Panel.PaddingY = 20;
            d.Frames.Add(TextFrame.Create("t", "AB", F(100), new ParagraphFormat(), 100, 100));
            d.Rules.Add(new RuleElement { X = 100, Y = 200, Length = 120, AngleDeg = 0, WeightMm = 4 });
            var g = SignBuilder.Build(d, Font);
            Assert.Equal(100 - 50, g.PanelRect.MinX, 6);
            Assert.Equal(100 - 20, g.PanelRect.MinY, 6);
            Assert.Equal(202 + 20, g.PanelRect.MaxY, 6);
            Assert.Equal(3, g.Pieces.Count);
            Assert.Equal(new Vec2(g.PanelRect.Center.X, g.PanelRect.MaxY), g.Origin(OriginPoint.BottomCenter));
        }

        [Fact]
        public void Vertical_rule_goes_up_for_90_degrees()
        {
            var (a, b) = SignBuilder.RuleEnds(new RuleElement { X = 10, Y = 100, Length = 50, AngleDeg = 90 });
            Assert.Equal(10, b.X, 6); Assert.Equal(50, b.Y, 6);
        }

        [Fact]
        public void Islands_put_holes_with_their_outer_loop()
        {
            var loops = Font.Outline('A', 0).Concat(new[] { Polygons.Rectangle(2, 2, 3, 3) }).ToList();
            var islands = SignBuilder.Islands(loops);
            Assert.Equal(2, islands.Count);
            Assert.Contains(islands, i => i.Count == 2);
        }

        [Fact]
        public void All_presets_lay_out()
        {
            foreach (var name in Presets.Names)
            {
                var g = SignBuilder.Build(Presets.Create(name), Font);
                Assert.NotEmpty(g.Pieces);
                Assert.False(g.PanelRect.IsEmpty);
            }
        }
    }
}

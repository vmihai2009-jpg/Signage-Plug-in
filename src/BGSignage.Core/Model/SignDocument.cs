using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BGSignage.Core.Model
{
    // The sign as the designer edits it. Every length is in millimetres; sign coordinates have x to the right
    // and y DOWN, with (0,0) at the top-left of the backpanel. Saved as JSON (.bgsign) and inside the Revit family.

    public enum KerningMode { Metrics, None }
    public enum CapsMode { Normal, AllCaps, SmallCaps }
    public enum ScriptPosition { Normal, Superscript, Subscript }
    public enum TextAlign { Left, Center, Right, Justify, JustifyAll }
    public enum RuleWidthMode { Text, Column, Fixed }
    public enum PanelShape { Rectangle, Ellipse }
    /// <summary>Which material a piece is made of. Each slot becomes a material parameter in the family.</summary>
    public enum MaterialSlot { Letters, Accent, Rules }
    public enum Hosting { WallBased, FaceBased, Freestanding }
    public enum OriginPoint { Center, BottomCenter, BottomLeft, TopLeft }

    internal static class Defaults
    {
        /// <summary>
        /// The JSON reader does not run constructors, so a file without a newer setting would get 0 or null.
        /// Copy the constructor defaults first; values in the file then overwrite them.
        /// </summary>
        public static void Apply<T>(T target, T defaults)
        {
            foreach (var p in typeof(T).GetProperties())
                if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0) p.SetValue(target, p.GetValue(defaults));
        }
    }

    [DataContract]
    public sealed class SignDocument
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new SignDocument());

        public const int CurrentVersion = 1;

        [DataMember(Order = 0)] public int Version { get; set; } = CurrentVersion;
        [DataMember(Order = 1)] public string Name { get; set; } = "Reception Sign";
        [DataMember(Order = 2)] public BackPanel Panel { get; set; } = new BackPanel();
        [DataMember(Order = 3)] public Construction Build { get; set; } = new Construction();
        [DataMember(Order = 4)] public List<TextFrame> Frames { get; set; } = new List<TextFrame>();
        [DataMember(Order = 5)] public List<RuleElement> Rules { get; set; } = new List<RuleElement>();
        [DataMember(Order = 6)] public List<GraphicElement> Graphics { get; set; } = new List<GraphicElement>();

        /// <summary>Fills anything missing after loading an older or hand-edited file.</summary>
        public SignDocument Normalise()
        {
            if (string.IsNullOrWhiteSpace(Name)) Name = "Sign";
            Panel = Panel ?? new BackPanel();
            Build = Build ?? new Construction();
            Build.Normalise();
            Frames = Frames ?? new List<TextFrame>();
            Rules = Rules ?? new List<RuleElement>();
            foreach (var f in Frames) f.Normalise();
            foreach (var r in Rules) if (string.IsNullOrEmpty(r.Id)) r.Id = NewId();
            Graphics = Graphics ?? new List<GraphicElement>();
            foreach (var g in Graphics)
            {
                if (string.IsNullOrEmpty(g.Id)) g.Id = NewId();
                g.Paths = g.Paths ?? new List<GraphicPath>();
            }
            return this;
        }

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    [DataContract]
    public sealed class BackPanel
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new BackPanel());

        /// <summary>Designed with a backpanel. The family always contains the panel; this is its starting visibility.</summary>
        [DataMember(Order = 0)] public bool Enabled { get; set; } = true;
        [DataMember(Order = 1)] public PanelShape Shape { get; set; } = PanelShape.Rectangle;
        /// <summary>Size the panel around the lettering plus padding, instead of a fixed width and height.</summary>
        [DataMember(Order = 2)] public bool AutoFit { get; set; }
        [DataMember(Order = 3)] public double Width { get; set; } = 1200;
        [DataMember(Order = 4)] public double Height { get; set; } = 400;
        [DataMember(Order = 5)] public double PaddingX { get; set; } = 60;
        [DataMember(Order = 6)] public double PaddingY { get; set; } = 60;
        [DataMember(Order = 7)] public double CornerRadius { get; set; }
    }

    [DataContract]
    public sealed class MaterialRef
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new MaterialRef());

        [DataMember(Order = 0)] public string Name { get; set; }
        /// <summary>Preview colour, also given to the material when the family creates it.</summary>
        [DataMember(Order = 1)] public byte R { get; set; }
        [DataMember(Order = 2)] public byte G { get; set; }
        [DataMember(Order = 3)] public byte B { get; set; }

        public MaterialRef() { }
        public MaterialRef(string name, byte r, byte g, byte b) { Name = name; R = r; G = g; B = b; }
        public MaterialRef Clone() => (MaterialRef)MemberwiseClone();
    }

    /// <summary>How the sign is built: thicknesses, stand-offs and materials. All become family parameters.</summary>
    [DataContract]
    public sealed class Construction
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new Construction());

        [DataMember(Order = 0)] public double PanelThickness { get; set; } = 12;
        [DataMember(Order = 1)] public double LetterDepth { get; set; } = 10;
        /// <summary>Gap between the panel face (or the wall, without a panel) and the back of the letters.</summary>
        [DataMember(Order = 2)] public double LetterStandoff { get; set; } = 0;
        [DataMember(Order = 3)] public double RuleDepth { get; set; } = 6;
        [DataMember(Order = 4)] public double RuleStandoff { get; set; } = 0;
        [DataMember(Order = 5)] public MaterialRef PanelMaterial { get; set; } = new MaterialRef("BG Sign - Panel", 236, 233, 226);
        [DataMember(Order = 6)] public MaterialRef LetterMaterial { get; set; } = new MaterialRef("BG Sign - Lettering", 40, 40, 40);
        [DataMember(Order = 7)] public MaterialRef AccentMaterial { get; set; } = new MaterialRef("BG Sign - Accent", 176, 125, 72);
        [DataMember(Order = 8)] public MaterialRef RuleMaterial { get; set; } = new MaterialRef("BG Sign - Rules", 150, 120, 70);
        [DataMember(Order = 9)] public Hosting Hosting { get; set; } = Hosting.WallBased;
        [DataMember(Order = 10)] public OriginPoint Origin { get; set; } = OriginPoint.Center;

        public void Normalise()
        {
            PanelMaterial = PanelMaterial ?? new MaterialRef("BG Sign - Panel", 236, 233, 226);
            LetterMaterial = LetterMaterial ?? new MaterialRef("BG Sign - Lettering", 40, 40, 40);
            AccentMaterial = AccentMaterial ?? new MaterialRef("BG Sign - Accent", 176, 125, 72);
            RuleMaterial = RuleMaterial ?? new MaterialRef("BG Sign - Rules", 150, 120, 70);
        }

        public MaterialRef For(MaterialSlot slot) =>
            slot == MaterialSlot.Accent ? AccentMaterial : slot == MaterialSlot.Rules ? RuleMaterial : LetterMaterial;
    }

    /// <summary>InDesign's Character panel, for one character or a run of them.</summary>
    [DataContract]
    public sealed class CharacterFormat : IEquatable<CharacterFormat>
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new CharacterFormat());

        [DataMember(Order = 0)] public string FontFamily { get; set; } = "Arial";
        /// <summary>100 (Thin) to 900 (Black); 400 Regular, 700 Bold.</summary>
        [DataMember(Order = 1)] public int Weight { get; set; } = 400;
        [DataMember(Order = 2)] public bool Italic { get; set; }
        /// <summary>Font width: 1 UltraCondensed .. 5 Normal .. 9 UltraExpanded (uses the font's own condensed cuts).</summary>
        [DataMember(Order = 3)] public int Stretch { get; set; } = 5;
        /// <summary>Type size (em) in mm. Cap height is usually about 70% of this.</summary>
        [DataMember(Order = 4)] public double SizeMm { get; set; } = 50;
        /// <summary>Baseline-to-baseline distance in mm; 0 means Auto (120% of the size).</summary>
        [DataMember(Order = 5)] public double LeadingMm { get; set; }
        [DataMember(Order = 6)] public KerningMode Kerning { get; set; } = KerningMode.Metrics;
        /// <summary>Extra space after each character, in 1/1000 em (InDesign units).</summary>
        [DataMember(Order = 7)] public double Tracking { get; set; }
        [DataMember(Order = 8)] public double HorizontalScale { get; set; } = 100;
        [DataMember(Order = 9)] public double VerticalScale { get; set; } = 100;
        /// <summary>Moves characters up (positive) or down from the baseline, mm.</summary>
        [DataMember(Order = 10)] public double BaselineShiftMm { get; set; }
        /// <summary>False italic: slants characters, degrees (positive leans right).</summary>
        [DataMember(Order = 11)] public double SkewDeg { get; set; }
        /// <summary>Rotates each character about its own centre, degrees anticlockwise.</summary>
        [DataMember(Order = 12)] public double RotationDeg { get; set; }
        [DataMember(Order = 13)] public CapsMode Caps { get; set; }
        [DataMember(Order = 14)] public ScriptPosition Position { get; set; }
        [DataMember(Order = 15)] public bool Underline { get; set; }
        [DataMember(Order = 16)] public bool Strikethrough { get; set; }
        /// <summary>Underline thickness in mm; null uses the font's own.</summary>
        [DataMember(Order = 17)] public double? UnderlineWeightMm { get; set; }
        /// <summary>Underline distance below the baseline (to its top edge) in mm; null uses the font's own.</summary>
        [DataMember(Order = 18)] public double? UnderlineOffsetMm { get; set; }
        [DataMember(Order = 19)] public double? StrikeWeightMm { get; set; }
        /// <summary>Strikethrough height above the baseline (to its centre) in mm; null uses the font's own.</summary>
        [DataMember(Order = 20)] public double? StrikeOffsetMm { get; set; }
        [DataMember(Order = 21)] public MaterialSlot Material { get; set; } = MaterialSlot.Letters;

        public CharacterFormat Clone() => (CharacterFormat)MemberwiseClone();

        public double EffectiveLeading => LeadingMm > 0 ? LeadingMm : SizeMm * 1.2;

        public bool Equals(CharacterFormat o) =>
            o != null && FontFamily == o.FontFamily && Weight == o.Weight && Italic == o.Italic && Stretch == o.Stretch &&
            SizeMm == o.SizeMm && LeadingMm == o.LeadingMm && Kerning == o.Kerning && Tracking == o.Tracking &&
            HorizontalScale == o.HorizontalScale && VerticalScale == o.VerticalScale && BaselineShiftMm == o.BaselineShiftMm &&
            SkewDeg == o.SkewDeg && RotationDeg == o.RotationDeg && Caps == o.Caps && Position == o.Position &&
            Underline == o.Underline && Strikethrough == o.Strikethrough && UnderlineWeightMm == o.UnderlineWeightMm &&
            UnderlineOffsetMm == o.UnderlineOffsetMm && StrikeWeightMm == o.StrikeWeightMm && StrikeOffsetMm == o.StrikeOffsetMm &&
            Material == o.Material;

        public override bool Equals(object obj) => Equals(obj as CharacterFormat);
        public override int GetHashCode() => (FontFamily ?? "").GetHashCode() ^ SizeMm.GetHashCode() ^ Weight;
    }

    /// <summary>A rule (line) drawn with a paragraph, like InDesign's Paragraph Rules.</summary>
    [DataContract]
    public sealed class ParagraphRule
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new ParagraphRule());

        [DataMember(Order = 0)] public bool On { get; set; }
        [DataMember(Order = 1)] public double WeightMm { get; set; } = 4;
        /// <summary>
        /// Rule below: from the baseline of the last line down to the top of the rule.
        /// Rule above: from the top of the capitals of the first line up to the bottom of the rule.
        /// </summary>
        [DataMember(Order = 2)] public double OffsetMm { get; set; } = 8;
        [DataMember(Order = 3)] public RuleWidthMode Width { get; set; } = RuleWidthMode.Column;
        /// <summary>Length when <see cref="Width"/> is Fixed.</summary>
        [DataMember(Order = 4)] public double LengthMm { get; set; } = 600;
        /// <summary>Extends the rule past the left end (positive) or shortens it (negative).</summary>
        [DataMember(Order = 5)] public double ExtendLeftMm { get; set; }
        [DataMember(Order = 6)] public double ExtendRightMm { get; set; }
        [DataMember(Order = 7)] public MaterialSlot Material { get; set; } = MaterialSlot.Rules;

        public ParagraphRule Clone() => (ParagraphRule)MemberwiseClone();
    }

    /// <summary>InDesign's Paragraph panel.</summary>
    [DataContract]
    public sealed class ParagraphFormat
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new ParagraphFormat());

        [DataMember(Order = 0)] public TextAlign Align { get; set; } = TextAlign.Left;
        [DataMember(Order = 1)] public double LeftIndentMm { get; set; }
        [DataMember(Order = 2)] public double RightIndentMm { get; set; }
        [DataMember(Order = 3)] public double FirstLineIndentMm { get; set; }
        [DataMember(Order = 4)] public double SpaceBeforeMm { get; set; }
        [DataMember(Order = 5)] public double SpaceAfterMm { get; set; }
        [DataMember(Order = 6)] public ParagraphRule RuleAbove { get; set; } = new ParagraphRule();
        [DataMember(Order = 7)] public ParagraphRule RuleBelow { get; set; } = new ParagraphRule();
        /// <summary>Extra space added to every space character, in 1/1000 em.</summary>
        [DataMember(Order = 8)] public double WordSpacing { get; set; }

        public ParagraphFormat Clone()
        {
            var c = (ParagraphFormat)MemberwiseClone();
            c.RuleAbove = (RuleAbove ?? new ParagraphRule()).Clone();
            c.RuleBelow = (RuleBelow ?? new ParagraphRule()).Clone();
            return c;
        }
    }

    [DataContract]
    public sealed class TextRun
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new TextRun());

        [DataMember(Order = 0)] public int Length { get; set; }
        [DataMember(Order = 1)] public CharacterFormat Format { get; set; }
        public TextRun() { }
        public TextRun(int length, CharacterFormat format) { Length = length; Format = format; }
    }

    /// <summary>
    /// A text frame placed on the sign. Its text holds paragraphs separated by '\n'. Character formatting is
    /// stored as runs; paragraph formatting has one entry per paragraph.
    /// </summary>
    [DataContract]
    public sealed class TextFrame
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new TextFrame());

        [DataMember(Order = 0)] public string Id { get; set; } = SignDocument.NewId();
        [DataMember(Order = 1)] public string Name { get; set; } = "Text";
        /// <summary>Top-left corner of the frame on the sign, mm.</summary>
        [DataMember(Order = 2)] public double X { get; set; }
        [DataMember(Order = 3)] public double Y { get; set; }
        /// <summary>Frame width for wrapping and alignment; 0 = no wrapping, as wide as the longest line.</summary>
        [DataMember(Order = 4)] public double Width { get; set; }
        /// <summary>Rotates the whole frame about its top-left corner, degrees anticlockwise.</summary>
        [DataMember(Order = 5)] public double RotationDeg { get; set; }
        [DataMember(Order = 6)] public string Text { get; set; } = "";
        [DataMember(Order = 7)] public List<TextRun> Runs { get; set; } = new List<TextRun>();
        [DataMember(Order = 8)] public List<ParagraphFormat> Paragraphs { get; set; } = new List<ParagraphFormat>();

        public int ParagraphCount => (Text ?? "").Count(c => c == '\n') + 1;

        /// <summary>One format per character (newlines included).</summary>
        public CharacterFormat[] ExpandFormats(CharacterFormat fallback = null)
        {
            var text = Text ?? "";
            var result = new CharacterFormat[text.Length];
            int i = 0;
            CharacterFormat last = fallback ?? Runs?.FirstOrDefault()?.Format ?? new CharacterFormat();
            if (Runs != null)
                foreach (var run in Runs)
                {
                    if (run?.Format == null) continue;
                    last = run.Format;
                    for (int k = 0; k < run.Length && i < result.Length; k++) result[i++] = run.Format;
                }
            while (i < result.Length) result[i++] = last;
            return result;
        }

        /// <summary>Stores one format per character as runs, merging equal neighbours.</summary>
        public void SetFormats(IReadOnlyList<CharacterFormat> formats)
        {
            var runs = new List<TextRun>();
            foreach (var f in formats)
            {
                if (runs.Count > 0 && runs[runs.Count - 1].Format.Equals(f)) runs[runs.Count - 1].Length++;
                else runs.Add(new TextRun(1, f));
            }
            if (runs.Count == 0)
            {
                // Keep the formatting of an empty frame so new typing picks it up.
                var keep = Runs?.FirstOrDefault()?.Format ?? new CharacterFormat();
                runs.Add(new TextRun(0, keep));
            }
            Runs = runs;
        }

        /// <summary>The format typing starts with in an empty frame.</summary>
        public CharacterFormat DefaultFormat => Runs?.FirstOrDefault(r => r?.Format != null)?.Format ?? new CharacterFormat();

        public void Normalise()
        {
            if (string.IsNullOrEmpty(Id)) Id = SignDocument.NewId();
            Text = (Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            Runs = Runs ?? new List<TextRun>();
            SetFormats(ExpandFormats());
            Paragraphs = Paragraphs ?? new List<ParagraphFormat>();
            int need = ParagraphCount;
            while (Paragraphs.Count < need)
                Paragraphs.Add(Paragraphs.Count > 0 ? Paragraphs[Paragraphs.Count - 1].Clone() : new ParagraphFormat());
            if (Paragraphs.Count > need) Paragraphs.RemoveRange(need, Paragraphs.Count - need);
            for (int i = 0; i < Paragraphs.Count; i++)
            {
                Paragraphs[i] = Paragraphs[i] ?? new ParagraphFormat();
                Paragraphs[i].RuleAbove = Paragraphs[i].RuleAbove ?? new ParagraphRule();
                Paragraphs[i].RuleBelow = Paragraphs[i].RuleBelow ?? new ParagraphRule();
            }
        }

        public static TextFrame Create(string name, string text, CharacterFormat format, ParagraphFormat para, double x, double y, double width = 0)
        {
            var f = new TextFrame { Name = name, Text = text ?? "", X = x, Y = y, Width = width };
            f.Runs = new List<TextRun> { new TextRun(f.Text.Length, format ?? new CharacterFormat()) };
            f.Paragraphs = new List<ParagraphFormat>();
            for (int i = 0; i < f.ParagraphCount; i++) f.Paragraphs.Add((para ?? new ParagraphFormat()).Clone());
            f.Normalise();
            return f;
        }
    }

    /// <summary>A free-standing bar or rail: horizontal, vertical or at any angle.</summary>
    [DataContract]
    public sealed class RuleElement
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new RuleElement());

        [DataMember(Order = 0)] public string Id { get; set; } = SignDocument.NewId();
        [DataMember(Order = 1)] public string Name { get; set; } = "Rule";
        /// <summary>Start point of the rule's centre line, mm.</summary>
        [DataMember(Order = 2)] public double X { get; set; }
        [DataMember(Order = 3)] public double Y { get; set; }
        [DataMember(Order = 4)] public double Length { get; set; } = 600;
        /// <summary>Degrees anticlockwise from horizontal (90 = straight up).</summary>
        [DataMember(Order = 5)] public double AngleDeg { get; set; }
        [DataMember(Order = 6)] public double WeightMm { get; set; } = 4;
        [DataMember(Order = 7)] public MaterialSlot Material { get; set; } = MaterialSlot.Rules;

        public RuleElement Clone() => (RuleElement)MemberwiseClone();
    }

    /// <summary>One path of an imported graphic, in millimetres (x right, y down).</summary>
    [DataContract]
    public sealed class GraphicPath
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new GraphicPath());

        [DataMember(Order = 0)] public bool Closed { get; set; }
        /// <summary>x0, y0, x1, y1, ...</summary>
        [DataMember(Order = 1)] public double[] Points { get; set; } = new double[0];

        public Geometry.Vec2[] ToVec2()
        {
            var pts = Points ?? new double[0];
            var r = new Geometry.Vec2[pts.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new Geometry.Vec2(pts[2 * i], pts[2 * i + 1]);
            return r;
        }

        public static GraphicPath From(IReadOnlyList<Geometry.Vec2> pts, bool closed)
        {
            var a = new double[pts.Count * 2];
            for (int i = 0; i < pts.Count; i++) { a[2 * i] = Math.Round(pts[i].X, 3); a[2 * i + 1] = Math.Round(pts[i].Y, 3); }
            return new GraphicPath { Closed = closed, Points = a };
        }
    }

    /// <summary>
    /// A logo or graphic imported from .dwg, .dxf, .ai or .pdf. Closed shapes become solids like letters;
    /// open lines become bars of <see cref="StrokeMm"/> width (or are left out when it is 0).
    /// </summary>
    [DataContract]
    public sealed class GraphicElement
    {
        [OnDeserializing] void SetDefaults(StreamingContext _) => Defaults.Apply(this, new GraphicElement());

        [DataMember(Order = 0)] public string Id { get; set; } = SignDocument.NewId();
        [DataMember(Order = 1)] public string Name { get; set; } = "Graphic";
        [DataMember(Order = 2)] public string Source { get; set; }
        /// <summary>Top-left of the graphic on the sign, mm.</summary>
        [DataMember(Order = 3)] public double X { get; set; }
        [DataMember(Order = 4)] public double Y { get; set; }
        /// <summary>Size as a percentage of the drawing's own size.</summary>
        [DataMember(Order = 5)] public double ScalePercent { get; set; } = 100;
        [DataMember(Order = 6)] public double RotationDeg { get; set; }
        [DataMember(Order = 7)] public bool Mirror { get; set; }
        [DataMember(Order = 8)] public MaterialSlot Material { get; set; } = MaterialSlot.Letters;
        [DataMember(Order = 9)] public double StrokeMm { get; set; } = 3;
        /// <summary>Paths with their top-left at (0,0), at the drawing's own size.</summary>
        [DataMember(Order = 10)] public List<GraphicPath> Paths { get; set; } = new List<GraphicPath>();
        [DataMember(Order = 11)] public double NativeWidth { get; set; }
        [DataMember(Order = 12)] public double NativeHeight { get; set; }

        /// <summary>Builds a graphic from paths in any position, moving their top-left corner to (0,0).</summary>
        public static GraphicElement FromPaths(string name, string source, IEnumerable<(Geometry.Vec2[] Points, bool Closed)> paths)
        {
            var list = paths.Where(p => p.Points != null && p.Points.Length >= 2).ToList();
            var bounds = Geometry.Rect2.Empty;
            foreach (var p in list) bounds = bounds.Include(Geometry.Rect2.Of(p.Points));
            var g = new GraphicElement { Name = name, Source = source };
            if (bounds.IsEmpty) return g;
            var shift = new Geometry.Vec2(-bounds.MinX, -bounds.MinY);
            foreach (var p in list) g.Paths.Add(GraphicPath.From(p.Points.Select(v => v + shift).ToArray(), p.Closed));
            g.NativeWidth = bounds.Width; g.NativeHeight = bounds.Height;
            return g;
        }
    }
}

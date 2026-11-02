using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using BGSignage.Core.Geometry;
using BGSignage.Core.Layout;
using BGSignage.Core.Model;
using SixLabors.Fonts;

// Usage: PreviewRender <out folder> [font files...]
class Program
{
    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "out";
        Directory.CreateDirectory(outDir);
        var fonts = new SixFontProvider(args.Skip(1));
        foreach (var name in Presets.Names)
        {
            var doc = Presets.Create(name);
            var g = SignBuilder.Build(doc, fonts, 0.3);
            File.WriteAllText(Path.Combine(outDir, Slug(name) + ".svg"), Svg(doc, g));
            Console.WriteLine($"{name}: {g.Pieces.Count} pieces, panel {g.PanelRect.Width:0}x{g.PanelRect.Height:0}  {string.Join(" ", g.Warnings)}");
        }

        // The symbol library on one sheet.
        var sheet = new SignDocument { Name = "Symbols" };
        sheet.Panel.Enabled = false; sheet.Panel.PaddingX = 30; sheet.Panel.PaddingY = 30;
        for (int i = 0; i < Symbols.Names.Count; i++)
        {
            var sym = Symbols.Create(Symbols.Names[i], 100);
            sym.X = (i % 6) * 150; sym.Y = (i / 6) * 150;
            sheet.Graphics.Add(sym);
        }
        sheet.Normalise();
        File.WriteAllText(Path.Combine(outDir, "symbols.svg"), Svg(sheet, SignBuilder.Build(sheet, fonts, 0.3)));
    }

    static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    static string Hex(MaterialRef m) => $"#{m.R:X2}{m.G:X2}{m.B:X2}";

    static string Svg(SignDocument doc, SignGeometry g)
    {
        var r = g.PanelRect.Include(g.ContentBounds).Inflate(60, 60);
        var sb = new StringBuilder();
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='{F(r.MinX)} {F(r.MinY)} {F(r.Width)} {F(r.Height)}' width='{F(r.Width / 2)}' height='{F(r.Height / 2)}'>");
        sb.Append($"<rect x='{F(r.MinX)}' y='{F(r.MinY)}' width='{F(r.Width)}' height='{F(r.Height)}' fill='#d6d2cb'/>");
        string Path(IEnumerable<Vec2[]> loops) => string.Join(" ", loops.Select(l => "M" + string.Join(" L", l.Select(p => F(p.X) + "," + F(p.Y))) + " Z"));
        if (doc.Panel.Enabled) sb.Append($"<path d='{Path(new[] { g.PanelOutline })}' fill='{Hex(doc.Build.PanelMaterial)}'/>");
        else sb.Append($"<path d='{Path(new[] { g.PanelOutline })}' fill='none' stroke='#0003' stroke-dasharray='8 6'/>");
        foreach (var p in g.Pieces.OrderBy(p => p.Role == PieceRole.Rule ? 0 : 1))
            sb.Append($"<path d='{Path(p.Loops)}' fill='{Hex(doc.Build.For(p.Material))}' fill-rule='evenodd'/>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}

/// <summary>Real fonts through SixLabors.Fonts (TrueType and OpenType/CFF). Any family maps to Inter if missing.</summary>
class SixFontProvider : IFontProvider
{
    readonly FontCollection _collection = new FontCollection();
    readonly Dictionary<string, IFontFace> _cache = new Dictionary<string, IFontFace>();

    public SixFontProvider(IEnumerable<string> files)
    {
        foreach (var f in files) _collection.Add(f);
        _collection.AddSystemFonts();
    }

    public IFontFace Resolve(string family, int weight, bool italic, int stretch)
    {
        string key = $"{family}|{weight}|{italic}";
        if (_cache.TryGetValue(key, out var face)) return face;
        // Inter ships its weights as separate families ("Inter SemiBold") on Linux.
        string w = weight <= 300 ? "Light" : weight <= 450 ? "" : weight <= 550 ? "Medium" : weight <= 650 ? "SemiBold" : "Bold";
        FontFamily fam;
        if (!_collection.TryGet(family, out fam)) fam = _collection.Get("Inter");
        if (w.Length > 0 && _collection.TryGet(fam.Name + " " + w, out var wf)) fam = wf;
        var style = weight >= 650 && fam.GetAvailableStyles().Contains(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        face = new SixFace(fam.CreateFont(1000, style), key);
        _cache[key] = face;
        return face;
    }
}

class SixFace : IFontFace
{
    readonly Font _font;
    readonly Dictionary<char, double> _adv = new Dictionary<char, double>();
    public SixFace(Font font, string key)
    {
        _font = font; Key = key;
        var m = font.FontMetrics;
        double upm = m.UnitsPerEm;
        Ascent = m.HorizontalMetrics.Ascender / upm;
        Descent = -m.HorizontalMetrics.Descender / upm;
        var h = Outline('H', 0.002);
        CapHeight = h.Count > 0 ? -h.SelectMany(l => l).Min(p => p.Y) : 0.7;
        var x = Outline('x', 0.002);
        XHeight = x.Count > 0 ? -x.SelectMany(l => l).Min(p => p.Y) : 0.5;
    }
    public string Key { get; }
    public double Ascent { get; }
    public double Descent { get; }
    public double CapHeight { get; }
    public double XHeight { get; }
    public double UnderlinePosition => 0.1;
    public double UnderlineThickness => 0.06;
    public double StrikePosition => XHeight / 2;
    public double StrikeThickness => 0.06;
    public bool HasGlyph(char c) => char.IsWhiteSpace(c) || _font.TryGetGlyphs(new SixLabors.Fonts.Unicode.CodePoint(c), out _);
    double Width(string s) => TextMeasurer.MeasureAdvance(s, new TextOptions(_font)).Width / 1000.0;
    public double Advance(char c) { if (!_adv.TryGetValue(c, out var a)) _adv[c] = a = Width(c.ToString()); return a; }
    public double Kerning(char l, char r) => char.IsWhiteSpace(l) || char.IsWhiteSpace(r) ? 0 : Math.Round(Width($"{l}{r}") - Advance(l) - Advance(r), 4);

    public IReadOnlyList<Vec2[]> Outline(char c, double tol)
    {
        var rend = new Collector();
        TextRenderer.RenderTextTo(rend, c.ToString(), new TextOptions(_font) { Origin = new Vector2(0, 0) });
        // RenderTextTo puts the top of the line box at y=0; move the baseline to 0.
        double asc = _font.FontMetrics.HorizontalMetrics.Ascender * 1000.0 / _font.FontMetrics.UnitsPerEm;
        double lineTop = rend.Loops.Count == 0 ? 0 : 0;
        return rend.Loops.Select(l => l.Select(p => new Vec2(p.X / 1000.0, (p.Y - asc) / 1000.0)).ToArray()).ToList();
    }

    class Collector : IGlyphRenderer
    {
        public List<List<Vector2>> Loops = new List<List<Vector2>>();
        List<Vector2> _cur; Vector2 _last;
        public void BeginFigure() { }
        public void MoveTo(Vector2 p) { _cur = new List<Vector2> { p }; Loops.Add(_cur); _last = p; }
        public void LineTo(Vector2 p) { _cur.Add(p); _last = p; }
        public void QuadraticBezierTo(Vector2 c, Vector2 p) { var a = _last; for (int i = 1; i <= 8; i++) { float t = i / 8f, u = 1 - t; _cur.Add(a * u * u + c * 2 * u * t + p * t * t); } _last = p; }
        public void CubicBezierTo(Vector2 c1, Vector2 c2, Vector2 p) { var a = _last; for (int i = 1; i <= 10; i++) { float t = i / 10f, u = 1 - t; _cur.Add(a * u * u * u + c1 * 3 * u * u * t + c2 * 3 * u * t * t + p * t * t * t); } _last = p; }
        public void EndFigure() { }
        public void EndGlyph() { }
        public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters p) => true;
        public void BeginText(in FontRectangle bounds) { }
        public void EndText() { }
        public TextDecorations EnabledDecorations() => TextDecorations.None;
        public void SetDecoration(TextDecorations d, Vector2 s, Vector2 e, float t) { }
    }
}

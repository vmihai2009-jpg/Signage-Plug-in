using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using BGSignage.Core.Geometry;
using BGSignage.Core.Layout;

namespace BGSignage.Engine
{
    /// <summary>Fonts installed in Windows, read through WPF. Outlines have overlapping strokes merged.</summary>
    internal sealed class WpfFontProvider : IFontProvider
    {
        public static readonly WpfFontProvider Instance = new WpfFontProvider();
        readonly Dictionary<string, WpfFontFace> _faces = new Dictionary<string, WpfFontFace>();

        /// <summary>
        /// Fonts WPF does not list on its own: fonts installed for this Windows user only (where Adobe Fonts and
        /// many company fonts go) and font files loaded in the designer (kept in %AppData%\BGSignage\Fonts).
        /// TrueType (.ttf), OpenType (.otf, including CFF outlines) and collections (.ttc) all work.
        /// </summary>
        static Dictionary<string, FontFamily> _extra;
        static HashSet<string> _installed;

        public static string PrivateFontFolder => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BGSignage", "Fonts");
        static string UserFontFolder => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");

        static void EnsureLoaded()
        {
            if (_installed != null) return;
            _installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _extra = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Fonts.SystemFontFamilies)
            {
                _installed.Add(f.Source);
                foreach (var n in f.FamilyNames.Values) _installed.Add(n);
            }
            foreach (var folder in new[] { UserFontFolder, PrivateFontFolder })
            {
                try
                {
                    if (!System.IO.Directory.Exists(folder)) continue;
                    foreach (var f in Fonts.GetFontFamilies(new Uri(folder.TrimEnd('\\') + "\\")))
                        foreach (var n in f.FamilyNames.Values)
                            if (!_installed.Contains(n) && !_extra.ContainsKey(n)) _extra[n] = f;
                }
                catch { /* an unreadable font folder is skipped */ }
            }
        }

        /// <summary>Copies a font file into the designer's own font folder and makes it available straight away.</summary>
        public static List<string> AddFontFile(string path)
        {
            System.IO.Directory.CreateDirectory(PrivateFontFolder);
            string target = System.IO.Path.Combine(PrivateFontFolder, System.IO.Path.GetFileName(path));
            System.IO.File.Copy(path, target, true);
            var names = Fonts.GetFontFamilies(new Uri(target)).SelectMany(f => f.FamilyNames.Values).Distinct().ToList();
            _installed = null; // reload the lists
            Instance._faces.Clear();
            return names;
        }

        static FontFamily Family(string family)
        {
            EnsureLoaded();
            if (_extra.TryGetValue(family, out var extra)) return extra;
            // A family name Windows does not know falls back silently to another font; check it really exists.
            return _installed.Contains(family) ? new FontFamily(family) : null;
        }

        public IFontFace Resolve(string family, int weight, bool italic, int stretch)
        {
            string key = $"{family}|{weight}|{italic}|{stretch}";
            if (_faces.TryGetValue(key, out var face)) return face;
            face = TryCreate(family, weight, italic, stretch) ?? TryCreate("Arial", weight, italic, 5) ?? TryCreate("Segoe UI", 400, false, 5);
            _faces[key] = face;
            return face;
        }

        static WpfFontFace TryCreate(string family, int weight, bool italic, int stretch)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(family)) return null;
                var ff = Family(family);
                if (ff == null) return null;
                var tf = new Typeface(ff, italic ? FontStyles.Italic : FontStyles.Normal,
                    FontWeight.FromOpenTypeWeight(Math.Max(1, Math.Min(999, weight))),
                    FontStretch.FromOpenTypeStretch(Math.Max(1, Math.Min(9, stretch))));
                return tf.TryGetGlyphTypeface(out var gt) ? new WpfFontFace(tf, gt, $"{family}|{weight}|{italic}|{stretch}") : null;
            }
            catch { return null; }
        }

        /// <summary>Installed font family names, sorted.</summary>
        public static List<string> FamilyNames()
        {
            EnsureLoaded();
            var lang = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
            return Fonts.SystemFontFamilies
                .Select(f => f.FamilyNames.TryGetValue(lang, out var n) ? n : f.Source)
                .Concat(_extra.Keys)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The weights/styles/widths a family offers, as (label, weight, italic, stretch).</summary>
        public static List<(string Label, int Weight, bool Italic, int Stretch)> Styles(string family)
        {
            var list = new List<(string, int, bool, int)>();
            try
            {
                foreach (var t in (Family(family) ?? new FontFamily(family)).FamilyTypefaces)
                {
                    int w = t.Weight.ToOpenTypeWeight();
                    bool it = t.Style != FontStyles.Normal;
                    int s = t.Stretch.ToOpenTypeStretch();
                    string label = (s != 5 ? StretchName(s) + " " : "") + WeightName(w) + (it ? " Italic" : "");
                    list.Add((label.Trim(), w, it, s));
                }
            }
            catch { }
            if (list.Count == 0) list.AddRange(new[] { ("Regular", 400, false, 5), ("Italic", 400, true, 5), ("Bold", 700, false, 5), ("Bold Italic", 700, true, 5) });
            return list.GroupBy(x => x.Item1).Select(g => g.First()).OrderBy(x => x.Item4).ThenBy(x => x.Item2).ThenBy(x => x.Item3).ToList();
        }

        public static string WeightName(int w) =>
            w <= 150 ? "Thin" : w <= 250 ? "ExtraLight" : w <= 350 ? "Light" : w <= 450 ? "Regular" : w <= 550 ? "Medium"
            : w <= 650 ? "SemiBold" : w <= 750 ? "Bold" : w <= 850 ? "ExtraBold" : "Black";

        public static string StretchName(int s) =>
            new[] { "", "UltraCondensed", "ExtraCondensed", "Condensed", "SemiCondensed", "", "SemiExpanded", "Expanded", "ExtraExpanded", "UltraExpanded" }[Math.Max(1, Math.Min(9, s))];
    }

    internal sealed class WpfFontFace : IFontFace
    {
        const double Em = 1000; // outlines are fetched at 1000 units per em for precision
        readonly Typeface _typeface;
        readonly GlyphTypeface _glyphs;
        readonly Dictionary<long, double> _kerning = new Dictionary<long, double>();
        readonly Dictionary<string, IReadOnlyList<Vec2[]>> _outlines = new Dictionary<string, IReadOnlyList<Vec2[]>>();
        readonly Dictionary<char, double> _single = new Dictionary<char, double>();

        public WpfFontFace(Typeface typeface, GlyphTypeface glyphs, string key)
        {
            _typeface = typeface; _glyphs = glyphs; Key = key;
        }

        public string Key { get; }
        public double Ascent => _glyphs.Baseline;
        public double Descent => Math.Max(0, _glyphs.Height - _glyphs.Baseline);
        public double CapHeight => _glyphs.CapsHeight > 0 ? _glyphs.CapsHeight : 0.7;
        public double XHeight => _glyphs.XHeight > 0 ? _glyphs.XHeight : 0.5;
        public double UnderlinePosition => Math.Abs(_glyphs.UnderlinePosition) > 0 ? Math.Abs(_glyphs.UnderlinePosition) : 0.1;
        public double UnderlineThickness => _glyphs.UnderlineThickness > 0 ? _glyphs.UnderlineThickness : 0.05;
        public double StrikePosition => _glyphs.StrikethroughPosition > 0 ? _glyphs.StrikethroughPosition : XHeight / 2;
        public double StrikeThickness => _glyphs.StrikethroughThickness > 0 ? _glyphs.StrikethroughThickness : UnderlineThickness;

        public bool HasGlyph(char c) => _glyphs.CharacterToGlyphMap.ContainsKey(c);

        public double Advance(char c) =>
            _glyphs.CharacterToGlyphMap.TryGetValue(c, out ushort g) ? _glyphs.AdvanceWidths[g]
            : _glyphs.CharacterToGlyphMap.TryGetValue(' ', out g) ? _glyphs.AdvanceWidths[g] : 0.25;

        /// <summary>
        /// The font's pair kerning, measured the way Windows sets the pair: width of "AV" minus the widths of
        /// "A" and "V". WPF applies the font's kerning table when it sets text.
        /// </summary>
        public double Kerning(char left, char right)
        {
            if (!HasGlyph(left) || !HasGlyph(right) || char.IsWhiteSpace(left) || char.IsWhiteSpace(right)) return 0;
            long key = ((long)left << 16) | right;
            if (_kerning.TryGetValue(key, out double k)) return k;
            try { k = (Width(new string(new[] { left, right })) - Width(left) - Width(right)) / Em; }
            catch { k = 0; }
            if (Math.Abs(k) < 1e-4) k = 0;
            _kerning[key] = k;
            return k;
        }

        double Width(char c)
        {
            if (!_single.TryGetValue(c, out double w)) _single[c] = w = Width(c.ToString());
            return w;
        }

        double Width(string s) =>
            new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, Em, Brushes.Black, 1.0)
                .WidthIncludingTrailingWhitespace;

        public IReadOnlyList<Vec2[]> Outline(char c, double toleranceEm)
        {
            if (!_glyphs.CharacterToGlyphMap.TryGetValue(c, out ushort g)) return Array.Empty<Vec2[]>();
            toleranceEm = Math.Max(1e-4, Math.Min(0.02, toleranceEm));
            string key = c + "|" + Math.Round(Math.Log(toleranceEm) * 4); // cache in steps of tolerance
            if (_outlines.TryGetValue(key, out var cached)) return cached;

            var loops = new List<Vec2[]>();
            try
            {
                Geometry outline = _glyphs.GetGlyphOutline(g, Em, Em);
                // Merge overlapping strokes (common in variable fonts) so each solid is one clean outline.
                var merged = outline.GetOutlinedPathGeometry(toleranceEm * Em / 4, ToleranceType.Absolute);
                var flat = merged.GetFlattenedPathGeometry(toleranceEm * Em, ToleranceType.Absolute);
                foreach (var figure in flat.Figures)
                {
                    var pts = new List<Vec2> { new Vec2(figure.StartPoint.X / Em, figure.StartPoint.Y / Em) };
                    foreach (var seg in figure.Segments)
                    {
                        if (seg is LineSegment ls) pts.Add(new Vec2(ls.Point.X / Em, ls.Point.Y / Em));
                        else if (seg is PolyLineSegment pl) foreach (var p in pl.Points) pts.Add(new Vec2(p.X / Em, p.Y / Em));
                    }
                    if (pts.Count >= 3) loops.Add(pts.ToArray());
                }
            }
            catch { /* a broken glyph is left out */ }
            _outlines[key] = loops;
            return loops;
        }
    }
}

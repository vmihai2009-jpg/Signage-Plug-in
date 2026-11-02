using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BGSignage.Core.Geometry;

namespace BGSignage.Core.Import
{
    /// <summary>
    /// Reads the vector artwork of an Illustrator (.ai) or PDF file: filled shapes as closed outlines and
    /// stroked lines as open paths, in millimetres (x right, y down). Illustrator files are read through the
    /// PDF copy Illustrator saves inside them ("Create PDF Compatible File", on by default).
    /// Text must be converted to outlines in Illustrator first (Type > Create Outlines); images are ignored.
    /// </summary>
    public static class PdfVectorReader
    {
        public sealed class VectorPath
        {
            public Vec2[] Points;
            public bool Closed;
            /// <summary>Line width in mm for stroked paths, 0 for filled shapes.</summary>
            public double StrokeMm;
        }

        const double PtToMm = 25.4 / 72;

        sealed class PdfObject
        {
            public string Dict = "";
            public byte[] Data;
        }

        public static List<VectorPath> Read(string path) => Read(File.ReadAllBytes(path));

        public static List<VectorPath> Read(byte[] bytes)
        {
            string text = Latin1(bytes);
            int head = text.IndexOf("%PDF", StringComparison.Ordinal);
            if (head < 0 || head > 2048)
                throw new InvalidDataException("This file has no PDF copy inside it. In Illustrator use File > Save As, and tick \"Create PDF Compatible File\".");

            var objects = ReadObjects(bytes, text);
            var page = FirstPage(objects) ?? throw new InvalidDataException("No page was found in this file.");

            var reader = new ContentReader(objects);
            var contents = Refs(Value(page.Dict, "Contents"));
            var resources = ResolveDict(objects, Value(page.Dict, "Resources"));
            var all = new List<byte>();
            foreach (var r in contents)
                if (objects.TryGetValue(r, out var c) && c.Data != null) { all.AddRange(c.Data); all.Add((byte)'\n'); }
            reader.Run(all.ToArray(), resources, Affine.Identity, 0);
            return reader.Result();
        }

        static string Latin1(byte[] b)
        {
            var chars = new char[b.Length];
            for (int i = 0; i < b.Length; i++) chars[i] = (char)b[i];
            return new string(chars);
        }

        static Dictionary<int, PdfObject> ReadObjects(byte[] bytes, string text)
        {
            var objects = new Dictionary<int, PdfObject>();
            foreach (Match m in Regex.Matches(text, @"(?<![0-9])(\d+)\s+(\d+)\s+obj\b"))
            {
                int num = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int start = m.Index + m.Length;
                int end = text.IndexOf("endobj", start, StringComparison.Ordinal);
                if (end < 0) end = text.Length;
                var obj = new PdfObject();
                int streamAt = IndexOfKeyword(text, "stream", start, end);
                if (streamAt >= 0)
                {
                    obj.Dict = text.Substring(start, streamAt - start);
                    int dataStart = streamAt + 6;
                    if (dataStart < text.Length && text[dataStart] == '\r') dataStart++;
                    if (dataStart < text.Length && text[dataStart] == '\n') dataStart++;
                    int dataEnd = -1;
                    var len = Value(obj.Dict, "Length");
                    if (len != null && Regex.IsMatch(len.Trim(), @"^\d+$"))
                    {
                        int l = int.Parse(len.Trim(), CultureInfo.InvariantCulture);
                        if (dataStart + l <= text.Length && text.IndexOf("endstream", dataStart + l, Math.Min(64, text.Length - dataStart - l), StringComparison.Ordinal) >= 0)
                            dataEnd = dataStart + l;
                    }
                    if (dataEnd < 0)
                    {
                        dataEnd = text.IndexOf("endstream", dataStart, StringComparison.Ordinal);
                        if (dataEnd < 0) dataEnd = end;
                        while (dataEnd > dataStart && (text[dataEnd - 1] == '\n' || text[dataEnd - 1] == '\r')) dataEnd--;
                    }
                    var raw = new byte[Math.Max(0, dataEnd - dataStart)];
                    Array.Copy(bytes, dataStart, raw, 0, raw.Length);
                    obj.Data = Decode(obj.Dict, raw);
                    end = Math.Max(end, dataEnd);
                }
                else obj.Dict = text.Substring(start, end - start);
                objects[num] = obj;
            }

            // Objects packed inside object streams (PDF 1.5+).
            foreach (var os in objects.Values.Where(o => o.Data != null && Regex.IsMatch(o.Dict, @"/Type\s*/ObjStm")).ToList())
            {
                int n = Int(Value(os.Dict, "N")), first = Int(Value(os.Dict, "First"));
                string s = Latin1(os.Data);
                var nums = Regex.Matches(s.Substring(0, Math.Min(first, s.Length)), @"\d+").Cast<Match>().Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
                for (int i = 0; i + 1 < nums.Count && i / 2 < n; i += 2)
                {
                    int objNum = nums[i], off = first + nums[i + 1];
                    int next = i + 3 < nums.Count ? first + nums[i + 3] : s.Length;
                    if (off >= s.Length || next > s.Length || next < off) continue;
                    if (!objects.ContainsKey(objNum)) objects[objNum] = new PdfObject { Dict = s.Substring(off, next - off) };
                }
            }
            return objects;
        }

        static int IndexOfKeyword(string text, string word, int start, int end)
        {
            int i = start;
            while (true)
            {
                i = text.IndexOf(word, i, Math.Max(0, end - i), StringComparison.Ordinal);
                if (i < 0) return -1;
                bool before = i == 0 || !char.IsLetter(text[i - 1]);
                bool after = i + word.Length >= text.Length || !char.IsLetter(text[i + word.Length]);
                if (before && after) return i;
                i += word.Length;
            }
        }

        static byte[] Decode(string dict, byte[] raw)
        {
            var filter = Value(dict, "Filter") ?? "";
            if (filter.Contains("/FlateDecode"))
            {
                try
                {
                    // zlib: 2-byte header before the deflate data.
                    using (var input = new MemoryStream(raw, 2, Math.Max(0, raw.Length - 2)))
                    using (var z = new DeflateStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        try { z.CopyTo(output); } catch (InvalidDataException) { /* keep what was read (trailing checksum) */ }
                        return output.ToArray();
                    }
                }
                catch { return null; }
            }
            return filter.Length == 0 ? raw : null;
        }

        /// <summary>The raw text of a dictionary entry: a number, a reference "12 0 R", a name, an array or a dictionary.</summary>
        static string Value(string dict, string key)
        {
            if (dict == null) return null;
            var m = Regex.Match(dict, @"/" + key + @"(?![A-Za-z0-9])\s*");
            if (!m.Success) return null;
            int i = m.Index + m.Length;
            if (i >= dict.Length) return null;
            if (dict[i] == '[') return Balanced(dict, i, '[', ']');
            if (dict[i] == '<' && i + 1 < dict.Length && dict[i + 1] == '<') return Balanced(dict, i, '<', '>');
            var r = Regex.Match(dict.Substring(i), @"^\d+\s+\d+\s+R");
            if (r.Success) return r.Value;
            var t = Regex.Match(dict.Substring(i), @"^/?[^\s/\[\]<>()]+");
            return t.Success ? t.Value : null;
        }

        static string Balanced(string s, int start, char open, char close)
        {
            int depth = 0;
            for (int i = start; i < s.Length; i++)
            {
                if (s[i] == open) depth++;
                else if (s[i] == close) { depth--; if (depth == 0) return s.Substring(start, i - start + 1); }
            }
            return s.Substring(start);
        }

        static int Int(string s) => int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        static List<int> Refs(string value) =>
            value == null ? new List<int>() : Regex.Matches(value, @"(\d+)\s+\d+\s+R").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();

        static string ResolveDict(Dictionary<int, PdfObject> objects, string value)
        {
            if (value == null) return "";
            var refs = Refs(value);
            if (value.TrimStart().StartsWith("<<", StringComparison.Ordinal) || refs.Count == 0) return value;
            return objects.TryGetValue(refs[0], out var o) ? o.Dict : "";
        }

        static PdfObject FirstPage(Dictionary<int, PdfObject> objects)
        {
            // Follow the page tree from the root when possible, so the first artboard is used.
            var root = objects.Values.FirstOrDefault(o => Regex.IsMatch(o.Dict, @"/Type\s*/Catalog"));
            var pagesRef = root == null ? null : Refs(Value(root.Dict, "Pages")).Cast<int?>().FirstOrDefault();
            for (int guard = 0; pagesRef != null && guard < 32; guard++)
            {
                if (!objects.TryGetValue(pagesRef.Value, out var node)) break;
                if (Regex.IsMatch(node.Dict, @"/Type\s*/Page(?![s])")) return node;
                pagesRef = Refs(Value(node.Dict, "Kids")).Cast<int?>().FirstOrDefault();
            }
            return objects.Values.FirstOrDefault(o => Regex.IsMatch(o.Dict, @"/Type\s*/Page(?![s])"));
        }

        /// <summary>Runs a page's drawing operators and collects the paths it paints.</summary>
        sealed class ContentReader
        {
            readonly Dictionary<int, PdfObject> _objects;
            readonly List<(VectorPath Path, bool White)> _paths = new List<(VectorPath, bool)>();

            public ContentReader(Dictionary<int, PdfObject> objects) { _objects = objects; }

            sealed class State
            {
                public Affine Ctm = Affine.Identity;
                public double LineWidth = 1;
                public bool WhiteFill, WhiteStroke;
                public State Clone() => (State)MemberwiseClone();
            }

            public void Run(byte[] content, string resources, Affine baseCtm, int depth)
            {
                if (depth > 8) return;
                var state = new State { Ctm = baseCtm };
                var stack = new Stack<State>();
                var nums = new List<double>();
                string lastName = null;
                var subpaths = new List<(List<Vec2> Pts, bool Closed)>();
                List<Vec2> current = null;
                Vec2 cur = default, start = default;

                Vec2 T(double x, double y) => state.Ctm.Apply(new Vec2(x, y));
                void NewSub(Vec2 p) { current = new List<Vec2> { p }; subpaths.Add((current, false)); cur = start = p; }
                void LineTo(Vec2 p) { if (current == null) NewSub(cur); current.Add(p); cur = p; }
                void Curve(Vec2 p1, Vec2 p2, Vec2 p3)
                {
                    if (current == null) NewSub(cur);
                    var p0 = cur;
                    double len = Vec2.Dist(p0, p1) + Vec2.Dist(p1, p2) + Vec2.Dist(p2, p3);
                    int n = (int)Math.Max(4, Math.Min(64, Math.Ceiling(len * PtToMm / 0.4)));
                    for (int i = 1; i <= n; i++)
                    {
                        double t = (double)i / n, u = 1 - t;
                        current.Add(p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t));
                    }
                    cur = p3;
                }
                void ClosePath()
                {
                    if (subpaths.Count > 0) subpaths[subpaths.Count - 1] = (subpaths[subpaths.Count - 1].Pts, true);
                    cur = start;
                    current = null;
                }
                void Paint(bool fill, bool stroke)
                {
                    double scale = Math.Sqrt(Math.Abs(state.Ctm.A * state.Ctm.D - state.Ctm.B * state.Ctm.C));
                    foreach (var (pts, closed) in subpaths)
                    {
                        if (pts.Count < 2) continue;
                        var mm = pts.Select(p => new Vec2(p.X * PtToMm, -p.Y * PtToMm)).ToArray();
                        if (fill && pts.Count >= 3)
                            _paths.Add((new VectorPath { Points = mm, Closed = true }, state.WhiteFill));
                        else if (stroke)
                            _paths.Add((new VectorPath { Points = mm, Closed = closed, StrokeMm = Math.Max(0.1, state.LineWidth * scale * PtToMm) }, state.WhiteStroke));
                    }
                    subpaths.Clear();
                    current = null;
                }
                double N(int fromEnd) => nums.Count >= fromEnd ? nums[nums.Count - fromEnd] : 0;

                foreach (var tok in Tokens(content))
                {
                    if (tok.Kind == TokenKind.Number) { nums.Add(tok.Number); continue; }
                    if (tok.Kind == TokenKind.Name) { lastName = tok.Text; continue; }
                    if (tok.Kind != TokenKind.Operator) continue;
                    switch (tok.Text)
                    {
                        case "q": stack.Push(state.Clone()); break;
                        case "Q": if (stack.Count > 0) state = stack.Pop(); break;
                        case "cm":
                            if (nums.Count >= 6)
                                state.Ctm = new Affine(N(6), N(5), N(4), N(3), N(2), N(1)).Then(state.Ctm);
                            break;
                        case "w": state.LineWidth = N(1); break;
                        case "m": NewSub(T(N(2), N(1))); break;
                        case "l": LineTo(T(N(2), N(1))); break;
                        case "c": Curve(T(N(6), N(5)), T(N(4), N(3)), T(N(2), N(1))); break;
                        case "v": Curve(cur, T(N(4), N(3)), T(N(2), N(1))); break;
                        case "y": { var p3 = T(N(2), N(1)); Curve(T(N(4), N(3)), p3, p3); break; }
                        case "h": ClosePath(); break;
                        case "re":
                            {
                                double x = N(4), y = N(3), w = N(2), h = N(1);
                                NewSub(T(x, y)); LineTo(T(x + w, y)); LineTo(T(x + w, y + h)); LineTo(T(x, y + h)); ClosePath();
                                break;
                            }
                        case "f": case "F": case "f*": Paint(true, false); break;
                        case "B": case "B*": Paint(true, true); break;
                        case "b": case "b*": ClosePath(); Paint(true, true); break;
                        case "S": Paint(false, true); break;
                        case "s": ClosePath(); Paint(false, true); break;
                        case "n": subpaths.Clear(); current = null; break;
                        case "g": state.WhiteFill = N(1) >= 0.99; break;
                        case "G": state.WhiteStroke = N(1) >= 0.99; break;
                        case "rg": state.WhiteFill = N(3) >= 0.99 && N(2) >= 0.99 && N(1) >= 0.99; break;
                        case "RG": state.WhiteStroke = N(3) >= 0.99 && N(2) >= 0.99 && N(1) >= 0.99; break;
                        case "k": state.WhiteFill = N(4) + N(3) + N(2) + N(1) <= 0.01; break;
                        case "K": state.WhiteStroke = N(4) + N(3) + N(2) + N(1) <= 0.01; break;
                        case "Do":
                            if (lastName != null) RunForm(lastName, resources, state.Ctm, depth);
                            break;
                    }
                    nums.Clear();
                }
            }

            void RunForm(string name, string resources, Affine ctm, int depth)
            {
                var xobjects = ResolveDict(_objects, Value(resources, "XObject"));
                var r = Refs(Value(xobjects, name.TrimStart('/')));
                if (r.Count == 0 || !_objects.TryGetValue(r[0], out var form) || form.Data == null) return;
                if (!Regex.IsMatch(form.Dict, @"/Subtype\s*/Form")) return;
                var matrix = Value(form.Dict, "Matrix");
                var m = matrix == null ? null : Regex.Matches(matrix, @"-?\d*\.?\d+(?:[eE][-+]?\d+)?").Cast<Match>().Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
                var formCtm = m != null && m.Length == 6 ? new Affine(m[0], m[1], m[2], m[3], m[4], m[5]).Then(ctm) : ctm;
                var res = Value(form.Dict, "Resources");
                Run(form.Data, res == null ? resources : ResolveDict(_objects, res), formCtm, depth + 1);
            }

            public List<VectorPath> Result()
            {
                // White shapes are kept only as cut-outs inside coloured ones (a white background would swallow the logo).
                var coloured = _paths.Where(p => !p.White).Select(p => p.Path).ToList();
                var closedColoured = coloured.Where(p => p.Closed && p.StrokeMm == 0).ToList();
                var result = new List<VectorPath>(coloured);
                foreach (var (path, white) in _paths)
                    if (white && path.Closed && path.StrokeMm == 0 && path.Points.Length > 0 &&
                        closedColoured.Any(c => Polygons.Contains(c.Points, path.Points[0])))
                        result.Add(path);
                return result;
            }
        }

        enum TokenKind { Number, Name, Operator, Other }

        struct Token
        {
            public TokenKind Kind;
            public string Text;
            public double Number;
        }

        static IEnumerable<Token> Tokens(byte[] s)
        {
            int i = 0, n = s.Length;
            bool IsWhite(byte b) => b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32;
            bool IsDelim(byte b) => b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' || b == '{' || b == '}' || b == '/' || b == '%';
            while (i < n)
            {
                byte c = s[i];
                if (IsWhite(c)) { i++; continue; }
                if (c == '%') { while (i < n && s[i] != '\n' && s[i] != '\r') i++; continue; }
                if (c == '(')
                {
                    int depth = 0;
                    for (; i < n; i++)
                    {
                        if (s[i] == '\\') { i++; continue; }
                        if (s[i] == '(') depth++;
                        else if (s[i] == ')' && --depth == 0) { i++; break; }
                    }
                    yield return new Token { Kind = TokenKind.Other };
                    continue;
                }
                if (c == '<')
                {
                    if (i + 1 < n && s[i + 1] == '<') { i += 2; yield return new Token { Kind = TokenKind.Other }; continue; }
                    while (i < n && s[i] != '>') i++;
                    i++;
                    yield return new Token { Kind = TokenKind.Other };
                    continue;
                }
                if (c == '>' || c == '[' || c == ']' || c == '{' || c == '}' || c == ')') { i++; yield return new Token { Kind = TokenKind.Other }; continue; }
                int st = i;
                if (c == '/') i++;
                while (i < n && !IsWhite(s[i]) && !IsDelim(s[i])) i++;
                if (i == st) { i++; continue; }
                var text = Encoding.ASCII.GetString(s, st, i - st);
                if (c == '/') { yield return new Token { Kind = TokenKind.Name, Text = text }; continue; }
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    yield return new Token { Kind = TokenKind.Number, Number = v };
                    continue;
                }
                if (text == "BI")
                {
                    // Inline image: skip to "EI".
                    while (i + 2 < n && !(s[i] == 'E' && s[i + 1] == 'I' && IsWhite(s[i + 2]) && IsWhite(s[i - 1]))) i++;
                    i += 2;
                    continue;
                }
                yield return new Token { Kind = TokenKind.Operator, Text = text };
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BGSignage.Core.Model;

namespace BGSignage.Core.Editing
{
    /// <summary>
    /// Keeps character and paragraph formatting attached to the right characters while the text is typed in a
    /// plain text box, and applies Character / Paragraph panel changes to the selected text.
    /// </summary>
    public static class TextEditing
    {
        /// <summary>
        /// Replaces the frame's text with <paramref name="newText"/>. Unchanged text keeps its formatting; typed
        /// characters take the formatting of the character before them (or after, at the very start); a new
        /// paragraph copies the paragraph it was split from.
        /// </summary>
        public static void SetText(TextFrame frame, string newText)
        {
            newText = (newText ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            string old = frame.Text ?? "";
            if (old == newText) return;
            var formats = frame.ExpandFormats();

            int prefix = 0, max = Math.Min(old.Length, newText.Length);
            while (prefix < max && old[prefix] == newText[prefix]) prefix++;
            int suffix = 0;
            while (suffix < max - prefix && old[old.Length - 1 - suffix] == newText[newText.Length - 1 - suffix]) suffix++;

            int removedLen = old.Length - prefix - suffix;
            int insertedLen = newText.Length - prefix - suffix;

            CharacterFormat inherit = prefix > 0 ? formats[prefix - 1]
                : prefix < formats.Length ? formats[prefix] : frame.DefaultFormat;
            var newFormats = new List<CharacterFormat>(newText.Length);
            for (int i = 0; i < prefix; i++) newFormats.Add(formats[i]);
            for (int i = 0; i < insertedLen; i++) newFormats.Add(inherit);
            for (int i = old.Length - suffix; i < old.Length; i++) newFormats.Add(formats[i]);

            // Paragraphs: those before the edit stay, removed newlines drop theirs, inserted newlines copy.
            var paras = frame.Paragraphs.Count > 0 ? frame.Paragraphs : new List<ParagraphFormat> { new ParagraphFormat() };
            int at = CountNewlines(old, 0, prefix);
            int removedBreaks = CountNewlines(old, prefix, removedLen);
            int insertedBreaks = CountNewlines(newText, prefix, insertedLen);
            var newParas = new List<ParagraphFormat>();
            for (int i = 0; i <= at && i < paras.Count; i++) newParas.Add(paras[i]);
            var source = paras[Math.Min(at, paras.Count - 1)];
            for (int i = 0; i < insertedBreaks; i++) newParas.Add(source.Clone());
            for (int i = at + removedBreaks + 1; i < paras.Count; i++) newParas.Add(paras[i]);

            frame.Text = newText;
            frame.SetFormats(newFormats);
            if (newText.Length == 0) frame.Runs[0].Format = inherit;
            frame.Paragraphs = newParas;
            frame.Normalise();
        }

        static int CountNewlines(string s, int start, int length)
        {
            int n = 0;
            for (int i = start; i < start + length && i < s.Length; i++) if (s[i] == '\n') n++;
            return n;
        }

        /// <summary>Index of the paragraph that holds character position <paramref name="pos"/>.</summary>
        public static int ParagraphAt(string text, int pos) => CountNewlines(text ?? "", 0, Math.Max(0, Math.Min(pos, (text ?? "").Length)));

        /// <summary>First and last paragraph touched by a selection (a caret touches its own paragraph).</summary>
        public static (int First, int Last) ParagraphRange(string text, int start, int length)
        {
            int first = ParagraphAt(text, start);
            int last = length > 0 ? ParagraphAt(text, start + length - 1) : first;
            return (first, Math.Max(first, last));
        }

        /// <summary>
        /// Character positions a Character panel change applies to: the selection, or with nothing selected,
        /// the whole paragraph the caret is in (handy for "make this line bigger").
        /// </summary>
        public static (int Start, int End) CharacterRange(string text, int start, int length)
        {
            text = text ?? "";
            start = Math.Max(0, Math.Min(start, text.Length));
            if (length > 0) return (start, Math.Min(text.Length, start + length));
            int s = start;
            while (s > 0 && text[s - 1] != '\n') s--;
            int e = start;
            while (e < text.Length && text[e] != '\n') e++;
            if (e < text.Length) e++; // the newline carries the paragraph's format for empty lines
            return (s, e);
        }

        public static void ApplyCharacter(TextFrame frame, int start, int length, Action<CharacterFormat> change)
        {
            var formats = frame.ExpandFormats();
            if (formats.Length == 0)
            {
                var f = frame.DefaultFormat.Clone();
                change(f);
                frame.Runs = new List<TextRun> { new TextRun(0, f) };
                return;
            }
            var (s, e) = CharacterRange(frame.Text, start, length);
            if (e <= s) { s = Math.Max(0, Math.Min(s, formats.Length - 1)); e = s + 1; }
            // Equal formats are changed once, so runs stay shared.
            var changed = new Dictionary<CharacterFormat, CharacterFormat>(ReferenceComparer.Instance);
            for (int i = s; i < e && i < formats.Length; i++)
            {
                if (!changed.TryGetValue(formats[i], out var nf))
                {
                    nf = formats[i].Clone();
                    change(nf);
                    changed[formats[i]] = nf;
                }
                formats[i] = nf;
            }
            frame.SetFormats(formats);
        }

        public static void ApplyParagraph(TextFrame frame, int start, int length, Action<ParagraphFormat> change)
        {
            frame.Normalise();
            var (first, last) = ParagraphRange(frame.Text, start, length);
            for (int i = first; i <= last && i < frame.Paragraphs.Count; i++)
            {
                var p = frame.Paragraphs[i].Clone();
                change(p);
                frame.Paragraphs[i] = p;
            }
        }

        /// <summary>The formats in the selection (or caret paragraph), for showing in the panels.</summary>
        public static List<CharacterFormat> FormatsIn(TextFrame frame, int start, int length)
        {
            var formats = frame.ExpandFormats();
            if (formats.Length == 0) return new List<CharacterFormat> { frame.DefaultFormat };
            var (s, e) = CharacterRange(frame.Text, start, length);
            var list = new List<CharacterFormat>();
            for (int i = s; i < e && i < formats.Length; i++) list.Add(formats[i]);
            if (list.Count == 0) list.Add(formats[Math.Max(0, Math.Min(s, formats.Length) - 1)]);
            return list;
        }

        public static List<ParagraphFormat> ParagraphsIn(TextFrame frame, int start, int length)
        {
            frame.Normalise();
            var (first, last) = ParagraphRange(frame.Text, start, length);
            return frame.Paragraphs.Skip(first).Take(last - first + 1).ToList();
        }

        sealed class ReferenceComparer : IEqualityComparer<CharacterFormat>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(CharacterFormat a, CharacterFormat b) => ReferenceEquals(a, b);
            public int GetHashCode(CharacterFormat o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}

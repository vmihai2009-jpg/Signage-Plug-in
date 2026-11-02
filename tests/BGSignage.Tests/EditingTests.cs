using System.Linq;
using BGSignage.Core.Editing;
using BGSignage.Core.Model;
using Xunit;

namespace BGSignage.Tests
{
    public class EditingTests
    {
        static TextFrame Frame(string text) => TextFrame.Create("t", text, new CharacterFormat { SizeMm = 50 }, new ParagraphFormat(), 0, 0);

        [Fact]
        public void Typed_text_takes_the_format_of_the_character_before()
        {
            var f = Frame("AB");
            TextEditing.ApplyCharacter(f, 1, 1, c => c.SizeMm = 99);
            TextEditing.SetText(f, "ABC");
            var formats = f.ExpandFormats();
            Assert.Equal(50, formats[0].SizeMm);
            Assert.Equal(99, formats[1].SizeMm);
            Assert.Equal(99, formats[2].SizeMm);
        }

        [Fact]
        public void Deleting_keeps_the_formats_around_it()
        {
            var f = Frame("ABCD");
            TextEditing.ApplyCharacter(f, 3, 1, c => c.Underline = true);
            TextEditing.SetText(f, "AD");
            var formats = f.ExpandFormats();
            Assert.False(formats[0].Underline);
            Assert.True(formats[1].Underline);
        }

        [Fact]
        public void New_paragraph_copies_its_paragraph_format_and_joining_drops_one()
        {
            var f = Frame("AB\nCD");
            TextEditing.ApplyParagraph(f, 4, 0, p => p.Align = TextAlign.Right);
            TextEditing.SetText(f, "AB\nC\nD");
            Assert.Equal(3, f.Paragraphs.Count);
            Assert.Equal(TextAlign.Left, f.Paragraphs[0].Align);
            Assert.Equal(TextAlign.Right, f.Paragraphs[1].Align);
            Assert.Equal(TextAlign.Right, f.Paragraphs[2].Align);
            Assert.NotSame(f.Paragraphs[1], f.Paragraphs[2]);

            TextEditing.SetText(f, "ABC\nD");
            Assert.Equal(2, f.Paragraphs.Count);
            Assert.Equal(TextAlign.Left, f.Paragraphs[0].Align);
        }

        [Fact]
        public void Caret_without_selection_formats_the_whole_paragraph()
        {
            var f = Frame("AB\nCD");
            TextEditing.ApplyCharacter(f, 4, 0, c => c.Weight = 700);
            var formats = f.ExpandFormats();
            Assert.Equal(400, formats[0].Weight);
            Assert.Equal(700, formats[3].Weight);
            Assert.Equal(700, formats[4].Weight);
            Assert.Equal(2, f.Runs.Count);
        }

        [Fact]
        public void Emptying_the_frame_keeps_its_format_for_new_typing()
        {
            var f = Frame("AB");
            TextEditing.ApplyCharacter(f, 0, 2, c => c.FontFamily = "Gill Sans");
            TextEditing.SetText(f, "");
            Assert.Equal("Gill Sans", f.DefaultFormat.FontFamily);
            TextEditing.SetText(f, "X");
            Assert.Equal("Gill Sans", f.ExpandFormats()[0].FontFamily);
        }

        [Fact]
        public void Signs_round_trip_through_json_with_defaults_for_missing_settings()
        {
            var d = Presets.Create(Presets.Names[2]);
            var back = SignSerializer.FromJson(SignSerializer.ToJson(d));
            Assert.Equal(SignSerializer.ToJson(d), SignSerializer.ToJson(back));

            var old = SignSerializer.FromJson("{\"Name\":\"Old\",\"Frames\":[{\"Text\":\"HI\",\"Runs\":[{\"Length\":2,\"Format\":{\"SizeMm\":30}}]}]}");
            var fmt = old.Frames.Single().ExpandFormats()[0];
            Assert.Equal(30, fmt.SizeMm);
            Assert.Equal(100, fmt.HorizontalScale);
            Assert.Equal("Arial", fmt.FontFamily);
            Assert.Equal(12, old.Build.PanelThickness);
        }
    }
}

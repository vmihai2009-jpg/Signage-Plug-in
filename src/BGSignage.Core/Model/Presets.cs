using System.Collections.Generic;

namespace BGSignage.Core.Model
{
    /// <summary>Starting points modelled on typical reception and wayfinding signs. Everything stays editable.</summary>
    public static class Presets
    {
        public const string DefaultFont = "Bahnschrift"; // ships with Windows 10/11; a DIN-like signage face

        public static IReadOnlyList<string> Names { get; } = new[]
        {
            "Reception sign (lettering on backpanel)",
            "Floor directory (numbers + rules)",
            "Directory (names sitting on rails)",
            "Directory (brass rail grid)",
            "Directory (vertical divider)",
            "Blank sign",
        };

        public static SignDocument Create(string name)
        {
            switch (name)
            {
                case "Floor directory (numbers + rules)": return FloorDirectory();
                case "Directory (names sitting on rails)": return NamesOnRails();
                case "Directory (brass rail grid)": return RailGrid();
                case "Directory (vertical divider)": return VerticalDivider();
                case "Blank sign": return Blank();
                default: return Reception();
            }
        }

        static CharacterFormat Font(double size, int weight = 400, double tracking = 0, double leading = 0, CapsMode caps = CapsMode.Normal) =>
            new CharacterFormat { FontFamily = DefaultFont, SizeMm = size, Weight = weight, Tracking = tracking, LeadingMm = leading, Caps = caps };

        static ParagraphFormat Para(TextAlign align = TextAlign.Left) => new ParagraphFormat { Align = align };

        public static SignDocument Blank()
        {
            var d = new SignDocument { Name = "New Sign" };
            d.Panel.Width = 1000; d.Panel.Height = 400;
            d.Frames.Add(TextFrame.Create("Text", "SIGN TEXT", Font(80, 400, 50), Para(TextAlign.Center), 100, 160, 800));
            return d.Normalise();
        }

        public static SignDocument Reception()
        {
            var d = new SignDocument { Name = "Reception Sign" };
            d.Panel.Width = 1800; d.Panel.Height = 560; d.Panel.CornerRadius = 0;
            d.Build.PanelThickness = 20; d.Build.LetterDepth = 15; d.Build.LetterStandoff = 15;

            var frame = TextFrame.Create("Name", "COMPANY NAME\nRECEPTION", Font(150, 500, 80), Para(TextAlign.Center), 100, 150, 1600);
            // Second line: smaller, widely tracked, with an accent rule above it.
            int start = frame.Text.IndexOf('\n') + 1;
            Editing.TextEditing.ApplyCharacter(frame, start, frame.Text.Length - start, f => { f.SizeMm = 55; f.Tracking = 400; f.Weight = 300; });
            Editing.TextEditing.ApplyParagraph(frame, start, 0, p =>
            {
                p.SpaceBeforeMm = 70;
                p.RuleAbove = new ParagraphRule { On = true, WeightMm = 4, OffsetMm = 32, Width = RuleWidthMode.Fixed, LengthMm = 360, Material = MaterialSlot.Accent };
            });
            d.Frames.Add(frame);
            return d.Normalise();
        }

        public static SignDocument FloorDirectory()
        {
            var d = new SignDocument { Name = "Floor Directory" };
            d.Panel.Enabled = false; d.Panel.PaddingX = 40; d.Panel.PaddingY = 40;
            d.Build.LetterDepth = 10; d.Build.RuleDepth = 3;
            d.Build.RuleMaterial = new MaterialRef("BG Sign - Rules (dark)", 30, 30, 30);
            var floors = new[]
            {
                ("4", "Reception\nUser Experience\nLab"),
                ("3", "Tech Bar\nWellness Centre"),
                ("2", "Cafe\nTeam Games"),
                ("1", "Training Rooms\nMulti Faith Room"),
            };
            const double pitch = 300, width = 640;
            for (int k = 0; k < floors.Length; k++)
            {
                double top = k * pitch;
                d.Rules.Add(new RuleElement { Name = $"Rule {k + 1}", X = 0, Y = top, Length = width, WeightMm = 5 });
                var num = TextFrame.Create($"Floor {floors[k].Item1}", floors[k].Item1, Font(170, 600), Para(), 0, top + 50);
                if (floors[k].Item1 == "2") Editing.TextEditing.ApplyCharacter(num, 0, 1, f => f.Material = MaterialSlot.Accent);
                d.Frames.Add(num);
                d.Frames.Add(TextFrame.Create($"Floor {floors[k].Item1} names", floors[k].Item2, Font(34, 400, 0, 50), Para(), 170, top + 50, width - 170));
            }
            d.Rules.Add(new RuleElement { Name = "Rule 5", X = 0, Y = floors.Length * pitch, Length = width, WeightMm = 5 });
            return d.Normalise();
        }

        public static SignDocument NamesOnRails()
        {
            var d = new SignDocument { Name = "Directory on Rails" };
            d.Panel.Enabled = false; d.Panel.PaddingX = 40; d.Panel.PaddingY = 40;
            d.Build.LetterDepth = 6; d.Build.LetterStandoff = 0; d.Build.RuleDepth = 15;
            d.Build.LetterMaterial = new MaterialRef("BG Sign - Brushed Brass", 196, 170, 120);
            d.Build.RuleMaterial = new MaterialRef("BG Sign - Black Metal", 25, 25, 25);
            d.Build.AccentMaterial = new MaterialRef("BG Sign - Black Metal", 25, 25, 25);

            string text = "3\nBALLROOM\nMEETING ROOMS\n2\nLOUNGE BALCONY\n1\nPOOL\nRESTAURANT\nWHISKEY BAR\nGYM";
            var frame = TextFrame.Create("Directory", text, Font(48, 500, 60, 100), Para(), 0, 0, 700);
            var lines = text.Split('\n');
            int pos = 0;
            foreach (var line in lines)
            {
                bool isFloor = line.Length == 1 && char.IsDigit(line[0]);
                if (isFloor)
                {
                    Editing.TextEditing.ApplyCharacter(frame, pos, line.Length, f => { f.SizeMm = 42; f.Weight = 700; f.Material = MaterialSlot.Accent; });
                    Editing.TextEditing.ApplyParagraph(frame, pos, 0, p => p.SpaceBeforeMm = pos == 0 ? 0 : 70);
                }
                else
                    Editing.TextEditing.ApplyParagraph(frame, pos, 0, p =>
                        p.RuleBelow = new ParagraphRule { On = true, WeightMm = 6, OffsetMm = 0, Width = RuleWidthMode.Fixed, LengthMm = 650 });
                pos += line.Length + 1;
            }
            d.Frames.Add(frame);
            return d.Normalise();
        }

        public static SignDocument RailGrid()
        {
            var d = new SignDocument { Name = "Rail Grid Directory" };
            d.Panel.Enabled = true; d.Panel.AutoFit = true; d.Panel.PaddingX = 80; d.Panel.PaddingY = 80;
            d.Build.PanelThickness = 30; d.Build.LetterDepth = 5; d.Build.LetterStandoff = 12; d.Build.RuleDepth = 12;
            d.Build.PanelMaterial = new MaterialRef("BG Sign - Limestone", 222, 216, 204);
            d.Build.LetterMaterial = new MaterialRef("BG Sign - Dark Bronze", 50, 44, 38);
            d.Build.AccentMaterial = new MaterialRef("BG Sign - Limestone", 240, 237, 230);
            d.Build.RuleMaterial = new MaterialRef("BG Sign - Antique Brass", 160, 128, 80);

            const double pitch = 90;
            string[] names = { "TENANT ONE", "TENANT ONE RECEPTION", "TENANT ONE", "TENANT ONE", "TENANT ONE", "TENANT TWO / TENANT THREE", "TENANT TWO", "TENANT TWO RECEPTION", "TENANT TWO" };
            var numbers = TextFrame.Create("Floor numbers", "9\n8\n7\n6\n5\n4\n3\n2\n1", Font(64, 600, 0, pitch), Para(TextAlign.Center), 0, 0, 70);
            Editing.TextEditing.ApplyCharacter(numbers, 0, numbers.Text.Length, f => f.Material = MaterialSlot.Accent);
            d.Frames.Add(numbers);
            var list = TextFrame.Create("Tenants", string.Join("\n", names), Font(30, 400, 60, pitch), Para(), 220, 34, 700);
            Editing.TextEditing.ApplyParagraph(list, 0, list.Text.Length, p =>
                p.RuleBelow = new ParagraphRule { On = true, WeightMm = 5, OffsetMm = 2, Width = RuleWidthMode.Column, ExtendLeftMm = 90, ExtendRightMm = 0 });
            d.Frames.Add(list);
            double bottom = 34 + 22 + pitch * (names.Length - 1) + 30;
            foreach (var x in new[] { 200.0, 520.0, 900.0 })
                d.Rules.Add(new RuleElement { Name = "Upright", X = x, Y = bottom, Length = bottom + 10, AngleDeg = 90, WeightMm = 8 });
            return d.Normalise();
        }

        public static SignDocument VerticalDivider()
        {
            var d = new SignDocument { Name = "Divider Directory" };
            d.Panel.Enabled = false; d.Panel.PaddingX = 40; d.Panel.PaddingY = 40;
            d.Build.LetterDepth = 12; d.Build.RuleDepth = 4;
            d.Build.LetterMaterial = new MaterialRef("BG Sign - Black Metal", 25, 25, 25);
            d.Build.RuleMaterial = new MaterialRef("BG Sign - Black Metal", 25, 25, 25);
            var floors = new[] { ("6", "SIDE NOTE CLUB"), ("5", "RYSE SPACE\nSTUDIO A-E"), ("4", "LONG CHIM\nFITNESS") };
            const double pitch = 330;
            for (int k = 0; k < floors.Length; k++)
            {
                double top = k * pitch;
                d.Frames.Add(TextFrame.Create($"Floor {floors[k].Item1}", floors[k].Item1, Font(150, 600), Para(TextAlign.Right), 0, top, 120));
                d.Rules.Add(new RuleElement { Name = $"Divider {k + 1}", X = 150, Y = top + 270, Length = 290, AngleDeg = 90, WeightMm = 6 });
                d.Frames.Add(TextFrame.Create($"Floor {floors[k].Item1} names", floors[k].Item2, Font(40, 600, 20, 62, CapsMode.AllCaps), Para(), 180, top + 10, 520));
            }
            return d.Normalise();
        }
    }
}

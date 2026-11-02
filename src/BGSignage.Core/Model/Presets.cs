using System.Collections.Generic;
using System.Linq;

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
            "Directory (slanted rules)",
            "Stand-off letters on wall (no panel)",
            "Floor number (large numeral)",
            "Wayfinding (arrows)",
            "Room plaque (round)",
            "Tenant directory (two columns)",
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
                case "Directory (slanted rules)": return SlantedRules();
                case "Stand-off letters on wall (no panel)": return StandOffLetters();
                case "Floor number (large numeral)": return FloorNumber();
                case "Wayfinding (arrows)": return Wayfinding();
                case "Room plaque (round)": return RoomPlaque();
                case "Tenant directory (two columns)": return TwoColumnDirectory();
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

        /// <summary>Big lettering pinned off the wall with no panel, like a lobby name wall.</summary>
        public static SignDocument StandOffLetters()
        {
            var d = new SignDocument { Name = "Stand-off Lettering" };
            d.Panel.Enabled = false; d.Panel.PaddingX = 50; d.Panel.PaddingY = 50;
            d.Build.LetterDepth = 25; d.Build.LetterStandoff = 30;
            d.Build.LetterMaterial = new MaterialRef("BG Sign - Brushed Brass", 196, 170, 120);
            d.Build.AccentMaterial = new MaterialRef("BG Sign - Black Metal", 25, 25, 25);
            var name = TextFrame.Create("Name", "COMPANY", Font(260, 600, 120), Para(TextAlign.Center), 0, 0, 2000);
            d.Frames.Add(name);
            var tag = TextFrame.Create("Tagline", "ARCHITECTURE  ·  INTERIORS", Font(60, 400, 500), Para(TextAlign.Center), 0, 380, 2000);
            Editing.TextEditing.ApplyCharacter(tag, 0, tag.Text.Length, f => f.Material = MaterialSlot.Accent);
            d.Frames.Add(tag);
            return d.Normalise();
        }

        /// <summary>A large floor numeral with the floor name and a short list under a rule, as at a lift lobby.</summary>
        public static SignDocument FloorNumber()
        {
            var d = new SignDocument { Name = "Floor Number" };
            d.Panel.Enabled = false; d.Panel.PaddingX = 40; d.Panel.PaddingY = 40;
            d.Build.LetterDepth = 12; d.Build.RuleDepth = 4;
            d.Build.LetterMaterial = new MaterialRef("BG Sign - Black Metal", 25, 25, 25);
            d.Build.AccentMaterial = new MaterialRef("BG Sign - Brushed Brass", 196, 170, 120);
            d.Build.RuleMaterial = new MaterialRef("BG Sign - Brushed Brass", 196, 170, 120);
            var num = TextFrame.Create("Numeral", "3", Font(600, 600, -20), Para(), 0, 0);
            d.Frames.Add(num);
            var label = TextFrame.Create("Floor name", "THIRD FLOOR", Font(50, 600, 300), Para(), 0, 520, 700);
            Editing.TextEditing.ApplyCharacter(label, 0, label.Text.Length, f => f.Material = MaterialSlot.Accent);
            d.Frames.Add(label);
            d.Rules.Add(new RuleElement { Name = "Rule", X = 0, Y = 630, Length = 700, WeightMm = 5 });
            d.Frames.Add(TextFrame.Create("Rooms", "Meeting Rooms 3.01 – 3.08\nStudio\nTerrace", Font(36, 400, 20, 56), Para(), 0, 690, 700));
            return d.Normalise();
        }

        /// <summary>Directional sign: arrows and destinations on a panel, one per row with rules between.</summary>
        public static SignDocument Wayfinding()
        {
            var d = new SignDocument { Name = "Wayfinding Sign" };
            d.Panel.Width = 1200; d.Panel.Height = 700; d.Panel.CornerRadius = 10;
            d.Build.PanelThickness = 10; d.Build.LetterDepth = 3; d.Build.RuleDepth = 2;
            d.Build.PanelMaterial = new MaterialRef("BG Sign - Dark Grey", 55, 58, 60);
            d.Build.LetterMaterial = new MaterialRef("BG Sign - White", 245, 245, 242);
            d.Build.AccentMaterial = new MaterialRef("BG Sign - Yellow", 230, 185, 40);
            d.Build.RuleMaterial = new MaterialRef("BG Sign - White", 245, 245, 242);
            var rows = new[] { ("Reception", 'R'), ("Meeting Rooms 1–6", 'L'), ("Lifts  ·  Stairs", 'U'), ("Cafe", 'R') };
            const double top = 90, pitch = 140;
            for (int k = 0; k < rows.Length; k++)
            {
                double y = top + k * pitch;
                var arrow = GraphicElement.FromPaths("Arrow " + (k + 1), "template", new[] { (Arrow(rows[k].Item2, 80), true) });
                arrow.X = 80; arrow.Y = y - 10; arrow.Material = MaterialSlot.Accent;
                d.Graphics.Add(arrow);
                d.Frames.Add(TextFrame.Create(rows[k].Item1, rows[k].Item1, Font(60, 500, 10), Para(), 220, y, 900));
                if (k < rows.Length - 1) d.Rules.Add(new RuleElement { Name = $"Rule {k + 1}", X = 80, Y = y + 100, Length = 1040, WeightMm = 2 });
            }
            return d.Normalise();
        }

        /// <summary>An arrow in an s × s box, pointing Right, Left, Up or Down (sign coordinates, y down).</summary>
        static Geometry.Vec2[] Arrow(char dir, double s)
        {
            // Pointing right: shaft 0.36 s thick, head 0.5 s long.
            double t = 0.18 * s, h = 0.5 * s;
            var right = new[] { (0.0, s / 2 - t), (s - h, s / 2 - t), (s - h, 0.0), (s, s / 2), (s - h, s), (s - h, s / 2 + t), (0.0, s / 2 + t) };
            return right.Select(p =>
            {
                double x = p.Item1, y = p.Item2;
                switch (dir)
                {
                    case 'L': return new Geometry.Vec2(s - x, y);
                    case 'U': return new Geometry.Vec2(y, s - x);
                    case 'D': return new Geometry.Vec2(y, x);
                    default: return new Geometry.Vec2(x, y);
                }
            }).ToArray();
        }

        /// <summary>A round door or room plaque: number over name.</summary>
        public static SignDocument RoomPlaque()
        {
            var d = new SignDocument { Name = "Room Plaque" };
            d.Panel.Shape = PanelShape.Ellipse; d.Panel.Width = 300; d.Panel.Height = 300;
            d.Build.PanelThickness = 8; d.Build.LetterDepth = 2;
            d.Build.PanelMaterial = new MaterialRef("BG Sign - Brushed Brass", 196, 170, 120);
            d.Build.LetterMaterial = new MaterialRef("BG Sign - Black Metal", 25, 25, 25);
            d.Frames.Add(TextFrame.Create("Number", "1.04", Font(70, 600, 20), Para(TextAlign.Center), 30, 95, 240));
            var name = TextFrame.Create("Room", "MEETING\nROOM", Font(22, 500, 250, 32), Para(TextAlign.Center), 30, 190, 240);
            Editing.TextEditing.ApplyParagraph(name, 0, 0, p =>
                p.RuleAbove = new ParagraphRule { On = true, WeightMm = 1.5, OffsetMm = 18, Width = RuleWidthMode.Fixed, LengthMm = 60 });
            d.Frames.Add(name);
            return d.Normalise();
        }

        /// <summary>Tenant directory: names on the left, floors aligned on the right, thin rules between rows.</summary>
        public static SignDocument TwoColumnDirectory()
        {
            var d = new SignDocument { Name = "Tenant Directory" };
            d.Panel.Width = 900; d.Panel.Height = 1000;
            d.Build.PanelThickness = 15; d.Build.LetterDepth = 4; d.Build.RuleDepth = 2;
            d.Build.PanelMaterial = new MaterialRef("BG Sign - Smoked Oak", 120, 92, 66);
            d.Build.LetterMaterial = new MaterialRef("BG Sign - Cream", 236, 230, 214);
            d.Build.RuleMaterial = new MaterialRef("BG Sign - Cream", 236, 230, 214);
            d.Build.AccentMaterial = new MaterialRef("BG Sign - Cream", 236, 230, 214);
            d.Frames.Add(TextFrame.Create("Heading", "DIRECTORY", Font(50, 600, 400), Para(), 80, 90, 740));
            d.Rules.Add(new RuleElement { Name = "Heading rule", X = 80, Y = 180, Length = 740, WeightMm = 4 });
            string[] names = { "Atelier North", "Halden & Co", "Marlow Studio", "Penrose Partners", "Studio Kite", "The Long Room", "Wren Associates" };
            string[] floors = { "6", "5", "5", "4", "3", "2", "1" };
            const double pitch = 100;
            var left = TextFrame.Create("Tenants", string.Join("\n", names), Font(40, 400, 10, pitch), Para(), 80, 240, 600);
            Editing.TextEditing.ApplyParagraph(left, 0, left.Text.Length, p =>
                p.RuleBelow = new ParagraphRule { On = true, WeightMm = 1, OffsetMm = 30, Width = RuleWidthMode.Fixed, LengthMm = 740 });
            d.Frames.Add(left);
            d.Frames.Add(TextFrame.Create("Floors", string.Join("\n", floors), Font(40, 600, 0, pitch), Para(TextAlign.Right), 700, 240, 120));
            return d.Normalise();
        }

        /// <summary>Big floor numbers with the names between sloping rules.</summary>
        public static SignDocument SlantedRules()
        {
            var d = new SignDocument { Name = "Slanted Rule Directory" };
            d.Panel.Enabled = false; d.Panel.PaddingX = 40; d.Panel.PaddingY = 40;
            d.Build.LetterDepth = 10; d.Build.RuleDepth = 4;
            d.Build.LetterMaterial = new MaterialRef("BG Sign - Black Metal", 25, 25, 25);
            d.Build.RuleMaterial = new MaterialRef("BG Sign - Brushed Brass", 196, 170, 120);
            var floors = new[] { ("3", "Studio\nMeeting Rooms"), ("2", "Lounge\nLibrary"), ("1", "Reception\nCafe") };
            const double pitch = 300, width = 720, slope = 6; // degrees, rising to the right
            double rise = width * System.Math.Tan(slope * System.Math.PI / 180);
            for (int k = 0; k <= floors.Length; k++)
                d.Rules.Add(new RuleElement { Name = $"Sloped rule {k + 1}", X = 0, Y = k * pitch + rise, Length = width / System.Math.Cos(slope * System.Math.PI / 180), AngleDeg = slope, WeightMm = 5 });
            for (int k = 0; k < floors.Length; k++)
            {
                double top = k * pitch + rise + 50;
                d.Frames.Add(TextFrame.Create($"Floor {floors[k].Item1}", floors[k].Item1, Font(170, 600), Para(), 0, top));
                d.Frames.Add(TextFrame.Create($"Floor {floors[k].Item1} names", floors[k].Item2, Font(36, 500, 60, 54, CapsMode.AllCaps), Para(), 190, top - 10, 500));
            }
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

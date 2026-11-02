using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BGSignage.Core.Editing;
using BGSignage.Core.Model;

namespace BGSignage.Engine.UI
{
    // The panels of the designer window: sign settings (left), Character, Paragraph and Position (right).
    internal sealed partial class DesignerWindow
    {
        // Sign panel
        TextBox _signName;
        CheckBox _panelOn, _autoFit;
        ComboBox _shape, _hosting, _origin;
        TextBox _familySizes;
        NumberField _panelW, _panelH, _padX, _padY, _radius, _panelT, _letterD, _letterS, _ruleD, _ruleS;
        readonly List<(MaterialSlot? Slot, ComboBox Name, Button Swatch)> _materialRows = new List<(MaterialSlot?, ComboBox, Button)>();

        // Character panel
        ComboBox _font, _style, _kerning, _charMaterial;
        NumberField _size, _leading, _tracking, _vScale, _hScale, _baseline, _skew, _charRot, _underW, _underOff, _strikeW, _strikeOff;
        ToggleButton _allCaps, _smallCaps, _super, _sub, _underline, _strike;
        TextBlock _capHeight;
        List<(string Label, int Weight, bool Italic, int Stretch)> _styles = new List<(string, int, bool, int)>();

        // Paragraph panel
        readonly List<ToggleButton> _align = new List<ToggleButton>();
        NumberField _leftInd, _rightInd, _firstInd, _wordSp, _spBefore, _spAfter;
        RuleEditor _ruleAbove, _ruleBelow;

        // ================================================================== left: elements + sign

        UIElement BuildLeft()
        {
            var p = new StackPanel();
            p.Children.Add(Ui.Title("Elements"));
            _elements = new ListBox { Height = 170 };
            _elements.SelectionChanged += (s, e) =>
            {
                if (_loading) return;
                if (_elements.SelectedItem is ListBoxItem item) Select((string)item.Tag);
            };
            p.Children.Add(_elements);
            p.Children.Add(Ui.Note("Click an element in the preview or here; drag it in the preview to move it (Shift = 10 mm steps, Ctrl = straight, Alt = drag a copy), or nudge with the arrow keys. Tick Snap on the toolbar to land on the grid."));

            p.Children.Add(Ui.Title("Sign"));
            _signName = new TextBox();
            _signName.LostFocus += (s, e) => { if (!_loading && _signName.Text != _doc.Name) Change(() => _doc.Name = _signName.Text.Trim()); };
            p.Children.Add(Ui.Labelled("Family name", _signName, "The Revit family takes this name. Exporting again with the same name updates the signs already placed."));

            p.Children.Add(Ui.Title("Backpanel"));
            _panelOn = new CheckBox { Content = "Backpanel (the family can switch it on and off)", Margin = new Thickness(0, 2, 0, 2) };
            _panelOn.Click += (s, e) => Change(() => _doc.Panel.Enabled = _panelOn.IsChecked == true);
            p.Children.Add(_panelOn);
            _shape = Ui.Combo("Rectangle", "Ellipse / circle");
            _shape.SelectionChanged += (s, e) => { if (!_loading) Change(() => _doc.Panel.Shape = (PanelShape)_shape.SelectedIndex); };
            p.Children.Add(Ui.Labelled("Shape", _shape));
            _autoFit = new CheckBox { Content = "Fit panel to content + padding", Margin = new Thickness(0, 2, 0, 2) };
            _autoFit.Click += (s, e) => Change(() => _doc.Panel.AutoFit = _autoFit.IsChecked == true);
            p.Children.Add(_autoFit);
            _panelW = Num("W", "mm", v => _doc.Panel.Width = v, 10, 1, 20000, 0);
            _panelH = Num("H", "mm", v => _doc.Panel.Height = v, 10, 1, 20000, 0);
            p.Children.Add(Ui.Pair(_panelW, _panelH));
            _padX = Num("Pad X", "mm", v => _doc.Panel.PaddingX = v, 5, 0, 5000, 0, "Padding left and right (used when the panel fits the content, or when there is no backpanel)");
            _padY = Num("Pad Y", "mm", v => _doc.Panel.PaddingY = v, 5, 0, 5000, 0, "Padding top and bottom");
            p.Children.Add(Ui.Pair(_padX, _padY));
            _radius = Num("Corner R", "mm", v => _doc.Panel.CornerRadius = v, 5, 0, 5000, 0);
            p.Children.Add(Ui.Pair(_radius, null));

            p.Children.Add(Ui.Title("Construction (family parameters)"));
            _panelT = Num("Panel", "mm", v => _doc.Build.PanelThickness = v, 1, 0.5, 2000, 1, "Panel thickness");
            p.Children.Add(Ui.Pair(_panelT, null));
            _letterD = Num("Letter depth", "mm", v => _doc.Build.LetterDepth = v, 1, 0.5, 2000, 1, "Thickness of the letters (and logos)");
            _letterS = Num("Stand-off", "mm", v => _doc.Build.LetterStandoff = v, 1, 0, 2000, 1, "Gap between the panel (or wall) and the back of the letters");
            p.Children.Add(Ui.Pair(_letterD, _letterS));
            _ruleD = Num("Rule depth", "mm", v => _doc.Build.RuleDepth = v, 1, 0.5, 2000, 1, "Thickness of rules and rails");
            _ruleS = Num("Stand-off", "mm", v => _doc.Build.RuleStandoff = v, 1, 0, 2000, 1, "Gap behind rules and rails");
            p.Children.Add(Ui.Pair(_ruleD, _ruleS));

            p.Children.Add(Ui.Title("Materials"));
            p.Children.Add(MaterialRow("Panel", null));
            p.Children.Add(MaterialRow("Letters", MaterialSlot.Letters));
            p.Children.Add(MaterialRow("Accent", MaterialSlot.Accent));
            p.Children.Add(MaterialRow("Rules", MaterialSlot.Rules));
            p.Children.Add(Ui.Note("Pick a project material or type a new name. The swatch sets the preview colour (and the colour of a new material)."));

            p.Children.Add(Ui.Title("Revit family"));
            _hosting = Ui.Combo("Wall based", "Face based", "Freestanding");
            _hosting.SelectionChanged += (s, e) => { if (!_loading) Change(() => _doc.Build.Hosting = (Hosting)_hosting.SelectedIndex); };
            p.Children.Add(Ui.Labelled("Hosting", _hosting, "Wall based: placed on a wall and moves with it. Face based: on any face. Freestanding: anywhere."));
            _origin = Ui.Combo("Centre", "Bottom centre", "Bottom left", "Top left");
            _origin.SelectionChanged += (s, e) => { if (!_loading) Change(() => _doc.Build.Origin = (OriginPoint)_origin.SelectedIndex); };
            p.Children.Add(Ui.Labelled("Insertion point", _origin, "The family's insertion point on the sign. With Centre, the Elevation in Revit is the centre height."));

            _familySizes = new TextBox { Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
            void CommitSizes()
            {
                if (_loading) return;
                var sizes = ParseSizes(_familySizes.Text);
                if (sizes.SequenceEqual(_doc.Build.FamilySizes)) { _familySizes.Text = SizesText(sizes); return; }
                Change(() => _doc.Build.FamilySizes = sizes);
            }
            _familySizes.LostFocus += (s, e) => CommitSizes();
            _familySizes.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) CommitSizes(); };
            p.Children.Add(Ui.Labelled("Sizes (%)", _familySizes,
                "Each size becomes a family type, e.g. 75, 100, 150 gives the types 75%, 100% and 150%. Change the type in Revit to resize the whole sign."));
            p.Children.Add(Ui.Note("Several sizes, e.g. \"75, 100, 150\", give one family type per size: pick a type in Revit to resize the sign. Thicknesses stay the same in every size."));

            p.Children.Add(Ui.Title("Scale whole sign"));
            var scale = new NumberField("Scale", "%", 5, 1, 1000, 1, "Resizes everything: text, spacing, rules, graphics and the panel");
            scale.Value = 100;
            var depthsToo = new CheckBox { Content = "Thicknesses too", Margin = new Thickness(0, 2, 0, 2), ToolTip = "Also scale the panel thickness, letter and rule depths and stand-offs" };
            var apply = Ui.Button("Apply", "Scale the whole design now (Undo brings it back)", (s, e) =>
            {
                double pc = scale.Value ?? 100;
                if (Math.Abs(pc - 100) < 1e-6) return;
                Change(() => SignScaling.Scale(_doc, pc / 100, depthsToo.IsChecked == true));
                scale.Value = 100;
                _preview.FitToView();
            });
            p.Children.Add(Ui.Pair(scale, apply));
            p.Children.Add(depthsToo);
            return p;
        }

        static string SizesText(IEnumerable<int> sizes) => string.Join(", ", sizes);

        static List<int> ParseSizes(string text)
        {
            var sizes = (text ?? "").Split(new[] { ',', ';', ' ', '%', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? (int)Math.Round(v) : 0)
                .Where(v => v >= 5 && v <= 1000).Distinct().OrderBy(v => v).ToList();
            if (sizes.Count == 0) sizes.Add(100);
            return sizes;
        }

        NumberField Num(string label, string unit, Action<double> set, double step, double min, double max, int decimals, string tip = null)
        {
            var n = new NumberField(label, unit, step, min, max, decimals, tip);
            n.Committed += v => Change(() => set(v));
            return n;
        }

        UIElement MaterialRow(string label, MaterialSlot? slot)
        {
            var combo = new ComboBox { IsEditable = true, MinHeight = 24, IsTextSearchEnabled = true };
            foreach (var m in _projectMaterials) combo.Items.Add(m.Name);
            var swatch = new Button { Width = 24, Height = 22, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Preview colour" };
            MaterialRef Get() => slot == null ? _doc.Build.PanelMaterial : _doc.Build.For(slot.Value);
            void Set(MaterialRef m)
            {
                if (slot == null) _doc.Build.PanelMaterial = m;
                else if (slot == MaterialSlot.Accent) _doc.Build.AccentMaterial = m;
                else if (slot == MaterialSlot.Rules) _doc.Build.RuleMaterial = m;
                else _doc.Build.LetterMaterial = m;
            }
            void CommitName()
            {
                if (_loading) return;
                string name = (combo.Text ?? "").Trim();
                if (name.Length == 0 || name == Get().Name) return;
                var known = _projectMaterials.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                var cur = Get();
                Change(() => Set(known?.Clone() ?? new MaterialRef(name, cur.R, cur.G, cur.B)));
            }
            combo.SelectionChanged += (s, e) => { if (combo.SelectedItem is string sel) { combo.Text = sel; CommitName(); } };
            combo.LostFocus += (s, e) => CommitName();
            combo.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) CommitName(); };
            swatch.Click += (s, e) =>
            {
                var cur = Get();
                using (var dlg = new System.Windows.Forms.ColorDialog { Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B), FullOpen = true })
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        Change(() => { var m = cur.Clone(); m.R = dlg.Color.R; m.G = dlg.Color.G; m.B = dlg.Color.B; Set(m); });
            };
            _materialRows.Add((slot, combo, swatch));
            var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var l = new TextBlock { Text = label, Width = 56, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Header };
            DockPanel.SetDock(l, Dock.Left); d.Children.Add(l);
            DockPanel.SetDock(swatch, Dock.Right); d.Children.Add(swatch);
            d.Children.Add(combo);
            return d;
        }

        void RefreshSignPanel()
        {
            _signName.Text = _doc.Name;
            _panelOn.IsChecked = _doc.Panel.Enabled;
            _shape.SelectedIndex = (int)_doc.Panel.Shape;
            _autoFit.IsChecked = _doc.Panel.AutoFit;
            _panelW.Value = _doc.Panel.Width; _panelH.Value = _doc.Panel.Height;
            _panelW.IsEnabled = _panelH.IsEnabled = !_doc.Panel.AutoFit && _doc.Panel.Enabled;
            _padX.Value = _doc.Panel.PaddingX; _padY.Value = _doc.Panel.PaddingY;
            _radius.Value = _doc.Panel.CornerRadius;
            _panelT.Value = _doc.Build.PanelThickness;
            _letterD.Value = _doc.Build.LetterDepth; _letterS.Value = _doc.Build.LetterStandoff;
            _ruleD.Value = _doc.Build.RuleDepth; _ruleS.Value = _doc.Build.RuleStandoff;
            _hosting.SelectedIndex = (int)_doc.Build.Hosting;
            _origin.SelectedIndex = (int)_doc.Build.Origin;
            if (!_familySizes.IsKeyboardFocusWithin) _familySizes.Text = SizesText(_doc.Build.FamilySizes);
            foreach (var (slot, combo, swatch) in _materialRows)
            {
                var m = slot == null ? _doc.Build.PanelMaterial : _doc.Build.For(slot.Value);
                combo.Text = m.Name;
                swatch.Background = new SolidColorBrush(Color.FromRgb(m.R, m.G, m.B));
            }
        }

        // ================================================================== Character

        UIElement BuildCharacterPanel()
        {
            var p = new StackPanel { Margin = new Thickness(8) };
            _font = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, MinHeight = 26, FontSize = 13 };
            foreach (var n in WpfFontProvider.FamilyNames()) _font.Items.Add(n);
            _font.SelectionChanged += (s, e) => { if (_font.SelectedItem is string f) SetFont(f); };
            _font.LostFocus += (s, e) => SetFont(_font.Text);
            p.Children.Add(Ui.Title("Font"));
            p.Children.Add(_font);
            _style = new ComboBox { MinHeight = 24, Margin = new Thickness(0, 4, 0, 0) };
            _style.SelectionChanged += (s, e) =>
            {
                if (_loading || _style.SelectedIndex < 0 || _style.SelectedIndex >= _styles.Count) return;
                var st = _styles[_style.SelectedIndex];
                ApplyChar(c => { c.Weight = st.Weight; c.Italic = st.Italic; c.Stretch = st.Stretch; });
            };
            p.Children.Add(_style);
            p.Children.Add(Ui.Button("Load font file (.otf / .ttf)…", "Use a font that is not installed in Windows, e.g. GT America Standard .otf files", (s, e) => LoadFontFile()));

            p.Children.Add(Ui.Title("Size and spacing"));
            _size = CharNum("Size", "mm", (c, v) => c.SizeMm = v, 1, 1, 5000, 1, "Type size (em) in mm. Cap height is shown below.");
            _leading = CharNum("Leading", "mm", (c, v) => c.LeadingMm = v, 1, 0, 10000, 1, "Baseline to baseline. 0 or 'Auto' = 120% of the size");
            _leading.AutoText = "Auto";
            p.Children.Add(Ui.Pair(_size, _leading));
            _kerning = Ui.Combo("Metrics", "None");
            _kerning.ToolTip = "Kerning: the font's own pair spacing, or none";
            _kerning.SelectionChanged += (s, e) => { if (!_loading && _kerning.SelectedIndex >= 0) { var k = (KerningMode)_kerning.SelectedIndex; ApplyChar(c => c.Kerning = k); } };
            _tracking = CharNum("Track", "/1000", (c, v) => c.Tracking = v, 10, -1000, 10000, 0, "Tracking: extra space between all letters, in thousandths of an em");
            p.Children.Add(Ui.Pair(Ui.Labelled("Kerning", _kerning), _tracking));
            _vScale = CharNum("V scale", "%", (c, v) => c.VerticalScale = v, 1, 1, 1000, 1, "Stretch letters vertically");
            _hScale = CharNum("H scale", "%", (c, v) => c.HorizontalScale = v, 1, 1, 1000, 1, "Stretch letters horizontally (condense / extend)");
            p.Children.Add(Ui.Pair(_vScale, _hScale));
            _baseline = CharNum("Baseline", "mm", (c, v) => c.BaselineShiftMm = v, 1, -5000, 5000, 1, "Baseline shift: move characters up (+) or down (-)");
            _skew = CharNum("Skew", "°", (c, v) => c.SkewDeg = v, 1, -60, 60, 1, "Slant characters (false italic)");
            p.Children.Add(Ui.Pair(_baseline, _skew));
            _charRot = CharNum("Rotate", "°", (c, v) => c.RotationDeg = v, 1, -360, 360, 1, "Rotate each character about its own centre");
            _capHeight = new TextBlock { Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
            p.Children.Add(Ui.Pair(_charRot, _capHeight));

            p.Children.Add(Ui.Title("Style"));
            var toggles = new WrapPanel();
            _allCaps = Ui.Toggle("TT", "All caps"); _allCaps.Click += (s, e) => { bool on = _allCaps.IsChecked == true; ApplyChar(c => c.Caps = on ? CapsMode.AllCaps : CapsMode.Normal); };
            _smallCaps = Ui.Toggle("Tᴛ", "Small caps"); _smallCaps.Click += (s, e) => { bool on = _smallCaps.IsChecked == true; ApplyChar(c => c.Caps = on ? CapsMode.SmallCaps : CapsMode.Normal); };
            _super = Ui.Toggle("T¹", "Superscript"); _super.Click += (s, e) => { bool on = _super.IsChecked == true; ApplyChar(c => c.Position = on ? ScriptPosition.Superscript : ScriptPosition.Normal); };
            _sub = Ui.Toggle("T₁", "Subscript"); _sub.Click += (s, e) => { bool on = _sub.IsChecked == true; ApplyChar(c => c.Position = on ? ScriptPosition.Subscript : ScriptPosition.Normal); };
            _underline = Ui.Toggle("T", "Underline (Ctrl+U)", deco: TextDecorations.Underline); _underline.Click += (s, e) => { bool on = _underline.IsChecked == true; ApplyChar(c => c.Underline = on); };
            _strike = Ui.Toggle("T", "Strikethrough", deco: TextDecorations.Strikethrough); _strike.Click += (s, e) => { bool on = _strike.IsChecked == true; ApplyChar(c => c.Strikethrough = on); };
            foreach (var t in new[] { _allCaps, _smallCaps, _super, _sub, _underline, _strike }) toggles.Children.Add(t);
            p.Children.Add(toggles);

            p.Children.Add(Ui.Title("Underline / strikethrough"));
            _underW = CharNumOpt("U weight", (c, v) => c.UnderlineWeightMm = v, "Underline thickness. Blank = the font's own");
            _underOff = CharNumOpt("U offset", (c, v) => c.UnderlineOffsetMm = v, "Distance from the baseline down to the top of the underline. Blank = the font's own");
            p.Children.Add(Ui.Pair(_underW, _underOff));
            _strikeW = CharNumOpt("S weight", (c, v) => c.StrikeWeightMm = v, "Strikethrough thickness");
            _strikeOff = CharNumOpt("S height", (c, v) => c.StrikeOffsetMm = v, "Height of the strikethrough centre above the baseline");
            p.Children.Add(Ui.Pair(_strikeW, _strikeOff));

            p.Children.Add(Ui.Title("Material"));
            _charMaterial = Ui.Combo("Letters", "Accent", "Rules");
            _charMaterial.ToolTip = "Which material these characters are made of (e.g. one floor number in brass)";
            _charMaterial.SelectionChanged += (s, e) => { if (!_loading && _charMaterial.SelectedIndex >= 0) { var m = (MaterialSlot)_charMaterial.SelectedIndex; ApplyChar(c => c.Material = m); } };
            p.Children.Add(_charMaterial);
            p.Children.Add(Ui.Note("Applies to the selected text, or to the whole line when nothing is selected."));
            return p;
        }

        NumberField CharNum(string label, string unit, Action<CharacterFormat, double> set, double step, double min, double max, int decimals, string tip)
        {
            var n = new NumberField(label, unit, step, min, max, decimals, tip, 52);
            n.Committed += v => ApplyChar(c => set(c, v));
            return n;
        }

        NumberField CharNumOpt(string label, Action<CharacterFormat, double?> set, string tip)
        {
            var n = new NumberField(label, "mm", 0.5, -1000, 1000, 2, tip, 52) { AutoText = "" };
            n.Committed += v => ApplyChar(c => set(c, Math.Abs(v) < 1e-9 ? (double?)null : v));
            return n;
        }

        void SetFont(string family)
        {
            if (_loading || string.IsNullOrWhiteSpace(family)) return;
            family = family.Trim();
            var formats = Frame == null ? null : TextEditing.FormatsIn(Frame, SelStart, SelLength);
            if (formats != null && formats.All(f => f.FontFamily == family)) return;
            // Keep the closest weight/style the new family has.
            var styles = WpfFontProvider.Styles(family);
            ApplyChar(c =>
            {
                var best = styles.OrderBy(s => Math.Abs(s.Weight - c.Weight) + (s.Italic != c.Italic ? 1000 : 0) + Math.Abs(s.Stretch - c.Stretch) * 50).FirstOrDefault();
                c.FontFamily = family;
                if (best.Label != null) { c.Weight = best.Weight; c.Italic = best.Italic; c.Stretch = best.Stretch; }
            });
        }

        void LoadFontFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Fonts (*.otf;*.ttf;*.ttc)|*.otf;*.ttf;*.ttc", Multiselect = true };
            if (dlg.ShowDialog(this) != true) return;
            var names = new List<string>();
            foreach (var f in dlg.FileNames)
            {
                try { names.AddRange(WpfFontProvider.AddFontFile(f)); }
                catch (Exception ex) { MessageBox.Show(this, $"{System.IO.Path.GetFileName(f)}: {ex.Message}", "Load font"); }
            }
            names = names.Distinct().ToList();
            _loading = true;
            try
            {
                _font.Items.Clear();
                foreach (var n in WpfFontProvider.FamilyNames()) _font.Items.Add(n);
            }
            finally { _loading = false; }
            if (names.Count > 0)
            {
                _status.Text = "Loaded font: " + string.Join(", ", names) + ". It stays available in the designer (kept in " + WpfFontProvider.PrivateFontFolder + ").";
                SetFont(names[0]);
            }
        }

        // ================================================================== Paragraph

        UIElement BuildParagraphPanel()
        {
            var p = new StackPanel { Margin = new Thickness(8) };
            p.Children.Add(Ui.Title("Alignment"));
            var row = new WrapPanel();
            string[] glyphs = { "⯇", "≡", "⯈", "☰", "▤" };
            string[] tips = { "Align left", "Align centre", "Align right", "Justify (last line left)", "Justify all lines (force)" };
            string[] labels = { "Left", "Centre", "Right", "Justify", "Force" };
            for (int i = 0; i < 5; i++)
            {
                var t = new ToggleButton { Content = labels[i], ToolTip = tips[i], Height = 26, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 3, 3) };
                int idx = i;
                t.Click += (s, e) => ApplyPara(pf => pf.Align = (TextAlign)idx);
                _align.Add(t);
                row.Children.Add(t);
            }
            p.Children.Add(row);

            p.Children.Add(Ui.Title("Indents and spacing"));
            _leftInd = ParaNum("Left", (f, v) => f.LeftIndentMm = v, "Left indent");
            _rightInd = ParaNum("Right", (f, v) => f.RightIndentMm = v, "Right indent");
            p.Children.Add(Ui.Pair(_leftInd, _rightInd));
            _firstInd = ParaNum("First line", (f, v) => f.FirstLineIndentMm = v, "First line indent");
            _wordSp = new NumberField("Word sp.", "/1000", 10, -500, 5000, 0, "Extra space between words, thousandths of an em", 52);
            _wordSp.Committed += v => ApplyPara(f => f.WordSpacing = v);
            p.Children.Add(Ui.Pair(_firstInd, _wordSp));
            _spBefore = ParaNum("Before", (f, v) => f.SpaceBeforeMm = v, "Space before the paragraph");
            _spAfter = ParaNum("After", (f, v) => f.SpaceAfterMm = v, "Space after the paragraph");
            p.Children.Add(Ui.Pair(_spBefore, _spAfter));

            _ruleAbove = new RuleEditor("Rule above", "Offset: from the top of the capitals up to the bottom of the rule", (act) => ApplyPara(f => act(f.RuleAbove)));
            _ruleBelow = new RuleEditor("Rule below", "Offset: from the baseline down to the top of the rule (0 = letters sit on the rule)", (act) => ApplyPara(f => act(f.RuleBelow)));
            p.Children.Add(_ruleAbove);
            p.Children.Add(_ruleBelow);
            p.Children.Add(Ui.Note("Paragraph rules are the lines under or over each entry in a directory. They use the Rules material unless set otherwise."));
            return p;
        }

        NumberField ParaNum(string label, Action<ParagraphFormat, double> set, string tip)
        {
            var n = new NumberField(label, "mm", 1, -10000, 10000, 1, tip, 52);
            n.Committed += v => ApplyPara(f => set(f, v));
            return n;
        }

        /// <summary>The settings of one paragraph rule (InDesign's Paragraph Rules dialog).</summary>
        sealed class RuleEditor : StackPanel
        {
            readonly CheckBox _on;
            readonly NumberField _weight, _offset, _length, _left, _right;
            readonly ComboBox _width, _material;
            bool _setting;

            public RuleEditor(string title, string offsetTip, Action<Action<ParagraphRule>> apply)
            {
                Children.Add(Ui.Title(title));
                _on = new CheckBox { Content = "On" };
                _on.Click += (s, e) => { bool on = _on.IsChecked == true; apply(r => r.On = on); };
                Children.Add(_on);
                _weight = new NumberField("Weight", "mm", 0.5, 0.1, 1000, 2, "Rule thickness", 52);
                _weight.Committed += v => apply(r => r.WeightMm = v);
                _offset = new NumberField("Offset", "mm", 1, -1000, 1000, 1, offsetTip, 52);
                _offset.Committed += v => apply(r => r.OffsetMm = v);
                Children.Add(Ui.Pair(_weight, _offset));
                _width = Ui.Combo("Text", "Column", "Fixed");
                _width.ToolTip = "Text: as long as the line of text. Column: the frame width. Fixed: the length given";
                _width.SelectionChanged += (s, e) => { if (!_setting && _width.SelectedIndex >= 0) { var w = (RuleWidthMode)_width.SelectedIndex; apply(r => r.Width = w); } };
                _length = new NumberField("Length", "mm", 10, 0, 100000, 0, "Length for Fixed width", 52);
                _length.Committed += v => apply(r => r.LengthMm = v);
                Children.Add(Ui.Pair(Ui.Labelled("Width", _width), _length));
                _left = new NumberField("Extend L", "mm", 5, -10000, 10000, 0, "Extend the rule past the left end (negative shortens)", 52);
                _left.Committed += v => apply(r => r.ExtendLeftMm = v);
                _right = new NumberField("Extend R", "mm", 5, -10000, 10000, 0, "Extend the rule past the right end", 52);
                _right.Committed += v => apply(r => r.ExtendRightMm = v);
                Children.Add(Ui.Pair(_left, _right));
                _material = Ui.Combo("Letters", "Accent", "Rules");
                _material.SelectionChanged += (s, e) => { if (!_setting && _material.SelectedIndex >= 0) { var m = (MaterialSlot)_material.SelectedIndex; apply(r => r.Material = m); } };
                Children.Add(Ui.Labelled("Material", _material));
            }

            public void Show(List<ParagraphRule> rules)
            {
                _setting = true;
                try
                {
                    var r = rules.FirstOrDefault() ?? new ParagraphRule();
                    _on.IsChecked = rules.All(x => x.On) ? true : rules.Any(x => x.On) ? (bool?)null : false;
                    _weight.Value = Same(rules, x => x.WeightMm);
                    _offset.Value = Same(rules, x => x.OffsetMm);
                    _width.SelectedIndex = rules.All(x => x.Width == r.Width) ? (int)r.Width : -1;
                    _length.Value = Same(rules, x => x.LengthMm);
                    _left.Value = Same(rules, x => x.ExtendLeftMm);
                    _right.Value = Same(rules, x => x.ExtendRightMm);
                    _material.SelectedIndex = rules.All(x => x.Material == r.Material) ? (int)r.Material : -1;
                }
                finally { _setting = false; }
            }
        }

        static double? Same<T>(IList<T> items, Func<T, double> get)
        {
            if (items.Count == 0) return null;
            double v = get(items[0]);
            return items.All(i => Math.Abs(get(i) - v) < 1e-9) ? v : (double?)null;
        }

        static double? SameOpt<T>(IList<T> items, Func<T, double?> get)
        {
            if (items.Count == 0) return null;
            var v = get(items[0]);
            return items.All(i => get(i) == v) ? (v ?? 0) : (double?)null;
        }

        // ================================================================== show selection in the text panels

        void RefreshTextPanels()
        {
            var f = Frame;
            _charTab.IsEnabled = _paraTab.IsEnabled = f != null;
            if (f == null) return;
            bool was = _loading;
            _loading = true;
            try
            {
                var cs = TextEditing.FormatsIn(f, SelStart, SelLength);
                var c0 = cs[0];
                bool sameFont = cs.All(c => c.FontFamily == c0.FontFamily);
                _font.SelectedItem = null;
                _font.Text = sameFont ? c0.FontFamily : "";
                _styles = WpfFontProvider.Styles(c0.FontFamily);
                _style.Items.Clear();
                foreach (var s in _styles) _style.Items.Add(s.Label);
                bool sameStyle = cs.All(c => c.Weight == c0.Weight && c.Italic == c0.Italic && c.Stretch == c0.Stretch);
                _style.SelectedIndex = sameStyle ? _styles.FindIndex(s => s.Weight == c0.Weight && s.Italic == c0.Italic && s.Stretch == c0.Stretch) : -1;
                if (sameStyle && _style.SelectedIndex < 0) _style.Text = WpfFontProvider.WeightName(c0.Weight) + (c0.Italic ? " Italic" : "");

                _size.Value = Same(cs, c => c.SizeMm);
                _leading.Value = Same(cs, c => c.LeadingMm);
                _kerning.SelectedIndex = cs.All(c => c.Kerning == c0.Kerning) ? (int)c0.Kerning : -1;
                _tracking.Value = Same(cs, c => c.Tracking);
                _vScale.Value = Same(cs, c => c.VerticalScale);
                _hScale.Value = Same(cs, c => c.HorizontalScale);
                _baseline.Value = Same(cs, c => c.BaselineShiftMm);
                _skew.Value = Same(cs, c => c.SkewDeg);
                _charRot.Value = Same(cs, c => c.RotationDeg);
                _allCaps.IsChecked = cs.All(c => c.Caps == CapsMode.AllCaps);
                _smallCaps.IsChecked = cs.All(c => c.Caps == CapsMode.SmallCaps);
                _super.IsChecked = cs.All(c => c.Position == ScriptPosition.Superscript);
                _sub.IsChecked = cs.All(c => c.Position == ScriptPosition.Subscript);
                _underline.IsChecked = cs.All(c => c.Underline);
                _strike.IsChecked = cs.All(c => c.Strikethrough);
                _underW.Value = SameOpt(cs, c => c.UnderlineWeightMm);
                _underOff.Value = SameOpt(cs, c => c.UnderlineOffsetMm);
                _strikeW.Value = SameOpt(cs, c => c.StrikeWeightMm);
                _strikeOff.Value = SameOpt(cs, c => c.StrikeOffsetMm);
                _charMaterial.SelectedIndex = cs.All(c => c.Material == c0.Material) ? (int)c0.Material : -1;
                var face = WpfFontProvider.Instance.Resolve(c0.FontFamily, c0.Weight, c0.Italic, c0.Stretch);
                _capHeight.Text = $"  Cap height {face.CapHeight * c0.SizeMm * c0.VerticalScale / 100:0.#} mm";

                var ps = TextEditing.ParagraphsIn(f, SelStart, SelLength);
                var p0 = ps[0];
                for (int i = 0; i < _align.Count; i++) _align[i].IsChecked = ps.All(p => (int)p.Align == i);
                _leftInd.Value = Same(ps, p => p.LeftIndentMm);
                _rightInd.Value = Same(ps, p => p.RightIndentMm);
                _firstInd.Value = Same(ps, p => p.FirstLineIndentMm);
                _wordSp.Value = Same(ps, p => p.WordSpacing);
                _spBefore.Value = Same(ps, p => p.SpaceBeforeMm);
                _spAfter.Value = Same(ps, p => p.SpaceAfterMm);
                _ruleAbove.Show(ps.Select(p => p.RuleAbove).ToList());
                _ruleBelow.Show(ps.Select(p => p.RuleBelow).ToList());
            }
            finally { _loading = was; }
        }

        // ================================================================== Position (selected element)

        void BuildPositionPanel()
        {
            _positionPanel.Children.Clear();
            var f = Frame; var r = Rule; var g = Graphic;
            if (f == null && r == null && g == null)
            {
                _positionPanel.Children.Add(Ui.Note("Select an element to place it precisely."));
                return;
            }
            void Name(string current, Action<string> set)
            {
                var tb = new TextBox { Text = current };
                tb.LostFocus += (s, e) => { if (tb.Text != current) Change(() => set(tb.Text.Trim())); };
                _positionPanel.Children.Add(Ui.Labelled("Name", tb));
            }
            NumberField N(string label, string unit, double value, Action<double> set, double step = 1, double min = -100000, double max = 100000, int dec = 1, string tip = null)
            {
                var n = new NumberField(label, unit, step, min, max, dec, tip, 60) { Value = value };
                n.Committed += v => Change(() => set(v));
                return n;
            }
            var panel = _geometry?.PanelRect ?? new Core.Geometry.Rect2(0, 0, 1000, 400);

            if (f != null)
            {
                _positionPanel.Children.Add(Ui.Title("Text frame"));
                Name(f.Name, v => f.Name = v);
                _positionPanel.Children.Add(Ui.Pair(N("X", "mm", f.X, v => f.X = v, tip: "Left edge of the frame"), N("Y", "mm", f.Y, v => f.Y = v, tip: "Top of the frame = top of the capitals of the first line")));
                var w = N("Width", "mm", f.Width, v => f.Width = v, 10, 0, 100000, 0, "Frame width for wrapping and alignment. Auto (0) = as wide as the longest line, no wrapping");
                w.AutoText = "Auto";
                w.Value = f.Width;
                _positionPanel.Children.Add(Ui.Pair(w, N("Rotate", "°", f.RotationDeg, v => f.RotationDeg = v, 1, -360, 360, 1, "Rotate the frame about its top-left corner (anticlockwise)")));
                AddAlignButtons(id: f.Id);
            }
            else if (r != null)
            {
                _positionPanel.Children.Add(Ui.Title("Rule / rail"));
                Name(r.Name, v => r.Name = v);
                _positionPanel.Children.Add(Ui.Pair(N("X", "mm", r.X, v => r.X = v, tip: "Start point"), N("Y", "mm", r.Y, v => r.Y = v)));
                _positionPanel.Children.Add(Ui.Pair(N("Length", "mm", r.Length, v => r.Length = v, 10, 1, 100000, 0), N("Angle", "°", r.AngleDeg, v => r.AngleDeg = v, 1, -360, 360, 1, "0 = horizontal, 90 = straight up")));
                _positionPanel.Children.Add(Ui.Pair(N("Weight", "mm", r.WeightMm, v => r.WeightMm = v, 0.5, 0.1, 1000, 2, "Thickness of the bar on the sign face"), null));
                var mat = Ui.Combo("Letters", "Accent", "Rules");
                mat.SelectedIndex = (int)r.Material;
                mat.SelectionChanged += (s, e) => { if (!_loading) Change(() => r.Material = (MaterialSlot)mat.SelectedIndex); };
                _positionPanel.Children.Add(Ui.Labelled("Material", mat, "Rules material: built at the rule depth and stand-off. Letters / Accent: built like letters."));
                AddAlignButtons(id: r.Id);
            }
            else
            {
                _positionPanel.Children.Add(Ui.Title("Graphic"));
                Name(g.Name, v => g.Name = v);
                _positionPanel.Children.Add(Ui.Note("From " + g.Source + $" ({g.NativeWidth:0.#} × {g.NativeHeight:0.#} mm at 100%)"));
                _positionPanel.Children.Add(Ui.Pair(N("X", "mm", g.X, v => g.X = v), N("Y", "mm", g.Y, v => g.Y = v)));
                _positionPanel.Children.Add(Ui.Pair(N("Scale", "%", g.ScalePercent, v => g.ScalePercent = v, 1, 0.01, 100000, 2),
                    N("Width", "mm", g.NativeWidth * g.ScalePercent / 100, v => g.ScalePercent = g.NativeWidth > 0 ? v / g.NativeWidth * 100 : 100, 10, 0.1, 100000, 1, "Set the width; the height follows")));
                _positionPanel.Children.Add(Ui.Pair(N("Rotate", "°", g.RotationDeg, v => g.RotationDeg = v, 1, -360, 360), N("Lines", "mm", g.StrokeMm, v => g.StrokeMm = v, 0.5, 0, 1000, 2, "Width given to open lines (0 leaves them out). Closed shapes are always solid.")));
                var mirror = new CheckBox { Content = "Mirror", IsChecked = g.Mirror };
                mirror.Click += (s, e) => Change(() => g.Mirror = mirror.IsChecked == true);
                _positionPanel.Children.Add(mirror);
                var mat = Ui.Combo("Letters", "Accent", "Rules");
                mat.SelectedIndex = (int)g.Material;
                mat.SelectionChanged += (s, e) => { if (!_loading) Change(() => g.Material = (MaterialSlot)mat.SelectedIndex); };
                _positionPanel.Children.Add(Ui.Labelled("Material", mat));
                AddAlignButtons(id: g.Id);
            }

            void AddAlignButtons(string id)
            {
                _positionPanel.Children.Add(Ui.Title("Align to panel"));
                var row = new WrapPanel();
                row.Children.Add(Ui.Button("Centre ↔", "Centre horizontally on the panel", (s, e) => AlignTo(id, true)));
                row.Children.Add(Ui.Button("Centre ↕", "Centre vertically on the panel", (s, e) => AlignTo(id, false)));
                _positionPanel.Children.Add(row);
                _positionPanel.Children.Add(Ui.Note($"Panel: {panel.Width:0} × {panel.Height:0} mm. Drag in the preview, or use the arrow keys there (Shift = 10 mm)."));
            }
        }

        void AlignTo(string id, bool horizontal)
        {
            if (_geometry == null || !_geometry.ElementBoxes.TryGetValue(id, out var box)) return;
            var panel = _doc.Panel.AutoFit || !_doc.Panel.Enabled ? (Core.Geometry.Rect2?)null : _geometry.PanelRect;
            if (panel == null) { _status.Text = "Centring needs a fixed-size backpanel (the panel follows the content otherwise)."; return; }
            // Centre the visible content of the element, not its frame.
            var content = _geometry.Pieces.Where(p => p.ElementId == id).Aggregate(Core.Geometry.Rect2.Empty, (a, p) => a.Include(p.Bounds));
            if (content.IsEmpty) content = Core.Geometry.Rect2.Of(box);
            double dx = horizontal ? panel.Value.Center.X - content.Center.X : 0;
            double dy = horizontal ? 0 : panel.Value.Center.Y - content.Center.Y;
            Change(() => MoveBy(id, Math.Round(dx, 1), Math.Round(dy, 1)));
        }
    }
}

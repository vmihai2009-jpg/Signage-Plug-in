using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using UIApplication = Autodesk.Revit.UI.UIApplication;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using BGSignage.Core.Editing;
using BGSignage.Core.Import;
using BGSignage.Core.Layout;
using BGSignage.Core.Model;
using BGSignage.Engine.RevitExport;

namespace BGSignage.Engine.UI
{
    /// <summary>
    /// The Sign Designer: elements and sign settings on the left, the live preview in the middle with the text
    /// editor under it, and InDesign-style Character / Paragraph / Position panels on the right.
    /// </summary>
    internal sealed partial class DesignerWindow : Window
    {
        static DesignerWindow _open;

        readonly UIApplication _ui;
        readonly List<MaterialRef> _projectMaterials;
        readonly DesignerSettings _settings = DesignerSettings.Load();
        SignDocument _doc;
        string _selId;
        /// <summary>Everything selected (the primary, <see cref="_selId"/>, included).</summary>
        readonly HashSet<string> _multi = new HashSet<string>();
        /// <summary>The project family this design was opened from: Export then updates it in place.</summary>
        string _sourceFamily;
        List<SignDocument> _projectSigns = new List<SignDocument>();
        ComboBox _source;
        Button _exportButton;
        SignGeometry _geometry;
        bool _loading;

        readonly List<string> _undo = new List<string>(), _redo = new List<string>();
        DateTime _lastTyping = DateTime.MinValue;
        Dictionary<string, (double X, double Y)> _dragOrigin;

        // Main areas
        PreviewCanvas _preview;
        TextBox _editor;
        TextBlock _editorHint, _status, _pointer;
        ListBox _elements;
        StackPanel _positionPanel;
        TabControl _tabs;
        TabItem _charTab, _paraTab, _posTab;
        ToggleButton _view3d;
        CheckBox _placeAfter;

        public static void Open(UIApplication ui, SignDocument sign, List<MaterialRef> materials, List<SignDocument> projectSigns = null)
        {
            if (_open != null && _open.IsLoaded)
            {
                if (projectSigns != null) { _open._projectSigns = projectSigns; _open.RefreshSourceList(); }
                if (sign != null) { _open.Snapshot(); _open.Load(sign, $"Editing \"{sign.Name}\" from the model. Export updates it in place.", keepUndo: true); _open.SetSource(sign.Name); }
                if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
                _open.Activate();
                return;
            }
            _open = new DesignerWindow(ui, sign ?? Presets.Create(Presets.Names[0]), materials ?? new List<MaterialRef>());
            _open._projectSigns = projectSigns ?? new List<SignDocument>();
            _open.Closed += (s, e) => _open = null;
            _open.Show();
            _open.RefreshSourceList();
            _open.SetSource(sign?.Name);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        DesignerWindow(UIApplication ui, SignDocument sign, List<MaterialRef> materials)
        {
            _ui = ui;
            _projectMaterials = materials;
            _doc = sign.Normalise();
            Title = "BG Signage – Sign Designer";
            Width = 1440; Height = 900; MinWidth = 1100; MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.PanelBack;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            try { new WindowInteropHelper(this).Owner = ui.MainWindowHandle; } catch { }

            Content = BuildLayout();
            PreviewKeyDown += OnWindowKey;
            Loaded += (s, e) =>
            {
                _selId = _doc.Frames.FirstOrDefault()?.Id;
                LoadEditor();
                Rebuild(true);
                _preview.FitToView();
            };
        }

        // ------------------------------------------------------------------ layout

        UIElement BuildLayout()
        {
            var root = new DockPanel();

            var toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            var statusBar = new DockPanel { Background = Brushes.WhiteSmoke, Height = 24 };
            _pointer = new TextBlock { Margin = new Thickness(8, 4, 8, 0), Foreground = Ui.Muted, Width = 160 };
            DockPanel.SetDock(_pointer, Dock.Right);
            statusBar.Children.Add(_pointer);
            _status = new TextBlock { Margin = new Thickness(8, 4, 8, 0), Foreground = Ui.Header, TextTrimming = TextTrimming.CharacterEllipsis };
            statusBar.Children.Add(_status);
            DockPanel.SetDock(statusBar, Dock.Bottom);
            root.Children.Add(statusBar);

            var left = new ScrollViewer { Width = 290, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = BuildLeft(), Padding = new Thickness(10, 0, 10, 10) };
            DockPanel.SetDock(left, Dock.Left);
            root.Children.Add(left);

            _tabs = new TabControl { Width = 320, Margin = new Thickness(0, 6, 6, 6) };
            _charTab = new TabItem { Header = "Character", Content = Scroll(BuildCharacterPanel()) };
            _paraTab = new TabItem { Header = "Paragraph", Content = Scroll(BuildParagraphPanel()) };
            _positionPanel = new StackPanel { Margin = new Thickness(8) };
            _posTab = new TabItem { Header = "Position", Content = Scroll(_positionPanel) };
            _tabs.Items.Add(_charTab); _tabs.Items.Add(_paraTab); _tabs.Items.Add(_posTab);
            DockPanel.SetDock(_tabs, Dock.Right);
            root.Children.Add(_tabs);

            root.Children.Add(BuildCentre());
            return root;
        }

        static ScrollViewer Scroll(UIElement content) => new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        UIElement BuildToolbar()
        {
            var bar = new WrapPanel { Margin = new Thickness(8, 6, 8, 6) };
            _source = new ComboBox { Width = 290, Margin = new Thickness(0, 0, 4, 0), MinHeight = 24, MaxDropDownHeight = 600,
                                     ToolTip = "Open a sign that is loaded in this project (to edit and update it), or start from a template" };
            _source.SelectionChanged += (s, e) => OnSourcePicked();
            bar.Children.Add(_source);
            bar.Children.Add(Ui.Button("Open…", "Open a saved sign (.bgsign)", (s, e) => OpenFile()));
            bar.Children.Add(Ui.Button("Save…", "Save the sign design (.bgsign) to reuse or share", (s, e) => SaveFile()));
            bar.Children.Add(Sep());
            bar.Children.Add(Ui.Button("↶ Undo", "Undo (Ctrl+Z)", (s, e) => Undo()));
            bar.Children.Add(Ui.Button("↷ Redo", "Redo (Ctrl+Y)", (s, e) => Redo()));
            bar.Children.Add(Sep());
            bar.Children.Add(Ui.Button("+ Text", "Add a text frame", (s, e) => AddFrame()));
            bar.Children.Add(Ui.Button("+ Rule", "Add a bar or rail (any angle)", (s, e) => AddRule()));
            bar.Children.Add(Ui.Button("+ Graphic…", "Import a logo or graphic from .dwg, .dxf, .ai or .pdf", (s, e) => ImportGraphic()));
            bar.Children.Add(Ui.Button("Duplicate", "Duplicate the selected element (Ctrl+D)", (s, e) => DuplicateSelected()));
            bar.Children.Add(Ui.Button("Delete", "Delete the selected element (Delete key in the preview)", (s, e) => DeleteSelected()));
            bar.Children.Add(Sep());
            _view3d = new ToggleButton { Content = "3D view", ToolTip = "Show depth, stand-off and shadows", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 4, 0) };
            _view3d.Click += (s, e) => { _preview.ThreeD = _view3d.IsChecked == true; _preview.InvalidateVisual(); };
            bar.Children.Add(_view3d);
            bar.Children.Add(Ui.Button("Fit", "Zoom to the whole sign", (s, e) => _preview.FitToView()));
            var grid = new CheckBox { Content = "Grid", IsChecked = _settings.ShowGrid, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
            grid.Click += (s, e) => { _settings.ShowGrid = _preview.ShowGrid = grid.IsChecked == true; _settings.Save(); _preview.InvalidateVisual(); };
            bar.Children.Add(grid);
            var snap = new CheckBox { Content = "Snap", IsChecked = _settings.SnapToGrid, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
                                      ToolTip = "Snap to the grid: dragged elements land on gridlines and the arrow keys move one grid step" };
            snap.Click += (s, e) => { _settings.SnapToGrid = snap.IsChecked == true; _settings.Save(); };
            bar.Children.Add(snap);
            var gridSize = Ui.Combo(GridSizes.Select(g => g + " mm").ToArray());
            gridSize.Width = 70; gridSize.Margin = new Thickness(0, 0, 8, 0); gridSize.ToolTip = "Grid spacing";
            int gi = Array.IndexOf(GridSizes, _settings.GridMm);
            gridSize.SelectedIndex = gi >= 0 ? gi : Array.IndexOf(GridSizes, 10.0);
            gridSize.SelectionChanged += (s, e) =>
            {
                if (gridSize.SelectedIndex < 0) return;
                _settings.GridMm = _preview.GridMm = GridSizes[gridSize.SelectedIndex];
                _settings.Save();
                _preview.InvalidateVisual();
            };
            bar.Children.Add(gridSize);
            bar.Children.Add(Sep());
            _exportButton = Ui.Button("Export to Revit", "Build the parametric sign family, load it into the project and place it", (s, e) => Export(), primary: true);
            bar.Children.Add(_exportButton);
            _placeAfter = new CheckBox { Content = "Place after export", IsChecked = _settings.PlaceAfterExport, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            _placeAfter.Click += (s, e) => { _settings.PlaceAfterExport = _placeAfter.IsChecked == true; _settings.Save(); };
            bar.Children.Add(_placeAfter);
            bar.Children.Add(Ui.Button("Folder…", "Where the .rfa families are saved", (s, e) => ChooseExportFolder()));
            return bar;
        }

        static readonly double[] GridSizes = { 1, 2, 5, 10, 20, 25, 50, 100 };

        static UIElement Sep() => new Border { Width = 1, Background = Ui.Border, Margin = new Thickness(6, 2, 8, 2) };

        UIElement BuildCentre()
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 6, 6) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(130) });

            _preview = new PreviewCanvas { ShowGrid = _settings.ShowGrid, GridMm = _settings.GridMm > 0 ? _settings.GridMm : 10 };
            _preview.ElementClicked += id => OnPreviewClicked(id);
            _preview.MarqueeSelected += (ids, add) => SelectMany(add ? _multi.Concat(ids) : ids, ids.FirstOrDefault() ?? _selId);
            _preview.ElementDoubleClicked += id => { Select(id); if (Frame != null) _editor.Focus(); };
            _preview.DragStarted += id => StartDrag(id);
            _preview.Dragging += (id, dx, dy) => DragTo(id, dx, dy);
            _preview.PointerMoved += p => _pointer.Text = $"x {p.X:0}  y {p.Y:0} mm";
            _preview.KeyDown += OnPreviewKey;
            var frame = new Border { BorderBrush = Ui.Border, BorderThickness = new Thickness(1), Child = _preview };
            grid.Children.Add(frame);

            var head = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
            head.Children.Add(new TextBlock { Text = "TEXT", FontWeight = FontWeights.SemiBold, FontSize = 10.5, Foreground = Ui.Header, Margin = new Thickness(0, 0, 8, 0) });
            _editorHint = new TextBlock { Foreground = Ui.Muted, FontSize = 11 };
            head.Children.Add(_editorHint);
            Grid.SetRow(head, 1);
            grid.Children.Add(head);

            _editor = new TextBox
            {
                AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, FontSize = 15,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsUndoEnabled = false,
                IsInactiveSelectionHighlightEnabled = true, Padding = new Thickness(4),
            };
            _editor.TextChanged += OnEditorTextChanged;
            _editor.SelectionChanged += (s, e) => { if (!_loading) RefreshTextPanels(); };
            Grid.SetRow(_editor, 2);
            grid.Children.Add(_editor);
            return grid;
        }

        // ------------------------------------------------------------------ selection helpers

        TextFrame Frame => _doc.Frames.FirstOrDefault(f => f.Id == _selId);
        RuleElement Rule => _doc.Rules.FirstOrDefault(r => r.Id == _selId);
        GraphicElement Graphic => _doc.Graphics.FirstOrDefault(g => g.Id == _selId);

        void Select(string id)
        {
            if (_selId == id && _multi.Count <= 1) return;
            _multi.Clear();
            if (id != null) _multi.Add(id);
            SetPrimary(id);
        }

        /// <summary>Selects several elements; <paramref name="primary"/> is the one the panels show.</summary>
        void SelectMany(IEnumerable<string> ids, string primary)
        {
            _multi.Clear();
            foreach (var i in ids.Where(Exists)) _multi.Add(i);
            if (primary == null || !_multi.Contains(primary)) primary = _multi.FirstOrDefault();
            _selId = null; // force a refresh
            SetPrimary(primary);
            if (_multi.Count > 1) _status.Text = $"{_multi.Count} elements selected: drag, nudge, duplicate or delete them together.";
        }

        void OnPreviewClicked(string id)
        {
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (shift && id != null)
            {
                // Shift-click adds to or removes from the selection.
                var ids = new HashSet<string>(_multi);
                if (!ids.Remove(id)) ids.Add(id);
                SelectMany(ids, ids.Contains(id) ? id : ids.FirstOrDefault());
            }
            else if (id != null && _multi.Contains(id) && _multi.Count > 1) SetPrimary(id); // keep the group to drag it
            else if (!shift) Select(id);
        }

        void SetPrimary(string id)
        {
            _selId = id;
            _preview.SelectedId = id;
            _preview.MultiIds = _multi;
            _preview.InvalidateVisual();
            LoadEditor();
            RefreshAllPanels();
            SyncElementList();
            if (Frame == null && id != null) _tabs.SelectedItem = _posTab;
        }

        // ------------------------------------------------------------------ undo / change

        void Snapshot()
        {
            _undo.Add(SignSerializer.ToJson(_doc));
            if (_undo.Count > 200) _undo.RemoveAt(0);
            _redo.Clear();
        }

        /// <summary>Every edit goes through here: undo point, change, redraw.</summary>
        void Change(Action action, bool refreshPanels = true)
        {
            if (_loading) return;
            Snapshot();
            action();
            _doc.Normalise();
            Rebuild(refreshPanels);
        }

        void Undo() => Step(_undo, _redo);
        void Redo() => Step(_redo, _undo);

        void Step(List<string> from, List<string> to)
        {
            if (from.Count == 0) return;
            to.Add(SignSerializer.ToJson(_doc));
            var json = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            var sel = _selId;
            _doc = SignSerializer.FromJson(json);
            _selId = Exists(sel) ? sel : _doc.Frames.FirstOrDefault()?.Id;
            _multi.RemoveWhere(i => !Exists(i));
            if (_selId != null) _multi.Add(_selId);
            LoadEditor();
            Rebuild(true);
        }

        bool Exists(string id) => id != null && (_doc.Frames.Any(f => f.Id == id) || _doc.Rules.Any(r => r.Id == id) || _doc.Graphics.Any(g => g.Id == id));

        void Load(SignDocument doc, string why, bool keepUndo = false)
        {
            if (!keepUndo) { _undo.Clear(); _redo.Clear(); }
            _doc = doc.Normalise();
            _selId = _doc.Frames.FirstOrDefault()?.Id;
            _multi.Clear(); if (_selId != null) _multi.Add(_selId);
            _preview.MultiIds = _multi;
            LoadEditor();
            Rebuild(true);
            _preview.FitToView();
            _status.Text = why;
        }

        /// <summary>Lays the sign out again and redraws everything that depends on it.</summary>
        void Rebuild(bool refreshPanels)
        {
            try { _geometry = SignBuilder.Build(_doc, WpfFontProvider.Instance, 0.3); }
            catch (Exception ex) { _status.Text = "Could not lay out the sign: " + ex.Message; return; }
            _preview.SelectedId = _selId;
            _preview.Show(_doc, _geometry);
            SyncElementList();
            if (refreshPanels) RefreshAllPanels();
            var r = _geometry.PanelRect;
            _status.Text = _geometry.Warnings.Count > 0
                ? "⚠ " + string.Join("  ", _geometry.Warnings.Take(3))
                : $"{_doc.Name}: {r.Width:0} × {r.Height:0} mm, {_geometry.Pieces.Count} solids";
        }

        void RefreshAllPanels()
        {
            _loading = true;
            try
            {
                RefreshSignPanel();
                RefreshTextPanels();
                BuildPositionPanel();
            }
            finally { _loading = false; }
        }

        // ------------------------------------------------------------------ text editor

        void LoadEditor()
        {
            _loading = true;
            try
            {
                var f = Frame;
                _editor.IsEnabled = f != null;
                _editor.Text = f?.Text ?? "";
                _editorHint.Text = f != null
                    ? $"{f.Name}: type here (Enter = new paragraph). Select text, then use Character / Paragraph; with nothing selected a change applies to the whole line."
                    : "Select a text frame in the preview or the list to edit its text.";
            }
            finally { _loading = false; }
        }

        void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || Frame == null) return;
            // Typing makes one undo step per pause, not per key.
            if ((DateTime.Now - _lastTyping).TotalSeconds > 1.5) Snapshot();
            _lastTyping = DateTime.Now;
            TextEditing.SetText(Frame, _editor.Text);
            Rebuild(false);
        }

        int SelStart => _editor.SelectionStart;
        int SelLength => _editor.SelectionLength;

        void ApplyChar(Action<CharacterFormat> change)
        {
            var f = Frame;
            if (f == null || _loading) return;
            Change(() => TextEditing.ApplyCharacter(f, SelStart, SelLength, change));
        }

        void ApplyPara(Action<ParagraphFormat> change)
        {
            var f = Frame;
            if (f == null || _loading) return;
            Change(() => TextEditing.ApplyParagraph(f, SelStart, SelLength, change));
        }

        // ------------------------------------------------------------------ keys

        void OnWindowKey(object sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.S) { SaveFile(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D) { DuplicateSelected(); e.Handled = true; }
            else if (ctrl && e.Key == Key.B) { ApplyChar(c => c.Weight = c.Weight >= 600 ? 400 : 700); e.Handled = true; }
            else if (ctrl && e.Key == Key.I) { ApplyChar(c => c.Italic = !c.Italic); e.Handled = true; }
            else if (ctrl && e.Key == Key.U) { ApplyChar(c => c.Underline = !c.Underline); e.Handled = true; }
        }

        void OnPreviewKey(object sender, KeyEventArgs e)
        {
            if (_selId == null) return;
            if (e.Key == Key.Escape) { Select(null); e.Handled = true; return; }
            if (Movable().Count == 0)
            {
                if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Delete)
                {
                    _status.Text = "This element is locked. Unlock it in the Layers list to move or delete it.";
                    e.Handled = true;
                }
                return;
            }
            double step = _settings.SnapToGrid ? _preview.GridMm * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1)
                        : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            if (dx != 0 || dy != 0) { Change(() => { foreach (var id in Movable()) MoveBy(id, dx, dy); }); e.Handled = true; }
            else if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        }

        // ------------------------------------------------------------------ moving

        void MoveBy(string id, double dx, double dy)
        {
            if (_doc.Frames.FirstOrDefault(x => x.Id == id) is TextFrame f) { f.X += dx; f.Y += dy; }
            else if (_doc.Rules.FirstOrDefault(x => x.Id == id) is RuleElement r) { r.X += dx; r.Y += dy; }
            else if (_doc.Graphics.FirstOrDefault(x => x.Id == id) is GraphicElement g) { g.X += dx; g.Y += dy; }
        }

        (double X, double Y)? PositionOf(string id)
        {
            if (_doc.Frames.FirstOrDefault(x => x.Id == id) is TextFrame f) return (f.X, f.Y);
            if (_doc.Rules.FirstOrDefault(x => x.Id == id) is RuleElement r) return (r.X, r.Y);
            if (_doc.Graphics.FirstOrDefault(x => x.Id == id) is GraphicElement g) return (g.X, g.Y);
            return null;
        }

        /// <summary>The selected elements that are not locked.</summary>
        List<string> Movable() => _multi.Where(i => !_doc.IsLocked(i)).ToList();

        void StartDrag(string id)
        {
            Snapshot();
            // Dragging one of several selected elements moves them all.
            var group = _multi.Contains(id) ? Movable() : new List<string> { id };
            if (!group.Contains(id)) group.Add(id);
            // Alt-drag: leave the originals where they are and drag copies.
            if (_preview.DragIsCopy)
            {
                var copies = new Dictionary<string, string>();
                foreach (var g in group) { var c = CopyOf(g, 0, 0); if (c != null) copies[g] = c; }
                if (copies.TryGetValue(id, out var lead))
                {
                    _doc.Normalise();
                    group = copies.Values.ToList();
                    id = lead;
                    SelectMany(group, lead);
                    _preview.RetargetDrag(lead);
                    Rebuild(true);
                }
            }
            _dragOrigin = new Dictionary<string, (double, double)>();
            foreach (var g in group) { var p = PositionOf(g); if (p != null) _dragOrigin[g] = p.Value; }
        }

        void DragTo(string id, double dx, double dy)
        {
            if (_doc.IsLocked(id)) return;
            if (_dragOrigin == null || !_dragOrigin.TryGetValue(id, out var o)) return;
            var now = PositionOf(id);
            if (now == null) return;
            double x = o.X + dx, y = o.Y + dy;
            if (_settings.SnapToGrid)
            {
                // The element's own position (frame: left / top of capitals; rule: its start; graphic: its anchor)
                // lands on a gridline. An axis held still with Shift is left where it was.
                double g = _preview.GridMm > 0 ? _preview.GridMm : 10;
                if (dx != 0) x = Math.Round(x / g) * g;
                if (dy != 0) y = Math.Round(y / g) * g;
            }
            // The dragged element sets the move (and the snap); the rest of the group follows by the same amount.
            double mx = x - o.X, my = y - o.Y;
            foreach (var kv in _dragOrigin)
            {
                var cur = PositionOf(kv.Key);
                if (cur != null) MoveBy(kv.Key, kv.Value.X + mx - cur.Value.X, kv.Value.Y + my - cur.Value.Y);
            }
            Rebuild(false);
            _loading = true;
            try { BuildPositionPanel(); } finally { _loading = false; }
        }

        // ------------------------------------------------------------------ elements

        void AddFrame()
        {
            var fmt = Frame?.DefaultFormat?.Clone() ?? new CharacterFormat { FontFamily = Presets.DefaultFont, SizeMm = 50 };
            var r = _geometry?.PanelRect ?? new Core.Geometry.Rect2(0, 0, 1000, 400);
            var f = TextFrame.Create("Text " + (_doc.Frames.Count + 1), "NEW TEXT", fmt, new ParagraphFormat(), Math.Round(r.MinX + 50), Math.Round(r.Center.Y), 0);
            Change(() => _doc.Frames.Add(f));
            Select(f.Id);
            _editor.Focus();
            _editor.SelectAll();
        }

        void AddRule()
        {
            var r = _geometry?.PanelRect ?? new Core.Geometry.Rect2(0, 0, 1000, 400);
            var rule = new RuleElement { Name = "Rule " + (_doc.Rules.Count + 1), X = Math.Round(r.MinX + 50), Y = Math.Round(r.MaxY - 50), Length = Math.Round(Math.Max(100, r.Width - 100)), WeightMm = 4 };
            Change(() => _doc.Rules.Add(rule));
            Select(rule.Id);
        }

        void DuplicateSelected()
        {
            if (_selId == null) return;
            var ids = _multi.Count > 0 ? _multi.ToList() : new List<string> { _selId };
            double off = ids.Count == 1 && Frame != null ? 60 : 40;
            var made = new Dictionary<string, string>();
            Change(() => { foreach (var i in ids) { var c = CopyOf(i, ids.Count == 1 && Graphic == null ? 0 : off, off); if (c != null) made[i] = c; } });
            if (made.Count > 0) SelectMany(made.Values, made.TryGetValue(_selId, out var p) ? p : made.Values.First());
        }

        /// <summary>Adds a copy of an element, moved by (dx, dy) mm, and returns its id. No undo point.</summary>
        string CopyOf(string id, double dx, double dy)
        {
            if (_doc.Frames.FirstOrDefault(x => x.Id == id) is TextFrame f)
            {
                var copy = SignSerializer.FromJson(SignSerializer.ToJson(new SignDocument { Frames = { f } })).Frames[0];
                copy.Id = SignDocument.NewId(); copy.Name += " copy"; copy.X += dx; copy.Y += dy; copy.Locked = false;
                _doc.Frames.Insert(_doc.Frames.IndexOf(f) + 1, copy);
                return copy.Id;
            }
            if (_doc.Rules.FirstOrDefault(x => x.Id == id) is RuleElement r)
            {
                var copy = r.Clone(); copy.Id = SignDocument.NewId(); copy.Name += " copy"; copy.X += dx; copy.Y += dy; copy.Locked = false;
                _doc.Rules.Insert(_doc.Rules.IndexOf(r) + 1, copy);
                return copy.Id;
            }
            if (_doc.Graphics.FirstOrDefault(x => x.Id == id) is GraphicElement g)
            {
                var copy = SignSerializer.FromJson(SignSerializer.ToJson(new SignDocument { Graphics = { g } })).Graphics[0];
                copy.Id = SignDocument.NewId(); copy.Name += " copy"; copy.X += dx; copy.Y += dy; copy.Locked = false;
                _doc.Graphics.Insert(_doc.Graphics.IndexOf(g) + 1, copy);
                return copy.Id;
            }
            return null;
        }

        void DeleteSelected()
        {
            if (_selId == null) return;
            var ids = new HashSet<string>(Movable());
            if (ids.Count == 0) { _status.Text = "This element is locked. Unlock it in the Layers list to delete it."; return; }
            Change(() =>
            {
                _doc.Frames.RemoveAll(f => ids.Contains(f.Id));
                _doc.Rules.RemoveAll(r => ids.Contains(r.Id));
                _doc.Graphics.RemoveAll(g => ids.Contains(g.Id));
            });
            Select(_doc.Frames.FirstOrDefault()?.Id);
        }

        void SyncElementList()
        {
            if (_elements == null) return;
            var wanted = _doc.Frames.Select(f => (f.Id, "T   " + f.Name + "  –  " + Short(f.Text), f.Locked))
                .Concat(_doc.Rules.Select(r => (r.Id, "—   " + r.Name, r.Locked)))
                .Concat(_doc.Graphics.Select(g => (g.Id, "◆   " + g.Name, g.Locked))).ToList();
            var keys = wanted.Select(w => w.Item1 + "|" + w.Item2 + "|" + w.Item3).ToList();
            bool same = _elementKeys.SequenceEqual(keys);
            var wasLoading = _loading;
            _loading = true;
            try
            {
                if (!same)
                {
                    _elements.Items.Clear();
                    foreach (var (id, label, locked) in wanted) _elements.Items.Add(ElementRow(id, label, locked));
                    _elementKeys = keys;
                }
                _elements.SelectedItems.Clear();
                foreach (var item in _elements.Items.Cast<ListBoxItem>().Where(i => _multi.Contains((string)i.Tag) || (string)i.Tag == _selId))
                    _elements.SelectedItems.Add(item);
            }
            finally { _loading = wasLoading; }
        }

        List<string> _elementKeys = new List<string>();

        /// <summary>One row of the Elements list: a lock button and the element's name.</summary>
        ListBoxItem ElementRow(string id, string label, bool locked)
        {
            var lockBtn = new Button
            {
                Content = locked ? "🔒" : "🔓", Width = 24, Height = 20, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0),
                FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 11,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Opacity = locked ? 1 : 0.35, Focusable = false,
                ToolTip = locked ? "Locked: can't be picked or moved in the preview. Click to unlock." : "Click to lock, so it can't be picked or moved in the preview",
            };
            lockBtn.Click += (s, e) => { Change(() => _doc.SetLocked(id, !locked)); e.Handled = true; };
            var row = new DockPanel();
            DockPanel.SetDock(lockBtn, Dock.Left);
            row.Children.Add(lockBtn);
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = locked ? Ui.Muted : Brushes.Black });
            return new ListBoxItem { Tag = id, Content = row, Padding = new Thickness(2, 1, 2, 1) };
        }

        static string Short(string s)
        {
            s = (s ?? "").Replace('\n', ' ');
            return s.Length > 24 ? s.Substring(0, 24) + "…" : s;
        }

        // ------------------------------------------------------------------ files

        void OpenFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "BG sign (*.bgsign)|*.bgsign|All files|*.*", InitialDirectory = _settings.LastFolder ?? _settings.Folder };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                Snapshot();
                Load(SignSerializer.Load(dlg.FileName), "Opened " + dlg.FileName, keepUndo: true);
                _settings.LastFolder = Path.GetDirectoryName(dlg.FileName); _settings.Save();
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not open that file: " + ex.Message, "Open"); }
        }

        void SaveFile()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "BG sign (*.bgsign)|*.bgsign", FileName = SignExporter.SafeName(_doc.Name) + ".bgsign",
                InitialDirectory = _settings.LastFolder ?? _settings.Folder,
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                SignSerializer.Save(_doc, dlg.FileName);
                _settings.LastFolder = Path.GetDirectoryName(dlg.FileName); _settings.Save();
                _status.Text = "Saved " + dlg.FileName;
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not save: " + ex.Message, "Save"); }
        }

        void ChooseExportFolder()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Folder for the sign families (.rfa)", SelectedPath = _settings.Folder })
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _settings.ExportFolder = dlg.SelectedPath; _settings.Save();
                    _status.Text = "Families are saved in " + dlg.SelectedPath;
                }
        }

        void ImportGraphic()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Drawings and artwork (*.dwg;*.dxf;*.ai;*.pdf)|*.dwg;*.dxf;*.ai;*.pdf|All files|*.*",
                InitialDirectory = _settings.LastFolder ?? _settings.Folder,
            };
            if (dlg.ShowDialog(this) != true) return;
            string path = dlg.FileName;
            _settings.LastFolder = Path.GetDirectoryName(path); _settings.Save();
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".dwg" || ext == ".dxf")
            {
                _status.Text = "Reading " + Path.GetFileName(path) + " with Revit…";
                RevitBridge.Instance.Run(app =>
                {
                    var g = DwgReader.Read(app.Application, path, out string note);
                    Dispatcher.BeginInvoke(new Action(() => AddGraphic(g, note)));
                });
                return;
            }
            try
            {
                var paths = PdfVectorReader.Read(path);
                if (paths.Count == 0) throw new InvalidDataException("No vector shapes were found. Convert text to outlines and embed linked artwork in Illustrator first.");
                var g = GraphicElement.FromPaths(Path.GetFileNameWithoutExtension(path), Path.GetFileName(path), paths.Select(p => (p.Points, p.Closed)));
                var strokes = paths.Where(p => p.StrokeMm > 0).Select(p => p.StrokeMm).OrderBy(x => x).ToList();
                g.StrokeMm = strokes.Count > 0 ? Math.Round(strokes[strokes.Count / 2], 2) : 3;
                AddGraphic(g, null);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import graphic"); }
        }

        void AddGraphic(GraphicElement g, string note)
        {
            var r = _geometry?.PanelRect ?? new Core.Geometry.Rect2(0, 0, 1000, 400);
            // Very large or tiny drawings are scaled to a sensible size; the scale stays editable.
            if (g.NativeWidth > r.Width * 1.5 || g.NativeWidth < 5)
                g.ScalePercent = Math.Round(r.Width * 0.3 / Math.Max(g.NativeWidth, 0.001) * 100, 2);
            g.X = Math.Round(r.MinX + _doc.Panel.PaddingX);
            g.Y = Math.Round(r.MinY + _doc.Panel.PaddingY);
            Change(() => _doc.Graphics.Add(g));
            Select(g.Id);
            _status.Text = $"Imported {g.Name}: {g.Paths.Count(p => p.Closed)} closed shapes, {g.Paths.Count(p => !p.Closed)} lines." + (note != null ? " " + note : "");
        }

        // ------------------------------------------------------------------ export

        // ------------------------------------------------------------------ open from project / templates

        sealed class SourceItem
        {
            public string Label; public SignDocument Sign; public string Template;
            public override string ToString() => Label;
        }

        bool _refreshingSource;

        /// <summary>The top-left list: signs loaded in this project, then the templates.</summary>
        void RefreshSourceList()
        {
            if (_source == null) return;
            _refreshingSource = true;
            try
            {
                _source.Items.Clear();
                _source.Items.Add(new ComboBoxItem { Content = "Open a sign or template…", IsEnabled = false });
                ComboBoxItem Header(string text) => new ComboBoxItem
                {
                    Content = text, IsEnabled = false, FontWeight = FontWeights.SemiBold, FontSize = 10.5, Foreground = Ui.Header,
                    Margin = new Thickness(0, 4, 0, 0),
                };
                _source.Items.Add(Header("IN THIS PROJECT"));
                if (_projectSigns.Count == 0) _source.Items.Add(new ComboBoxItem { Content = "   (none yet: export a sign)", IsEnabled = false, Foreground = Ui.Muted });
                foreach (var s in _projectSigns) _source.Items.Add(new ComboBoxItem { Content = "   " + s.Name, Tag = new SourceItem { Label = s.Name, Sign = s } });
                _source.Items.Add(Header("TEMPLATES"));
                foreach (var t in Presets.Names) _source.Items.Add(new ComboBoxItem { Content = "   " + t, Tag = new SourceItem { Label = t, Template = t } });
                _source.SelectedIndex = 0;
            }
            finally { _refreshingSource = false; }
        }

        void OnSourcePicked()
        {
            if (_refreshingSource || !(_source.SelectedItem is ComboBoxItem item) || !(item.Tag is SourceItem pick)) return;
            Snapshot();
            if (pick.Sign != null)
            {
                Load(SignSerializer.Clone(pick.Sign), $"Editing \"{pick.Sign.Name}\" from the project. Export updates it in place (Undo goes back).", keepUndo: true);
                SetSource(pick.Sign.Name);
            }
            else
            {
                Load(Presets.Create(pick.Template), $"New sign from the template \"{pick.Template}\" (Undo goes back).", keepUndo: true);
                SetSource(null);
            }
            _refreshingSource = true;
            try { _source.SelectedIndex = 0; } finally { _refreshingSource = false; }
        }

        /// <summary>Remembers which project family this design belongs to and labels the export button.</summary>
        void SetSource(string familyName)
        {
            _sourceFamily = familyName;
            if (_exportButton == null) return;
            _exportButton.Content = familyName != null ? "Update in Revit" : "Export to Revit";
            _exportButton.ToolTip = familyName != null
                ? $"Replace \"{familyName}\" in the project: every placed copy updates. Change the family name to export a new sign instead."
                : "Build the parametric sign family, load it into the project and place it";
        }

        void Export()
        {
            if (RevitBridge.Instance == null) { MessageBox.Show(this, "Restart the designer from the ribbon.", "Export"); return; }
            var sign = SignSerializer.Clone(_doc);
            string folder = _settings.Folder;
            // Working on a sign from the project: replace that family (every placed copy updates), no new placement.
            bool updating = _sourceFamily != null && string.Equals(SignExporter.SafeName(sign.Name), SignExporter.SafeName(_sourceFamily), StringComparison.OrdinalIgnoreCase);
            bool place = !updating && _placeAfter.IsChecked == true;
            _status.Text = updating ? $"Updating \"{sign.Name}\" in Revit…" : "Building the family in Revit…";
            RevitBridge.Instance.Run(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (!updating && doc != null && !doc.IsFamilyDocument && SignExporter.FamilyExists(doc, sign.Name))
                {
                    // A different sign with this name is already in the project: don't overwrite it by accident.
                    string free = sign.Name;
                    for (int n = 2; SignExporter.FamilyExists(doc, free); n++) free = $"{sign.Name} {n}";
                    var td = new TaskDialog("BG Signage")
                    {
                        MainInstruction = $"\"{sign.Name}\" is already in this project",
                        MainContent = "Exporting with the same name replaces that sign everywhere it is placed.",
                        CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Cancel,
                    };
                    td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink1, $"Save as a new sign, \"{free}\"");
                    td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink2, $"Replace \"{sign.Name}\"", "Every placed copy changes to this design.");
                    var answer = td.Show();
                    if (answer == Autodesk.Revit.UI.TaskDialogResult.CommandLink1)
                    {
                        sign.Name = free;
                        _doc.Name = free;
                        _loading = true; try { RefreshSignPanel(); } finally { _loading = false; }
                    }
                    else if (answer == Autodesk.Revit.UI.TaskDialogResult.CommandLink2) place = false;
                    else { _status.Text = "Export cancelled."; return; }
                }
                string msg;
                try { msg = SignExporter.Export(app, sign, folder, place); }
                catch (Exception ex) { msg = "Export failed: " + ex.Message; }
                if (!msg.StartsWith("Export failed"))
                {
                    SetSource(sign.Name);
                    try { _projectSigns = SignExporter.ProjectSigns(doc); RefreshSourceList(); } catch { }
                    if (updating) msg = $"\"{sign.Name}\" is updated: every placed copy shows the new design.\n" + string.Join("\n", msg.Split('\n').Skip(1));
                }
                bool failed = msg.StartsWith("Export failed");
                _status.Text = failed ? msg.Split('\n')[0] : msg.Split('\n')[0] + " " + SignExporter.PlacementHint(sign.Build.Hosting);
                if (place && !failed)
                {
                    // Get out of the way so the clicks go to Revit's view: the designer sits on top of Revit.
                    // Sign Designer on the ribbon brings it back.
                    WindowState = WindowState.Minimized;
                    try { SetForegroundWindow(app.MainWindowHandle); } catch { }
                }
                else TaskDialog.Show("BG Signage", msg);
            });
        }
    }
}

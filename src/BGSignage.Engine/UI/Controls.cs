using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BGSignage.Engine.UI
{
    /// <summary>Colours and small builders for the designer's compact, InDesign-like panels.</summary>
    internal static class Ui
    {
        public static readonly Brush PanelBack = Frozen(Color.FromRgb(243, 243, 243));
        public static readonly Brush Header = Frozen(Color.FromRgb(70, 70, 70));
        public static readonly Brush Accent = Frozen(Color.FromRgb(0, 110, 200));
        public static readonly Brush Muted = Frozen(Color.FromRgb(120, 120, 120));
        public static readonly Brush Border = Frozen(Color.FromRgb(210, 210, 210));

        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static TextBlock Title(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(), FontWeight = FontWeights.SemiBold, FontSize = 10.5, Foreground = Header,
            Margin = new Thickness(0, 10, 0, 4),
        };

        public static TextBlock Note(string text) => new TextBlock
        {
            Text = text, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4),
        };

        /// <summary>Two controls side by side, like the paired fields in InDesign's panels.</summary>
        public static Grid Pair(UIElement left, UIElement right)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            if (left != null) { Grid.SetColumn(left, 0); g.Children.Add(left); }
            if (right != null) { Grid.SetColumn(right, 2); g.Children.Add(right); }
            return g;
        }

        public static FrameworkElement Labelled(string label, FrameworkElement control, string tip = null)
        {
            var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = tip };
            var l = new TextBlock { Text = label, Width = 86, VerticalAlignment = VerticalAlignment.Center, Foreground = Header };
            DockPanel.SetDock(l, Dock.Left);
            d.Children.Add(l);
            d.Children.Add(control);
            return d;
        }

        public static ToggleButton Toggle(string glyph, string tip, double fontSize = 13, FontStyle? style = null, TextDecorationCollection deco = null)
        {
            var tb = new TextBlock { Text = glyph, FontSize = fontSize, FontFamily = new FontFamily("Segoe UI"), TextDecorations = deco };
            if (style.HasValue) tb.FontStyle = style.Value;
            return new ToggleButton { Content = tb, ToolTip = tip, Width = 32, Height = 26, Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(0) };
        }

        public static Button Button(string text, string tip, RoutedEventHandler click, bool primary = false)
        {
            var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 4, 0), MinHeight = 26 };
            if (primary) { b.Background = Accent; b.Foreground = Brushes.White; b.FontWeight = FontWeights.SemiBold; }
            b.Click += click;
            return b;
        }

        public static ComboBox Combo(params string[] items)
        {
            var c = new ComboBox { MinHeight = 24 };
            foreach (var i in items) c.Items.Add(i);
            return c;
        }
    }

    /// <summary>
    /// A number field: type a value and press Enter (or Tab away), use the up/down arrow keys or the mouse wheel to
    /// step (Shift = 10x). Blank means the selection has mixed values. Optionally shows "Auto" for 0.
    /// </summary>
    internal sealed class NumberField : DockPanel
    {
        readonly TextBox _box;
        readonly double _step, _min, _max;
        readonly string _format;
        double? _value;
        bool _setting;

        public string AutoText { get; set; }
        public event Action<double> Committed;

        public NumberField(string label, string unit, double step = 1, double min = -100000, double max = 100000, int decimals = 1, string tip = null, double labelWidth = 0)
        {
            _step = step; _min = min; _max = max;
            _format = decimals <= 0 ? "0" : "0." + new string('#', decimals);
            Margin = new Thickness(0, 2, 0, 2);
            ToolTip = tip;
            if (!string.IsNullOrEmpty(label))
            {
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Header, Margin = new Thickness(0, 0, 6, 0) };
                if (labelWidth > 0) l.Width = labelWidth;
                SetDock(l, Dock.Left);
                Children.Add(l);
            }
            if (!string.IsNullOrEmpty(unit))
            {
                var u = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted, Margin = new Thickness(4, 0, 0, 0), MinWidth = 18 };
                SetDock(u, Dock.Right);
                Children.Add(u);
            }
            _box = new TextBox { MinWidth = 44, VerticalContentAlignment = VerticalAlignment.Center, Height = 24, HorizontalContentAlignment = HorizontalAlignment.Right };
            _box.KeyDown += OnKey;
            _box.PreviewKeyDown += OnPreviewKey;
            _box.LostKeyboardFocus += (s, e) => Commit();
            _box.MouseWheel += (s, e) => { if (_box.IsKeyboardFocusWithin) { StepBy(e.Delta > 0 ? 1 : -1); e.Handled = true; } };
            _box.GotKeyboardFocus += (s, e) => _box.SelectAll();
            Children.Add(_box);
        }

        public double? Value
        {
            get => _value;
            set
            {
                _value = value;
                _setting = true;
                _box.Text = value == null ? "" : AutoText != null && Math.Abs(value.Value) < 1e-9 ? AutoText : value.Value.ToString(_format, CultureInfo.CurrentCulture);
                _setting = false;
            }
        }

        void OnPreviewKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down) { StepBy(e.Key == Key.Up ? 1 : -1); e.Handled = true; }
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Commit(); _box.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Value = _value; e.Handled = true; }
        }

        void StepBy(int dir)
        {
            double step = _step * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1);
            double v = Parse() ?? _value ?? 0;
            Set(Math.Round((v + dir * step) / _step) * _step);
        }

        double? Parse()
        {
            var t = (_box.Text ?? "").Trim();
            if (AutoText != null && string.Equals(t, AutoText, StringComparison.OrdinalIgnoreCase)) return 0;
            t = t.Replace("mm", "").Replace("%", "").Replace("°", "").Trim();
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out double v)) return v;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }

        void Commit()
        {
            if (_setting) return;
            var v = Parse();
            if (v == null) { Value = _value; return; }
            if (_value.HasValue && Math.Abs(v.Value - _value.Value) < 1e-9) return;
            Set(v.Value);
        }

        void Set(double v)
        {
            v = Math.Max(_min, Math.Min(_max, v));
            Value = v;
            Committed?.Invoke(v);
        }
    }
}

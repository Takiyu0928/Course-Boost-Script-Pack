using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimeSaver.Ui
{
    /// <summary>
    /// Visual language: warm white background, dark ink text, a single 1px line colour and only two
    /// accent colours (yellow for the primary action, red for destructive/attention). No rounded
    /// corners, no shadows, no gradients, no web fonts, no images - the UI must stay readable
    /// offline and must not pretend to be a dashboard.
    /// </summary>
    public static class Theme
    {
        public static readonly Brush Background = Frozen("#FBF8F1");
        public static readonly Brush Surface = Frozen("#F4EFE3");
        public static readonly Brush SurfaceAlt = Frozen("#EFE9DA");
        public static readonly Brush Ink = Frozen("#1E1B16");
        public static readonly Brush Muted = Frozen("#6B6459");
        public static readonly Brush Line = Frozen("#D9D2C3");
        public static readonly Brush AccentYellow = Frozen("#E8B33D");
        public static readonly Brush AccentRed = Frozen("#B5432C");
        public static readonly Brush OkInk = Frozen("#2F6B4F");

        public static readonly FontFamily UiFont = new FontFamily("Microsoft YaHei UI, Segoe UI, Tahoma, sans-serif");

        public const double FontTitle = 20;
        public const double FontSection = 13;
        public const double FontBody = 13;
        public const double FontSmall = 12;

        private static Brush Frozen(string color)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        public static TextBlock Label(string text, double size, Brush brush)
        {
            TextBlock block = new TextBlock();
            block.Text = text;
            block.FontFamily = UiFont;
            block.FontSize = size;
            block.Foreground = brush;
            block.TextWrapping = TextWrapping.Wrap;
            return block;
        }

        public static TextBlock Title(string text) { return Label(text, FontTitle, Ink); }

        public static TextBlock Body(string text) { return Label(text, FontBody, Ink); }

        public static TextBlock Muted13(string text) { return Label(text, FontSmall, Muted); }

        public static Border Section(string title, UIElement content)
        {
            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Vertical;
            TextBlock header = Label(title, FontSection, Ink);
            header.FontWeight = FontWeights.SemiBold;
            header.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(header);
            if (content != null) panel.Children.Add(content);

            Border border = new Border();
            border.Background = Brushes.Transparent;
            border.BorderBrush = Line;
            border.BorderThickness = new Thickness(0, 1, 0, 0);
            border.Padding = new Thickness(0, 14, 0, 14);
            border.Child = panel;
            return border;
        }

        public static Border Card(UIElement content)
        {
            Border border = new Border();
            border.Background = Surface;
            border.BorderBrush = Line;
            border.BorderThickness = new Thickness(1);
            border.Padding = new Thickness(12);
            border.Child = content;
            return border;
        }

        public static Button Button(string text, bool primary, bool danger)
        {
            Button button = new Button();
            button.Content = text;
            button.FontFamily = UiFont;
            button.FontSize = FontBody;
            button.Padding = new Thickness(14, 6, 14, 6);
            button.Margin = new Thickness(0, 0, 8, 0);
            button.MinWidth = 84;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.BorderThickness = new Thickness(1);
            button.Cursor = System.Windows.Input.Cursors.Hand;

            if (danger)
            {
                button.Background = Brushes.Transparent;
                button.Foreground = AccentRed;
                button.BorderBrush = AccentRed;
            }
            else if (primary)
            {
                button.Background = AccentYellow;
                button.Foreground = Ink;
                button.BorderBrush = Ink;
                button.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                button.Background = Brushes.Transparent;
                button.Foreground = Ink;
                button.BorderBrush = Line;
            }
            return button;
        }

        public static TextBox TextField(string value, bool multiline)
        {
            TextBox box = new TextBox();
            box.Text = value ?? string.Empty;
            box.FontFamily = UiFont;
            box.FontSize = FontBody;
            box.Foreground = Ink;
            box.Background = Brushes.White;
            box.BorderBrush = Line;
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(6, 4, 6, 4);
            box.Margin = new Thickness(0, 2, 0, 0);
            box.AcceptsReturn = multiline;
            box.TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (multiline) { box.MinHeight = 60; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            return box;
        }

        public static PasswordBox PasswordField(string value)
        {
            PasswordBox box = new PasswordBox();
            box.Password = value ?? string.Empty;
            box.FontFamily = UiFont;
            box.FontSize = FontBody;
            box.Background = Brushes.White;
            box.BorderBrush = Line;
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(6, 4, 6, 4);
            box.Margin = new Thickness(0, 2, 0, 0);
            return box;
        }

        public static CheckBox Check(string text, bool value)
        {
            CheckBox box = new CheckBox();
            box.Content = text;
            box.IsChecked = value;
            box.FontFamily = UiFont;
            box.FontSize = FontBody;
            box.Foreground = Ink;
            box.Margin = new Thickness(0, 4, 0, 4);
            return box;
        }

        public static ComboBox Combo(string[] items, string selected)
        {
            ComboBox combo = new ComboBox();
            combo.FontFamily = UiFont;
            combo.FontSize = FontBody;
            combo.Background = Brushes.White;
            combo.BorderBrush = Line;
            combo.BorderThickness = new Thickness(1);
            combo.Padding = new Thickness(6, 3, 6, 3);
            combo.Margin = new Thickness(0, 2, 0, 0);
            if (items != null) foreach (string item in items) combo.Items.Add(item);
            if (selected != null) combo.Text = selected;
            combo.IsEditable = true;
            return combo;
        }

        public static StackPanel Field(string label, UIElement input)
        {
            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Vertical;
            panel.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(Label(label, FontSmall, Muted));
            panel.Children.Add(input);
            return panel;
        }

        public static StackPanel Row(params UIElement[] elements)
        {
            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.Margin = new Thickness(0, 0, 0, 6);
            if (elements != null) foreach (UIElement element in elements) panel.Children.Add(element);
            return panel;
        }

        public static StackPanel Column(params UIElement[] elements)
        {
            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Vertical;
            if (elements != null) foreach (UIElement element in elements) panel.Children.Add(element);
            return panel;
        }

        public static Border Tag(string text, Brush foreground, Brush border)
        {
            Border tag = new Border();
            tag.BorderBrush = border;
            tag.BorderThickness = new Thickness(1);
            tag.Padding = new Thickness(6, 1, 6, 1);
            tag.Margin = new Thickness(0, 0, 6, 0);
            tag.VerticalAlignment = VerticalAlignment.Center;
            TextBlock label = Label(text, FontSmall, foreground);
            tag.Child = label;
            return tag;
        }
    }
}

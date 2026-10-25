using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TimeSaver.App;
using TimeSaver.Core;
using TimeSaver.Ui;

namespace TimeSaver.Tests
{
    /// <summary>
    /// Off-screen layout helper for the UI tests.
    ///
    /// Nothing here calls Window.Show(): the content element is measured and arranged directly, so
    /// no top-level window, taskbar entry, tray icon or focus change is created. The user asked that
    /// UI work stay non-interactive; human checks are listed in IMPLEMENTATION-REPORT.md instead.
    /// </summary>
    internal static class OffscreenUi
    {
        public const double Width = 1200;
        public const double Height = 820;

        public static FrameworkElement Layout(MainWindow window)
        {
            FrameworkElement content = window.Content as FrameworkElement;
            if (content == null) throw new InvalidOperationException("窗口没有内容元素");
            Relayout(content);
            return content;
        }

        public static void Relayout(FrameworkElement content)
        {
            content.InvalidateMeasure();
            content.Measure(new Size(Width, Height));
            content.Arrange(new Rect(0, 0, Width, Height));
            content.UpdateLayout();
        }

        public static void SelectTab(TabControl tabs, string header)
        {
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                TabItem item = tabs.Items[i] as TabItem;
                if (item != null && string.Equals(Convert.ToString(item.Header), header, StringComparison.Ordinal))
                {
                    tabs.SelectedIndex = i;
                    return;
                }
            }
        }

        public static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            List<T> found = new List<T>();
            if (root == null) return found;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                T typed = child as T;
                if (typed != null) found.Add(typed);
                found.AddRange(FindAll<T>(child));
            }
            return found;
        }

        public static T FindFirst<T>(DependencyObject root) where T : DependencyObject
        {
            List<T> all = FindAll<T>(root);
            return all.Count > 0 ? all[0] : null;
        }

        public static ComboBox FindCombo(DependencyObject root)
        {
            return FindFirst<ComboBox>(root);
        }

        public static Button FindButton(DependencyObject root, string text)
        {
            List<Button> buttons = FindAll<Button>(root);
            for (int i = 0; i < buttons.Count; i++)
                if (string.Equals(Convert.ToString(buttons[i].Content), text, StringComparison.Ordinal)) return buttons[i];
            return null;
        }

        public static void Click(Button button)
        {
            if (button != null) button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        /// <summary>
        /// Reproduces exactly what the ComboBox template does after it is applied: it pushes the
        /// current text into the inner editable TextBox, which bubbles one TextChanged whose value
        /// equals the text we programmed. Raising the routed event directly keeps the value intact
        /// so the test observes the handler, not a side effect of mutation.
        /// </summary>
        public static void RaiseBackfill(FrameworkElement combo)
        {
            combo.RaiseEvent(new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
        }
    }
}

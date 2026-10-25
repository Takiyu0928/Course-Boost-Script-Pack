using System;
using System.IO;
using System.Text;
using TimeSaver.Config;

namespace TimeSaver.Ui
{
    /// <summary>
    /// Window geometry and close behaviour, stored in launcher\state.json.
    /// Kept separate from the app's config so a corrupt state file can never stop the app: every
    /// failure falls back to defaults.
    /// </summary>
    public sealed class WindowStateStore
    {
        public double Left = double.NaN;
        public double Top = double.NaN;
        public double Width = 1100;
        public double Height = 720;
        public bool Maximized;
        /// <summary>true = closing the window hides it to the tray instead of exiting.</summary>
        public bool CloseToTray = true;
        public bool AskedClosePreference;

        public static WindowStateStore Load(string path)
        {
            WindowStateStore state = new WindowStateStore();
            try
            {
                if (!File.Exists(path)) return state;
                JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false)));
                JsonValue root = document.Root;
                if (root == null) return state;

                double number;
                JsonValue value = root.Get("left");
                if (value != null && value.TryGetDouble(out number)) state.Left = number;
                value = root.Get("top");
                if (value != null && value.TryGetDouble(out number)) state.Top = number;
                value = root.Get("width");
                if (value != null && value.TryGetDouble(out number) && number >= 640) state.Width = number;
                value = root.Get("height");
                if (value != null && value.TryGetDouble(out number) && number >= 480) state.Height = number;

                bool flag;
                value = root.Get("maximized");
                if (value != null && value.TryGetBool(out flag)) state.Maximized = flag;
                value = root.Get("closeToTray");
                if (value != null && value.TryGetBool(out flag)) state.CloseToTray = flag;
                value = root.Get("askedClosePreference");
                if (value != null && value.TryGetBool(out flag)) state.AskedClosePreference = flag;
                return state;
            }
            catch
            {
                return new WindowStateStore();
            }
        }

        public void Save(string path)
        {
            try
            {
                JsonValue root = JsonValue.NewObject();
                root.Set("left", NumberOrNull(Left));
                root.Set("top", NumberOrNull(Top));
                root.Set("width", NumberOrNull(Width));
                root.Set("height", NumberOrNull(Height));
                root.Set("maximized", JsonValue.NewBool(Maximized));
                root.Set("closeToTray", JsonValue.NewBool(CloseToTray));
                root.Set("askedClosePreference", JsonValue.NewBool(AskedClosePreference));

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                string temp = path + ".tmp";
                File.WriteAllText(temp, "{\r\n" + RenderManually(root) + "\r\n}\r\n", new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null, true);
                else File.Move(temp, path);
            }
            catch
            {
                // geometry is a convenience; never fail the app over it
            }
        }

        private static JsonValue NumberOrNull(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return JsonValue.NewNull();
            return JsonValue.NewNumber(value);
        }

        private static string RenderManually(JsonValue root)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < root.Members.Count; i++)
            {
                if (i > 0) builder.Append(",\r\n");
                builder.Append("  \"").Append(root.Members[i].Key).Append("\": ");
                JsonValue value = root.Members[i].Value;
                if (value.Kind == JsonKind.Number)
                {
                    double number;
                    if (value.TryGetDouble(out number) && double.IsNaN(number)) builder.Append("null");
                    else builder.Append(value.Text);
                }
                else if (value.Kind == JsonKind.Bool) builder.Append(value.Bool ? "true" : "false");
                else if (value.Kind == JsonKind.Null) builder.Append("null");
                else builder.Append("\"").Append(value.Text).Append("\"");
            }
            return builder.ToString();
        }
    }
}

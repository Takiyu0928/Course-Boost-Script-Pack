using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TimeSaver.Config;
using TimeSaver.Core;

namespace TimeSaver.Model
{
    /// <summary>How the UI wants a masked secret field to be treated on save.</summary>
    public enum SecretChange
    {
        /// <summary>Empty input: keep whatever is already stored (never overwrite with an empty string).</summary>
        Keep,
        /// <summary>Replace with the value the user typed.</summary>
        Replace,
        /// <summary>Explicit "清除" action: store an empty value.</summary>
        Clear
    }

    /// <summary>
    /// Autovisor's config.ini, surfaced as typed fields while every unknown line stays untouched.
    ///
    /// Save order: backup -> rewrite only the values we own -> atomic replace with the original
    /// encoding. Unknown keys, comments, blank lines and section order are reproduced from the
    /// parsed line list, so a save cannot lose data the tool wrote.
    /// </summary>
    public sealed class AutovisorSettings
    {
        public const string SectionAccount = "user-account";
        public const string SectionBrowser = "browser-option";
        public const string SectionScript = "script-option";
        public const string SectionCourse = "course-option";
        public const string SectionUrls = "course-url";

        public string Username = string.Empty;
        public bool PasswordConfigured;
        public string Driver = "Edge";
        public string ExePath = string.Empty;
        public bool EnableAutoCaptcha = true;
        public bool EnableHideWindow;
        public bool ShowDonateCode = true;
        public int LimitMaxTime = 30;
        public double LimitSpeed = 1.0;
        public bool SoundOff = true;
        public List<string> CourseUrls = new List<string>();
        public List<string> Warnings = new List<string>();

        private IniDocument _document;
        private TextFile.Snapshot _snapshot;

        public static AutovisorSettings Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("配置文件不存在: " + path, path);
            AutovisorSettings settings = new AutovisorSettings();
            settings._snapshot = TextFile.Read(path);
            settings._document = IniDocument.Parse(settings._snapshot.Text);

            IniDocument document = settings._document;
            settings.Username = document.Get(SectionAccount, "username", string.Empty);
            string password = document.Get(SectionAccount, "password", string.Empty);
            settings.PasswordConfigured = !string.IsNullOrEmpty(password);
            settings.Driver = document.Get(SectionBrowser, "driver", "Edge");
            settings.ExePath = document.Get(SectionBrowser, "EXE_PATH", string.Empty);
            settings.EnableAutoCaptcha = document.GetBool(SectionScript, "enableAutoCaptcha", true);
            settings.EnableHideWindow = document.GetBool(SectionScript, "enableHideWindow", false);
            settings.ShowDonateCode = document.GetBool(SectionScript, "showDonateCode", true);
            settings.LimitMaxTime = (int)Math.Round(document.GetDouble(SectionCourse, "limitMaxTime", 30));
            settings.LimitSpeed = document.GetDouble(SectionCourse, "limitSpeed", 1.0);
            settings.SoundOff = document.GetBool(SectionCourse, "soundOff", true);

            // numeric URL keys only (URL1..URLn); the literal "URLn" placeholder is documentation,
            // not a course link, and must never be read as one nor removed on save
            List<KeyValuePair<int, string>> numeric = new List<KeyValuePair<int, string>>();
            foreach (IniLine line in document.Lines)
            {
                if (line.Kind != IniLineKind.Entry) continue;
                if (!string.Equals(line.Section, SectionUrls, StringComparison.OrdinalIgnoreCase)) continue;
                int number;
                if (!TryParseUrlIndex(line.Key, out number)) continue;
                numeric.Add(new KeyValuePair<int, string>(number, line.Value));
            }
            numeric.Sort(delegate(KeyValuePair<int, string> left, KeyValuePair<int, string> right)
            {
                return left.Key.CompareTo(right.Key);
            });
            foreach (KeyValuePair<int, string> pair in numeric)
            {
                string value = (pair.Value ?? string.Empty).Trim();
                if (value.Length > 0) settings.CourseUrls.Add(value);
            }

            if (settings.LimitSpeed > 1.8)
                settings.Warnings.Add("limitSpeed=" + settings.LimitSpeed + " 超出该工具文档说明的上限 1.8，工具可能会回落。");
            if (settings.CourseUrls.Count == 0)
                settings.Warnings.Add("config.ini 里没有任何 URL1..URLn，启动后需要在工具界面里再填课程链接。");
            return settings;
        }

        /// <summary>True for URL1 / URL2 / … (digits only). "URLn" is a placeholder and returns false.</summary>
        private static bool TryParseUrlIndex(string key, out int index)
        {
            index = 0;
            if (string.IsNullOrEmpty(key)) return false;
            if (!key.StartsWith("URL", StringComparison.OrdinalIgnoreCase)) return false;
            if (key.Length <= 3) return false; // "URL" or "URLn"
            string digits = key.Substring(3);
            for (int i = 0; i < digits.Length; i++)
                if (!char.IsDigit(digits[i])) return false;
            return int.TryParse(digits, out index) && index >= 1;
        }

        /// <summary>Writes the fields back. Returns the backup path that was created first.</summary>
        public string Save(string path, BackupService backups, SecretChange passwordChange, string newPassword)
        {
            if (_document == null || _snapshot == null)
                throw new InvalidOperationException("必须先 Load 再 Save");

            string backupPath = string.Empty;
            if (backups != null)
            {
                BackupEntry entry = backups.Create("autovisor", path);
                if (entry != null) backupPath = entry.FilePath;
            }

            IniDocument document = _document;
            document.Set(SectionAccount, "username", Username ?? string.Empty);
            if (passwordChange == SecretChange.Replace) document.Set(SectionAccount, "password", newPassword ?? string.Empty);
            else if (passwordChange == SecretChange.Clear) document.Set(SectionAccount, "password", string.Empty);

            document.Set(SectionBrowser, "driver", string.IsNullOrEmpty(Driver) ? "Edge" : Driver);
            document.Set(SectionBrowser, "EXE_PATH", ExePath ?? string.Empty);
            document.Set(SectionScript, "enableAutoCaptcha", EnableAutoCaptcha ? "True" : "False");
            document.Set(SectionScript, "enableHideWindow", EnableHideWindow ? "True" : "False");
            document.Set(SectionScript, "showDonateCode", ShowDonateCode ? "True" : "False");
            document.Set(SectionCourse, "limitMaxTime", LimitMaxTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            document.Set(SectionCourse, "limitSpeed", FormatSpeed(LimitSpeed));
            document.Set(SectionCourse, "soundOff", SoundOff ? "True" : "False");

            List<string> urls = new List<string>();
            for (int i = 0; i < CourseUrls.Count; i++)
            {
                string url = (CourseUrls[i] ?? string.Empty).Trim();
                if (url.Length > 0) urls.Add(url);
            }

            for (int i = 0; i < urls.Count; i++) document.Set(SectionUrls, "URL" + (i + 1), urls[i]);

            // a shortened (or emptied) list must not leave stale links behind: collect the numeric
            // URL keys that are now beyond the list, then remove exactly those lines.
            List<string> stale = new List<string>();
            foreach (IniLine line in document.Lines)
            {
                if (line.Kind != IniLineKind.Entry) continue;
                if (!string.Equals(line.Section, SectionUrls, StringComparison.OrdinalIgnoreCase)) continue;
                int number;
                if (!TryParseUrlIndex(line.Key, out number)) continue;
                if (number > urls.Count) stale.Add(line.Key);
            }
            for (int i = 0; i < stale.Count; i++) document.Remove(SectionUrls, stale[i]);

            TextFile.Write(_snapshot, document.Render(_snapshot.NewLine));
            _snapshot = TextFile.Read(path);
            _document = IniDocument.Parse(_snapshot.Text);
            return backupPath;
        }

        private static string FormatSpeed(double speed)
        {
            if (speed <= 0) speed = 1.0;
            string text = speed.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
            return text;
        }
    }
}

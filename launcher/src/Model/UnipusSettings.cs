using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TimeSaver.Config;
using TimeSaver.Core;

namespace TimeSaver.Model
{
    /// <summary>
    /// UnipusAI's config.json, surfaced as typed fields while all other keys survive untouched.
    ///
    /// Type check first (as required): every known field's JSON kind is inspected on load and a
    /// mismatch is reported as a warning instead of being silently rewritten. api_key honours the
    /// "blank keeps the stored key, explicit clear erases it" rule.
    /// </summary>
    public sealed class UnipusSettings
    {
        public string Username = string.Empty;
        public bool PasswordConfigured;
        public string BaseUrl = string.Empty;
        public string Model = string.Empty;
        public string LearningStrategy = string.Empty;
        public int MaxTokens = 2000;
        public double Temperature = 0.3;
        public int IntervalMs = 3000;
        public bool WhisperEnabled = true;
        public string WhisperModel = "base";
        public string WhisperLanguage = "auto";
        public bool ApiKeyConfigured;
        public List<string> Warnings = new List<string>();

        private JsonDocument _document;
        private string _path;

        public static readonly string[] LearningStrategies = new string[]
        {
            "learn_all_compusory_course",
            "learn_all_course",
            "learn_compusory_course_only",
            "manual"
        };

        public static UnipusSettings Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("配置文件不存在: " + path, path);
            UnipusSettings settings = new UnipusSettings();
            settings._path = path;
            string text = File.ReadAllText(path, DetectEncoding(path));
            settings._document = JsonDocument.Parse(text);
            JsonValue root = settings._document.Root;
            if (root == null || root.Kind != JsonKind.Object)
                throw new InvalidDataException("config.json 顶层不是 JSON 对象: " + path);

            settings.BaseUrl = ReadString(root, "base_url", settings.Warnings, string.Empty);
            settings.Username = ReadString(root, "username", settings.Warnings, string.Empty);
            JsonValue password = root.Get("password");
            settings.PasswordConfigured = password != null && password.Kind == JsonKind.String && !string.IsNullOrEmpty(password.Text);
            settings.Model = ReadString(root, "model", settings.Warnings, string.Empty);
            settings.LearningStrategy = ReadString(root, "learning_strategy", settings.Warnings, "learn_all_compusory_course");
            settings.WhisperModel = ReadString(root, "whisper_model", settings.Warnings, "base");
            settings.WhisperLanguage = ReadString(root, "whisper_language", settings.Warnings, "auto");

            int maxTokens;
            if (TryReadInt(root, "max_tokens", settings.Warnings, 2000, out maxTokens)) settings.MaxTokens = maxTokens;

            double temperature;
            if (TryReadDouble(root, "temperature", settings.Warnings, 0.3, out temperature)) settings.Temperature = temperature;

            int interval;
            if (TryReadInt(root, "interval_ms", settings.Warnings, 3000, out interval)) settings.IntervalMs = interval;

            bool whisper;
            if (TryReadBool(root, "whisper_enabled", settings.Warnings, true, out whisper)) settings.WhisperEnabled = whisper;

            JsonValue apiKey = root.Get("api_key");
            settings.ApiKeyConfigured = apiKey != null && apiKey.Kind == JsonKind.String && !string.IsNullOrEmpty(apiKey.Text);

            if (settings.ApiKeyConfigured == false && apiKey != null && apiKey.Kind != JsonKind.String && apiKey.Kind != JsonKind.Null)
                settings.Warnings.Add("api_key 的类型是 " + apiKey.Kind + "，不是字符串；保存时会写成字符串。");

            if (settings.MaxTokens < 1 || settings.MaxTokens > 100000)
                settings.Warnings.Add("max_tokens=" + settings.MaxTokens + " 不在常见范围，请确认工具是否接受。");
            if (settings.IntervalMs < 0)
                settings.Warnings.Add("interval_ms 为负数，工具行为未定义。");
            if (settings.Temperature < 0 || settings.Temperature > 2)
                settings.Warnings.Add("temperature=" + settings.Temperature + " 超出 0..2。");
            return settings;
        }

        private static Encoding DetectEncoding(string path)
        {
            byte[] head = new byte[3];
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read = stream.Read(head, 0, 3);
                if (read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) return new UTF8Encoding(true);
            }
            return new UTF8Encoding(false);
        }

        private static string ReadString(JsonValue root, string key, List<string> warnings, string fallback)
        {
            JsonValue value = root.Get(key);
            if (value == null) return fallback;
            if (value.Kind == JsonKind.String) return value.Text;
            if (value.Kind == JsonKind.Null) return fallback;
            warnings.Add(key + " 的类型是 " + value.Kind + "，不是字符串；保存时会写成字符串。");
            return value.AsString(fallback);
        }

        private static bool TryReadInt(JsonValue root, string key, List<string> warnings, int fallback, out int result)
        {
            result = fallback;
            JsonValue value = root.Get(key);
            if (value == null) return false;
            if (value.Kind != JsonKind.Number && value.Kind != JsonKind.String)
            {
                warnings.Add(key + " 的类型是 " + value.Kind + "，不是数值；保存时会写成数值。");
                return false;
            }
            if (value.Kind == JsonKind.String) warnings.Add(key + " 以字符串形式保存，预期是数值；保存时会写成数值。");
            int parsed;
            if (!value.TryGetInt(out parsed))
            {
                warnings.Add(key + " 的值无法解析为整数: " + value.AsString(""));
                return false;
            }
            result = parsed;
            return true;
        }

        private static bool TryReadDouble(JsonValue root, string key, List<string> warnings, double fallback, out double result)
        {
            result = fallback;
            JsonValue value = root.Get(key);
            if (value == null) return false;
            if (value.Kind != JsonKind.Number && value.Kind != JsonKind.String)
            {
                warnings.Add(key + " 的类型是 " + value.Kind + "，不是数值；保存时会写成数值。");
                return false;
            }
            if (value.Kind == JsonKind.String) warnings.Add(key + " 以字符串形式保存，预期是数值；保存时会写成数值。");
            double parsed;
            if (!value.TryGetDouble(out parsed))
            {
                warnings.Add(key + " 的值无法解析为数值: " + value.AsString(""));
                return false;
            }
            result = parsed;
            return true;
        }

        private static bool TryReadBool(JsonValue root, string key, List<string> warnings, bool fallback, out bool result)
        {
            result = fallback;
            JsonValue value = root.Get(key);
            if (value == null) return false;
            if (value.Kind != JsonKind.Bool && value.Kind != JsonKind.String)
            {
                warnings.Add(key + " 的类型是 " + value.Kind + "，不是布尔；保存时会写成布尔。");
                return false;
            }
            if (value.Kind == JsonKind.String) warnings.Add(key + " 以字符串形式保存，预期是布尔；保存时会写成布尔。");
            bool parsed;
            if (!value.TryGetBool(out parsed))
            {
                warnings.Add(key + " 的值无法解析为布尔: " + value.AsString(""));
                return false;
            }
            result = parsed;
            return true;
        }

        public string Save(BackupService backups, SecretChange apiKeyChange, string newApiKey)
        {
            if (_document == null || _document.Root == null)
                throw new InvalidOperationException("必须先 Load 再 Save");
            if (double.IsNaN(Temperature) || double.IsInfinity(Temperature))
                throw new ArgumentOutOfRangeException("Temperature", Temperature,
                    "temperature 必须是有限数值（NaN / Infinity 会写出非法 JSON）");
            if (MaxTokens < 1) throw new ArgumentOutOfRangeException("MaxTokens", MaxTokens, "max_tokens 必须是正整数");
            if (IntervalMs < 0) throw new ArgumentOutOfRangeException("IntervalMs", IntervalMs, "interval_ms 不能为负");

            string backupPath = string.Empty;
            if (backups != null)
            {
                BackupEntry entry = backups.Create("unipus", _path);
                if (entry != null) backupPath = entry.FilePath;
            }

            JsonValue root = _document.Root;
            root.Set("base_url", JsonValue.NewString(BaseUrl ?? string.Empty));
            root.Set("model", JsonValue.NewString(Model ?? string.Empty));
            root.Set("learning_strategy", JsonValue.NewString(
                string.IsNullOrEmpty(LearningStrategy) ? "learn_all_compusory_course" : LearningStrategy));
            root.Set("max_tokens", JsonValue.NewNumber(MaxTokens));
            root.Set("temperature", JsonValue.NewNumber(Temperature));
            root.Set("interval_ms", JsonValue.NewNumber(IntervalMs));
            root.Set("whisper_enabled", JsonValue.NewBool(WhisperEnabled));
            root.Set("whisper_model", JsonValue.NewString(string.IsNullOrEmpty(WhisperModel) ? "base" : WhisperModel));
            root.Set("whisper_language", JsonValue.NewString(string.IsNullOrEmpty(WhisperLanguage) ? "auto" : WhisperLanguage));

            if (apiKeyChange == SecretChange.Replace) root.Set("api_key", JsonValue.NewString(newApiKey ?? string.Empty));
            else if (apiKeyChange == SecretChange.Clear) root.Set("api_key", JsonValue.NewString(string.Empty));

            string rendered = _document.Render();
            string temp = _path + ".timesaver-tmp";
            File.WriteAllText(temp, rendered, new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(temp, _path, null, true);
            else File.Move(temp, _path);

            ApiKeyConfigured = apiKeyChange == SecretChange.Replace
                ? !string.IsNullOrEmpty(newApiKey)
                : (apiKeyChange == SecretChange.Clear ? false : ApiKeyConfigured);
            return backupPath;
        }

        public string SaveAccount(BackupService backups, string username, SecretChange passwordChange, string newPassword)
        {
            username = (username ?? string.Empty).Trim();
            if (username.Length == 0) throw new ArgumentException("请填写 U 校园账号。");
            // Reload to preserve AI settings changed since this form was opened.
            UnipusSettings latest = Load(_path);
            bool switching = !string.Equals(latest.Username, username, StringComparison.Ordinal);
            if (switching && (passwordChange != SecretChange.Replace || string.IsNullOrEmpty(newPassword)))
                throw new ArgumentException("更换账号时请同时填写该账号的密码。");
            if (passwordChange == SecretChange.Replace && string.IsNullOrEmpty(newPassword))
                throw new ArgumentException("新密码不能为空。");
            string backupPath = string.Empty;
            if (backups != null)
            {
                BackupEntry entry = backups.Create("unipus", _path);
                if (entry != null) backupPath = entry.FilePath;
            }
            JsonValue root = latest._document.Root;
            root.Set("username", JsonValue.NewString(username));
            if (passwordChange == SecretChange.Replace) root.Set("password", JsonValue.NewString(newPassword));
            else if (passwordChange == SecretChange.Clear) root.Set("password", JsonValue.NewString(string.Empty));
            if (switching || passwordChange != SecretChange.Keep)
            {
                foreach (string key in new string[] { "cookie", "authorization", "refresh_token", "open_id", "x_annotator_auth_token" })
                    root.Set(key, JsonValue.NewString(string.Empty));
                foreach (string key in new string[] { "jwt_expire", "rt_expire" })
                    root.Set(key, JsonValue.NewNumber(0));
            }
            if (switching)
            {
                foreach (string key in new string[] { "course_id", "class_id", "curricula_id", "u_school", "publish_version" })
                    root.Set(key, JsonValue.NewString(string.Empty));
            }
            string temp = _path + ".timesaver-tmp";
            File.WriteAllText(temp, latest._document.Render(), new UTF8Encoding(false));
            File.Replace(temp, _path, null, true);
            return backupPath;
        }

        public string PathForDisplay { get { return _path; } }
    }
}

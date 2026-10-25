using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TimeSaver.Diagnostics
{
    /// <summary>
    /// Append-only run log for the launcher itself.
    ///
    /// Everything written here can end up in a shared bug report, so the log never receives raw
    /// secrets: Mask() removes API keys, passwords, cookies, tokens and bearer headers before the
    /// text reaches the file. Logs live under launcher\logs and rotate at 4 MB.
    /// </summary>
    public sealed class LogService
    {
        private static readonly Regex SecretPattern = new Regex(
            @"(?i)\b(api[_-]?key|apikey|password|passwd|pwd|secret|token|refresh_token|x_annotator_auth_token|authorization|cookie)\b(\s*[=:]\s*)(?:(?i:Bearer)\s+)?(?:""[^""]*""|'[^']*'|[^\s,;]+)",
            RegexOptions.Compiled);

        private static readonly Regex BearerPattern = new Regex(@"(?i)\bBearer\s+[A-Za-z0-9\-\._~\+/]+=*", RegexOptions.Compiled);
        private static readonly Regex LongHexPattern = new Regex(@"\b[0-9a-fA-F]{32,}\b", RegexOptions.Compiled);
        private static readonly Regex LongBase64Pattern = new Regex(@"\b[A-Za-z0-9\+/]{40,}={0,2}\b", RegexOptions.Compiled);

        private readonly object _gate = new object();
        private readonly string _directory;

        public LogService(string directory)
        {
            _directory = directory;
            if (!Directory.Exists(_directory)) Directory.CreateDirectory(_directory);
        }

        public string CurrentFile
        {
            get { return Path.Combine(_directory, "launcher-" + DateTime.Now.ToString("yyyyMMdd") + ".log"); }
        }

        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string result = SecretPattern.Replace(text, new MatchEvaluator(delegate(Match match)
            {
                return match.Groups[1].Value + match.Groups[2].Value + "***";
            }));
            result = BearerPattern.Replace(result, "Bearer ***");
            result = LongHexPattern.Replace(result, "***");
            result = LongBase64Pattern.Replace(result, "***");
            return result;
        }

        public void Info(string message) { Write("INFO", message); }
        public void Warn(string message) { Write("WARN", message); }
        public void Error(string message) { Write("ERROR", message); }

        public void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + Mask(message);
            lock (_gate)
            {
                try
                {
                    RotateIfNeeded();
                    File.AppendAllText(CurrentFile, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                    // logging must never break the app
                }
            }
        }

        private void RotateIfNeeded()
        {
            string file = CurrentFile;
            if (!File.Exists(file)) return;
            FileInfo info = new FileInfo(file);
            if (info.Length < 4 * 1024 * 1024) return;
            string rotated = file + ".1";
            if (File.Exists(rotated)) File.Delete(rotated);
            File.Move(file, rotated);
        }

        public string Tail(int lines)
        {
            try
            {
                if (!File.Exists(CurrentFile)) return string.Empty;
                string[] all = File.ReadAllLines(CurrentFile);
                int start = Math.Max(0, all.Length - lines);
                StringBuilder builder = new StringBuilder();
                for (int i = start; i < all.Length; i++) builder.AppendLine(all[i]);
                return builder.ToString();
            }
            catch (Exception error)
            {
                return "（读取日志失败: " + error.Message + "）";
            }
        }
    }
}

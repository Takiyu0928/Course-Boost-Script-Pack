using System;
using System.IO;
using System.Text;

namespace TimeSaver.Config
{
    /// <summary>
    /// Reads and writes small text configuration files while preserving their original encoding,
    /// BOM presence and line endings. Writes are atomic: temp file in the same folder, then a
    /// replace; the previous content is kept by BackupService (never silently lost).
    /// </summary>
    public static class TextFile
    {
        public sealed class Snapshot
        {
            public string Text;
            public Encoding Encoding;
            public bool HasBom;
            public string NewLine;
            public string Path;
        }

        private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);
        private static readonly Encoding Gbk = Encoding.GetEncoding(936);

        public static Snapshot Read(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            byte[] bytes = File.ReadAllBytes(path);

            Encoding encoding = new UTF8Encoding(false);
            bool hasBom = false;
            int offset = 0;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true);
                hasBom = true;
                offset = 3;
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = new UnicodeEncoding(false, true);
                hasBom = true;
                offset = 2;
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = new UnicodeEncoding(true, true);
                hasBom = true;
                offset = 2;
            }
            else
            {
                encoding = DetectLegacy(bytes);
            }

            string text = encoding.GetString(bytes, offset, bytes.Length - offset);
            Snapshot snapshot = new Snapshot();
            snapshot.Path = path;
            snapshot.Text = text;
            snapshot.Encoding = encoding;
            snapshot.HasBom = hasBom;
            snapshot.NewLine = DetectNewLine(text);
            return snapshot;
        }

        /// <summary>UTF-8 when the bytes are valid UTF-8, otherwise GBK (936).</summary>
        private static Encoding DetectLegacy(byte[] bytes)
        {
            try
            {
                Utf8Strict.GetString(bytes);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                return Gbk;
            }
        }

        private static string DetectNewLine(string text)
        {
            int lf = 0;
            int crlf = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                if (i > 0 && text[i - 1] == '\r') crlf++;
                else lf++;
            }
            if (crlf == 0 && lf == 0) return Environment.NewLine;
            return crlf >= lf ? "\r\n" : "\n";
        }

        /// <summary>Write text with the encoding captured by Read, atomically.</summary>
        public static void Write(Snapshot snapshot, string text)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            string directory = Path.GetDirectoryName(snapshot.Path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                throw new DirectoryNotFoundException("配置目录不存在: " + directory);

            string temp = snapshot.Path + ".timesaver-tmp";
            byte[] body = snapshot.Encoding.GetBytes(text);
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (snapshot.HasBom && snapshot.Encoding is UTF8Encoding)
                    stream.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
                else if (snapshot.HasBom && snapshot.Encoding is UnicodeEncoding)
                    stream.Write(snapshot.Encoding.GetPreamble(), 0, snapshot.Encoding.GetPreamble().Length);
                stream.Write(body, 0, body.Length);
                stream.Flush(true);
            }

            if (File.Exists(snapshot.Path))
            {
                File.Replace(temp, snapshot.Path, null, true);
            }
            else
            {
                File.Move(temp, snapshot.Path);
            }
        }

        /// <summary>Write arbitrary text as UTF-8 (used for app-owned files such as logs).</summary>
        public static void WriteUtf8(string path, string text)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }
}

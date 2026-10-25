using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TimeSaver.Core
{
    /// <summary>
    /// All paths are derived from the folder that contains TimeSaver.exe, so the whole "time saver"
    /// folder can be moved or renamed (including spaces, Chinese characters, apostrophes and the
    /// literal "exe)" folder of the xuexitong tool). Nothing is taken from the current directory
    /// and nothing is resolved through a shell.
    /// </summary>
    public sealed class PathResolver
    {
        public string InstallRoot { get; private set; }
        public string LauncherDirectory { get { return Path.Combine(InstallRoot, "launcher"); } }
        public string LogsDirectory { get { return Path.Combine(LauncherDirectory, "logs"); } }
        public string BackupsDirectory { get { return Path.Combine(LauncherDirectory, "backups"); } }
        public string StateFile { get { return Path.Combine(LauncherDirectory, "state.json"); } }
        public string ToolsManifestFile { get { return Path.Combine(LauncherDirectory, "tools.json"); } }

        private PathResolver(string installRoot)
        {
            InstallRoot = installRoot;
        }

        /// <summary>Directory names of the four shipped tools (used to recognise the install root).</summary>
        private static readonly string[] KnownToolDirectories = new string[]
        {
            "Autovisor-3.18.4-windows-amd64",
            "UnipusAI-v3.6-windows-x86-64",
            "domooc_free",
            "xuexitong_shuake26.10"
        };

        public static PathResolver FromExecutableDirectory()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(baseDirectory))
                throw new InvalidOperationException("无法确定程序所在目录");
            string trimmed = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0) trimmed = baseDirectory;
            return FromStartDirectory(trimmed);
        }

        /// <summary>
        /// Resolves the install root from wherever the exe was started.
        ///
        /// Both documented entry points must work:
        ///   &lt;root&gt;\TimeSaver.exe                 → root
        ///   &lt;root&gt;\launcher\build\TimeSaver.exe   → root
        ///
        /// The search is deliberately narrow: at most two levels up, and a candidate only wins if it
        /// actually looks like this install (has a launcher\ child or one of the known tool
        /// folders). Unrelated parent directories are never adopted, and an unrecognised layout
        /// falls back to the start directory (the UI then reports the missing tools instead of
        /// silently pointing at the wrong place).
        /// </summary>
        public static PathResolver FromStartDirectory(string startDirectory)
        {
            if (string.IsNullOrEmpty(startDirectory)) throw new ArgumentNullException("startDirectory");
            string start = Path.GetFullPath(startDirectory);
            if (LooksLikeInstallRoot(start)) return new PathResolver(start);

            string current = start;
            for (int level = 0; level < 2; level++)
            {
                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
                if (LooksLikeInstallRoot(current)) return new PathResolver(current);
            }
            return new PathResolver(start);
        }

        public static bool LooksLikeInstallRoot(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return false;
            try
            {
                if (!Directory.Exists(directory)) return false;
                if (Directory.Exists(Path.Combine(directory, "launcher"))) return true;
                for (int i = 0; i < KnownToolDirectories.Length; i++)
                    if (Directory.Exists(Path.Combine(directory, KnownToolDirectories[i]))) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static PathResolver ForRoot(string root)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentNullException("root");
            return new PathResolver(Path.GetFullPath(root));
        }

        /// <summary>Combine a path relative to the install root. Never quotes or escapes; callers pass paths to APIs, not shells.</summary>
        public string Combine(params string[] parts)
        {
            string result = InstallRoot;
            foreach (string part in parts) result = Path.Combine(result, part);
            return result;
        }

        public bool FileExists(params string[] parts)
        {
            return File.Exists(Combine(parts));
        }

        public bool DirectoryExists(params string[] parts)
        {
            return Directory.Exists(Combine(parts));
        }

        /// <summary>True when the path stays inside the install root (guards against ".." in manifests).</summary>
        public bool IsInsideRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return false; }
            string root = InstallRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), InstallRoot, StringComparison.OrdinalIgnoreCase);
        }

        public void EnsureDirectories()
        {
            if (!Directory.Exists(LauncherDirectory)) Directory.CreateDirectory(LauncherDirectory);
            if (!Directory.Exists(LogsDirectory)) Directory.CreateDirectory(LogsDirectory);
            if (!Directory.Exists(BackupsDirectory)) Directory.CreateDirectory(BackupsDirectory);
        }
    }

    public enum PortState
    {
        Closed,
        OurService,
        Foreign
    }

    public sealed class PortProbeResult
    {
        public PortState State;
        public string Detail = string.Empty;
    }

    /// <summary>
    /// Checks what is listening on a loopback port without ever terminating anything.
    ///
    /// A port that is open is not enough evidence: the launcher must know whether the listener is
    /// the domooc answer service (then it can be reused) or some unrelated program (then the
    /// launcher leaves it alone and reports a conflict). Identity comes from GET /api/health and
    /// the documented payload {"ok":true,"mode":"local-only",...}.
    /// </summary>
    public static class PortProbe
    {
        public static PortProbeResult ProbeAnswerService(int port, int timeoutMs)
        {
            PortProbeResult result = new PortProbeResult();

            TcpClient client = new TcpClient();
            try
            {
                IAsyncResult connect = client.BeginConnect(IPAddress.Loopback, port, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    result.State = PortState.Closed;
                    result.Detail = "端口 " + port + " 未在监听（连接超时）";
                    return result;
                }
                client.EndConnect(connect);
            }
            catch (SocketException error)
            {
                result.State = PortState.Closed;
                result.Detail = "端口 " + port + " 未在监听（" + error.SocketErrorCode + "）";
                return result;
            }
            finally
            {
                try { client.Close(); } catch { }
            }

            string body;
            int status;
            if (!TryHttpGet(port, "/api/health", timeoutMs * 3, out status, out body))
            {
                result.State = PortState.Foreign;
                result.Detail = "端口 " + port + " 被占用，但 /api/health 无响应，未识别为 domooc 答题服务";
                return result;
            }

            bool looksLikeDomooc =
                status == 200 &&
                body.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0 &&
                body.IndexOf("local-only", StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksLikeDomooc)
            {
                result.State = PortState.OurService;
                result.Detail = "已识别为 domooc 本机答题服务: " + Trim(body, 120);
            }
            else
            {
                result.State = PortState.Foreign;
                result.Detail = "端口 " + port + " 被其它程序占用（HTTP " + status + "): " + Trim(body, 120);
            }
            return result;
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= max ? flat : flat.Substring(0, max) + "…";
        }

        private static bool TryHttpGet(int port, string path, int timeoutMs, out int status, out string body)
        {
            status = 0;
            body = string.Empty;
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult connect = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    client.EndConnect(connect);
                    client.ReceiveTimeout = timeoutMs;

                    using (NetworkStream stream = client.GetStream())
                    {
                        string request = "GET " + path + " HTTP/1.1\r\n" +
                                         "Host: 127.0.0.1:" + port + "\r\n" +
                                         "Connection: close\r\n" +
                                         "Accept: application/json\r\n\r\n";
                        byte[] requestBytes = Encoding.ASCII.GetBytes(request);
                        stream.Write(requestBytes, 0, requestBytes.Length);

                        MemoryStream buffer = new MemoryStream();
                        byte[] chunk = new byte[4096];
                        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                        while (DateTime.UtcNow < deadline)
                        {
                            if (!stream.DataAvailable)
                            {
                                if (!client.Connected) break;
                                System.Threading.Thread.Sleep(15);
                                if (buffer.Length > 0 && !stream.DataAvailable) break;
                                continue;
                            }
                            int read = stream.Read(chunk, 0, chunk.Length);
                            if (read <= 0) break;
                            buffer.Write(chunk, 0, read);
                            if (buffer.Length > 256 * 1024) break;
                        }

                        string raw = Encoding.UTF8.GetString(buffer.ToArray());
                        if (raw.Length == 0) return false;

                        int firstLineEnd = raw.IndexOf("\r\n", StringComparison.Ordinal);
                        string statusLine = firstLineEnd > 0 ? raw.Substring(0, firstLineEnd) : raw;
                        string[] parts = statusLine.Split(' ');
                        if (parts.Length < 2) return false;
                        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out status)) return false;

                        int bodyStart = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        body = bodyStart >= 0 ? raw.Substring(bodyStart + 4) : string.Empty;
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}

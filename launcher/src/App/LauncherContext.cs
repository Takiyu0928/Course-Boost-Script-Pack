using System;
using System.Collections.Generic;
using System.IO;
using TimeSaver.Core;
using TimeSaver.Diagnostics;
using TimeSaver.Model;

namespace TimeSaver.App
{
    /// <summary>Shared services for the whole app: paths, catalog, process bookkeeping, backups, log.</summary>
    public sealed class LauncherContext
    {
        public PathResolver Paths { get; private set; }
        public ToolCatalog Catalog { get; private set; }
        public ProcessSupervisor Supervisor { get; private set; }
        public BackupService Backups { get; private set; }
        public LogService Log { get; private set; }
        public EnvironmentProbe Probe { get; private set; }
        public ToolRunner Runner { get; private set; }

        public static LauncherContext Create(PathResolver paths)
        {
            LauncherContext context = new LauncherContext();
            context.Paths = paths;
            paths.EnsureDirectories();
            context.Log = new LogService(paths.LogsDirectory);
            context.Supervisor = new ProcessSupervisor();
            context.Backups = new BackupService(paths.BackupsDirectory);
            context.Catalog = ToolCatalog.CreateDefault(paths);
            context.Probe = new EnvironmentProbe(context);
            context.Runner = new ToolRunner(context);
            context.Log.Info("启动器启动，安装目录 " + paths.InstallRoot);
            return context;
        }

        public string ToolDirectory(ToolDefinition tool)
        {
            if (tool == null || string.IsNullOrEmpty(tool.Directory)) return Paths.InstallRoot;
            return Path.Combine(Paths.InstallRoot, tool.Directory);
        }

        public string ConfigPath(ToolDefinition tool)
        {
            if (tool == null || string.IsNullOrEmpty(tool.ConfigRelativePath)) return string.Empty;
            return Path.Combine(ToolDirectory(tool), tool.ConfigRelativePath);
        }

        public static string NormalizeRelative(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return string.Empty;
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }
    }

    public sealed class ProbeItem
    {
        public string Name = string.Empty;
        public bool Ok;
        public bool IsWarning;
        public string Detail = string.Empty;
    }

    /// <summary>
    /// Read-only environment diagnostics. Nothing here changes the machine: no registry writes, no
    /// proxy/hosts/firewall changes, no admin. Child processes are started only with fixed
    /// executable paths and fixed arguments (never through a shell) to read their version.
    /// </summary>
    public sealed class EnvironmentProbe
    {
        private readonly LauncherContext _context;

        public EnvironmentProbe(LauncherContext context) { _context = context; }

        private List<ProbeItem> _cachedFull;
        private DateTime _cachedAt = DateTime.MinValue;

        public List<ProbeItem> Run(bool includeToolChecks)
        {
            if (includeToolChecks && _cachedFull != null &&
                (DateTime.UtcNow - _cachedAt).TotalSeconds < 15)
                return _cachedFull;

            List<ProbeItem> items = new List<ProbeItem>();

            items.Add(Make("Windows", true, Environment.OSVersion.VersionString + " · " +
                                                 (Environment.Is64BitOperatingSystem ? "x64" : "x86")));

            string framework = ReadFrameworkVersion();
            bool frameworkOk = framework.StartsWith("4.8", StringComparison.OrdinalIgnoreCase);
            items.Add(Make("".Trim() == "" ? ".NET Framework" : ".NET Framework", frameworkOk,
                framework + (frameworkOk ? "（启动器自身运行所需，Windows 内置）" : "（需要 4.8，启动器无法运行）")));

            string nodePath = FindExecutable("node.exe");
            if (nodePath.Length == 0)
                items.Add(Make("Node.js", false, "未找到（domooc 本地题库服务需要；缺它仍可刷课）", true));
            else
                items.Add(Make("Node.js", true, nodePath + " " + ShortVersion(nodePath, "--version")));

            string pythonPath = FindExecutable("python.exe");
            if (pythonPath.Length == 0)
                items.Add(Make("Python", false, "未找到（仅 domooc 的 auto/run.py 需要，本次不启动它）", true));
            else
                items.Add(Make("Python", true, pythonPath + " " + ShortVersion(pythonPath, "--version")));

            PortProbeResult port = PortProbe.ProbeAnswerService(8791, 400);
            if (port.State == PortState.OurService) items.Add(Make("本地答题服务 8791", true, port.Detail));
            else if (port.State == PortState.Foreign) items.Add(Make("本地答题服务 8791", false, port.Detail, true));
            else items.Add(Make("本地答题服务 8791", true, port.Detail + "（按需启动）", true));

            bool writable = CanWrite(_context.Paths.LauncherDirectory);
            items.Add(Make("启动器目录可写", writable, _context.Paths.LauncherDirectory));

            if (includeToolChecks)
            {
                foreach (ToolDefinition tool in _context.Catalog.Tools)
                {
                    string directory = _context.ToolDirectory(tool);
                    if (!Directory.Exists(directory))
                    {
                        items.Add(Make(tool.Name, false, "目录不存在: " + directory));
                        continue;
                    }
                    List<string> missing = new List<string>();
                    foreach (string required in tool.RequiredFiles)
                    {
                        string path = Path.Combine(directory, LauncherContext.NormalizeRelative(required));
                        if (!File.Exists(path) && !Directory.Exists(path)) missing.Add(required);
                    }
                    if (missing.Count == 0) items.Add(Make(tool.Name, true, "文件齐全"));
                    else items.Add(Make(tool.Name, false, "缺少: " + string.Join("、", missing.ToArray())));
                }
            }

            if (includeToolChecks)
            {
                _cachedFull = items;
                _cachedAt = DateTime.UtcNow;
            }
            return items;
        }

        private static ProbeItem Make(string name, bool ok, string detail)
        {
            ProbeItem item = new ProbeItem();
            item.Name = name;
            item.Ok = ok;
            item.Detail = detail;
            return item;
        }

        private static ProbeItem Make(string name, bool ok, string detail, bool warning)
        {
            ProbeItem item = Make(name, ok, detail);
            item.IsWarning = warning;
            return item;
        }

        public static string ReadFrameworkVersion()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key == null) return "未检测到（需要 4.8）";
                    object release = key.GetValue("Release");
                    object version = key.GetValue("Version");
                    string text = version == null ? "?" : version.ToString();
                    if (release != null)
                    {
                        int value = Convert.ToInt32(release);
                        if (value >= 533320) return text + " (4.8.1+)";
                        if (value >= 533509) return text + " (4.8)";
                    }
                    return text;
                }
            }
            catch
            {
                return "无法读取（需要 4.8）";
            }
        }

        /// <summary>PATH / common install locations lookup. No shell, no `where.exe`.</summary>
        public static string FindExecutable(string fileName)
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                string[] parts = path.Split(';');
                for (int i = 0; i < parts.Length; i++)
                {
                    string directory = parts[i].Trim();
                    if (directory.Length == 0) continue;
                    try
                    {
                        string candidate = Path.Combine(directory, fileName);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch
                    {
                        // ignore malformed PATH entries
                    }
                }
            }

            string[] defaults = new string[]
            {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
                @"C:\Program Files\Python312\python.exe",
                @"C:\Program Files\Python311\python.exe",
                @"C:\Program Files\Python310\python.exe"
            };
            for (int i = 0; i < defaults.Length; i++)
            {
                if (string.Equals(Path.GetFileName(defaults[i]), fileName, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(defaults[i]))
                    return defaults[i];
            }
            return string.Empty;
        }

        private static readonly Dictionary<string, string> VersionCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Runs a fixed executable with one fixed argument to read a version string.
        /// CreateNoWindow + redirected stdio: the version probe must never flash a console window.
        /// Results are cached, so this happens at most once per executable per session.
        /// </summary>
        public static string ShortVersion(string exePath, string argument)
        {
            string cacheKey = exePath + "|" + argument;
            lock (VersionCache)
            {
                if (VersionCache.ContainsKey(cacheKey)) return VersionCache[cacheKey];
            }
            string result = ShortVersionUncached(exePath, argument);
            lock (VersionCache)
            {
                VersionCache[cacheKey] = result;
            }
            return result;
        }

        private static string ShortVersionUncached(string exePath, string argument)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo();
                info.FileName = exePath;
                info.Arguments = ArgumentList.Join(new string[] { argument });
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
                {
                    if (process == null) return "(无法启动)";
                    string output = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(3000))
                    {
                        try { process.Kill(); } catch { }
                        return "(超时)";
                    }
                    output = (output ?? string.Empty).Trim().Replace("\r", " ").Replace("\n", " ");
                    return output.Length > 60 ? output.Substring(0, 60) : output;
                }
            }
            catch (Exception error)
            {
                return "(读取失败: " + error.Message + ")";
            }
        }

        private static bool CanWrite(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TimeSaver.Core;
using TimeSaver.Model;

namespace TimeSaver.App
{
    public sealed class PreflightItem
    {
        public string Name = string.Empty;
        public bool Ok;
        public bool Warning;
        public string Detail = string.Empty;
    }

    public sealed class PreflightReport
    {
        public List<PreflightItem> Items = new List<PreflightItem>();
        /// <summary>true when at least one blocking item failed.</summary>
        public bool Blocked;

        public void Add(string name, bool ok, bool warning, string detail)
        {
            PreflightItem item = new PreflightItem();
            item.Name = name;
            item.Ok = ok;
            item.Warning = warning;
            item.Detail = detail;
            Items.Add(item);
            if (!ok && !warning) Blocked = true;
        }
    }

    /// <summary>
    /// Turns a catalog entry into an actual launch.
    ///
    /// Invariants:
    ///  * no shell and no string-built command line: exe path + argument list go to
    ///    ProcessSupervisor, which quotes through ArgumentList;
    ///  * nothing is killed to free a port - a busy port is identified first and otherwise reported;
    ///  * domination order for domooc is service first, then browser, and a failed service never
    ///    blocks the browser (course playback does not need it);
    ///  * the launcher never deletes Firefox profile lock files (the original .bat does; that is
    ///    exactly the behaviour we must not copy).
    /// </summary>
    public sealed class ToolRunner
    {
        public const int AnswerServicePort = 8791;
        private readonly LauncherContext _context;

        public ToolRunner(LauncherContext context) { _context = context; }

        public PreflightReport Preflight(ToolDefinition tool, ToolMode mode)
        {
            PreflightReport report = new PreflightReport();
            if (tool == null) { report.Add("工具", false, false, "未选择工具"); return report; }

            string directory = _context.ToolDirectory(tool);
            if (!Directory.Exists(directory))
            {
                report.Add("工具目录", false, false, "不存在: " + directory);
                return report;
            }
            report.Add("工具目录", true, false, directory);

            foreach (string required in tool.RequiredFiles)
            {
                string path = Path.Combine(directory, LauncherContext.NormalizeRelative(required));
                bool exists = File.Exists(path) || Directory.Exists(path);
                report.Add("必需文件 " + required, exists, false, exists ? "存在" : "缺失: " + path);
            }

            ToolEntryPoint entry = tool.FindEntry(mode == null ? string.Empty : mode.EntryPointId);
            if (entry == null)
            {
                report.Add("启动入口", false, false, "该模式没有对应入口");
            }
            else
            {
                string exePath = Path.Combine(directory, LauncherContext.NormalizeRelative(entry.ExeRelativePath));
                bool exists = File.Exists(exePath);
                report.Add("启动入口 " + entry.Name, exists, false, exists ? exePath : "缺失: " + exePath);
                if (!entry.Confirmed)
                    report.Add("入口是否已验证", true, true, "文档只间接提到该入口，运行后请人工确认是否出现工具界面");
            }

            if (tool.ConfigKind == ToolConfigKind.Ini || tool.ConfigKind == ToolConfigKind.Json)
            {
                string configPath = _context.ConfigPath(tool);
                if (File.Exists(configPath))
                {
                    try
                    {
                        if (tool.ConfigKind == ToolConfigKind.Json)
                            Config.JsonDocument.Parse(File.ReadAllText(configPath));
                        else
                            Config.IniDocument.Parse(Config.TextFile.Read(configPath).Text);
                        report.Add("配置文件可解析", true, false, Path.GetFileName(configPath));
                    }
                    catch (Exception error)
                    {
                        report.Add("配置文件可解析", false, false, "解析失败: " + error.Message);
                    }
                }
            }

            if (string.Equals(tool.Id, "domooc", StringComparison.OrdinalIgnoreCase))
            {
                AddDomoocChecks(report, directory, mode);
            }

            if (string.Equals(tool.Id, "unipus", StringComparison.OrdinalIgnoreCase))
            {
                string modelCache = Path.Combine(directory, ".model_cache");
                if (!Directory.Exists(modelCache))
                    report.Add("模型缓存目录", true, true, "尚无 " + modelCache + "（首次启动会下载 whisper 模型）");
            }

            if (_context.Supervisor.IsRunning(tool.Id))
                report.Add("重复启动", false, false, "该工具已由启动器启动并在运行，不能重复启动");

            return report;
        }

        private void AddDomoocChecks(PreflightReport report, string directory, ToolMode mode)
        {
            string profile = Path.Combine(directory, "Profile");
            string[] locks = new string[]
            {
                Path.Combine(profile, "parent.lock"),
                Path.Combine(profile, "lock")
            };
            bool anyLock = false;
            foreach (string file in locks)
                if (File.Exists(file)) anyLock = true;

            if (anyLock)
            {
                report.Add("Profile 锁文件", true, true,
                    "检测到 parent.lock / lock：可能已有 Firefox 正在使用该 Profile。" +
                    "启动器不会自动删除（原 .bat 会盲删），请先确认没有实例在运行。");
            }
            else
            {
                report.Add("Profile 锁文件", true, false, "无锁文件");
            }

            string storage = Path.Combine(profile, @"storage\default\https+++domooc.top");
            if (Directory.Exists(storage))
                report.Add("domooc 站点存储目录", true, true,
                    "存在 https+++domooc.top 存储目录（原 .bat 会删除它）；启动器不删，自行判断是否需要清理");

            if (mode != null && mode.StartsAnswerService)
            {
                string server = Path.Combine(directory, @"answer-service\server.js");
                if (!File.Exists(server))
                {
                    report.Add("本地答题服务脚本", false, true, "缺少 " + server + "（该模式只能启动浏览器）");
                }
                else
                {
                    string node = EnvironmentProbe.FindExecutable("node.exe");
                    if (node.Length == 0) report.Add("Node.js", false, true, "未安装 Node.js：本地答题服务无法启动，浏览器仍会启动");
                    else report.Add("Node.js", true, false, node);
                }

                PortProbeResult port = PortProbe.ProbeAnswerService(AnswerServicePort, 400);
                if (port.State == PortState.Foreign)
                    report.Add("端口 " + AnswerServicePort, false, false, port.Detail + " —— 启动器不会结束该进程，请先自行处理");
                else if (port.State == PortState.OurService)
                    report.Add("端口 " + AnswerServicePort, true, false, "已有本机答题服务在运行，将复用");
                else
                    report.Add("端口 " + AnswerServicePort, true, false, "空闲，将启动答题服务");
            }

            if (mode != null && string.Equals(mode.Id, "automation", StringComparison.OrdinalIgnoreCase))
            {
                report.Add("自动化范围", true, true,
                    "该模式只准备浏览器（Marionette 127.0.0.1:2828）与本地服务，启动器不会执行任何刷课/自动化脚本。");
            }
        }

        public string DescribeAnswerService()
        {
            PortProbeResult result = PortProbe.ProbeAnswerService(AnswerServicePort, 400);
            return result.Detail;
        }

        public PortState AnswerServiceState()
        {
            return PortProbe.ProbeAnswerService(AnswerServicePort, 400).State;
        }

        public LaunchResult Launch(ToolDefinition tool, ToolMode mode)
        {
            LaunchResult result = new LaunchResult();
            if (tool == null || mode == null)
            {
                result.Outcome = LaunchOutcome.Failed;
                result.Message = "未选择工具或模式";
                return result;
            }

            string directory = _context.ToolDirectory(tool);
            ToolEntryPoint entry = tool.FindEntry(mode.EntryPointId);
            if (entry == null)
            {
                result.Outcome = LaunchOutcome.Failed;
                result.Message = "找不到入口: " + mode.EntryPointId;
                return result;
            }

            Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            tokens["dir"] = directory;
            tokens["profile"] = Path.Combine(directory, "Profile");

            string exePath = Path.Combine(directory, LauncherContext.NormalizeRelative(entry.ExeRelativePath));
            string[] arguments = ArgumentTemplate.Expand(entry.Arguments, tokens);

            Dictionary<string, string> environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in entry.Environment)
            {
                string[] expanded = ArgumentTemplate.Expand(new string[] { pair.Value }, tokens);
                environment[pair.Key] = expanded.Length > 0 ? expanded[0] : string.Empty;
            }

            string workingDirectory = string.IsNullOrEmpty(entry.WorkingDirectoryRelative)
                ? Path.GetDirectoryName(exePath)
                : Path.Combine(directory, LauncherContext.NormalizeRelative(entry.WorkingDirectoryRelative));

            // duplicate guard: our own record first, then a same-image process we did not start
            LaunchResult existing = new LaunchResult();
            if (_context.Supervisor.IsRunning(tool.Id))
            {
                OwnedProcess owned = _context.Supervisor.GetOwned(tool.Id);
                existing.Outcome = LaunchOutcome.AlreadyRunning;
                existing.ProcessId = owned == null ? 0 : owned.ProcessId;
                existing.Message = "该工具已由启动器启动并在运行（PID " + existing.ProcessId + "）";
                return existing;
            }

            IList<int> foreign = ProcessSupervisor.FindRunningByExecutable(exePath);
            if (foreign.Count > 0)
            {
                existing.Outcome = LaunchOutcome.AlreadyRunning;
                existing.ProcessId = foreign[0];
                existing.Message = "已有一个 " + Path.GetFileName(exePath) + " 在运行（PID " +
                                   JoinPids(foreign) + "），它不是启动器启动的，启动器不会结束它。" +
                                   "如果那是你自己打开的，请手动关闭后再启动。";
                _context.Log.Warn(existing.Message);
                return existing;
            }

            // domooc: bring the local answer service up first (browser still starts if it fails)
            if (mode.StartsAnswerService)
            {
                string serviceNote = EnsureAnswerService(directory);
                _context.Log.Info("domooc 本地答题服务: " + serviceNote);
            }

            // any non-domooc tool: one process, no extra machinery
            LaunchResult launched = _context.Supervisor.Start(
                tool.Id,
                exePath,
                arguments,
                workingDirectory,
                environment,
                null);

            _context.Log.Info(string.Format(
                "启动 {0}（模式 {1}）: {2} | args={3} | cwd={4} | {5}",
                tool.Name, mode.Name, exePath, ArgumentList.Join(arguments), workingDirectory, launched.Message));

            return launched;
        }

        /// <summary>
        /// Idempotent: reuses a verified running service, refuses to touch a foreign listener, and
        /// starts node with a fixed argument ("server.js") in the service folder when the port is free.
        /// </summary>
        public string EnsureAnswerService(string toolDirectory)
        {
            PortProbeResult probe = PortProbe.ProbeAnswerService(AnswerServicePort, 400);
            if (probe.State == PortState.OurService) return "复用已在运行的服务: " + probe.Detail;
            if (probe.State == PortState.Foreign) return "端口被其它程序占用，未启动服务: " + probe.Detail;

            string serviceDirectory = Path.Combine(toolDirectory, "answer-service");
            string server = Path.Combine(serviceDirectory, "server.js");
            if (!File.Exists(server)) return "缺少 server.js，跳过服务";

            string node = EnvironmentProbe.FindExecutable("node.exe");
            if (node.Length == 0) return "未找到 Node.js，跳过服务";

            LaunchResult started = _context.Supervisor.Start(
                "domooc-answer-service",
                node,
                new string[] { "server.js" },
                serviceDirectory,
                null,
                null);

            if (started.Outcome == LaunchOutcome.Failed) return "启动失败: " + started.Message;

            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(250);
                PortProbeResult health = PortProbe.ProbeAnswerService(AnswerServicePort, 300);
                if (health.State == PortState.OurService) return "已启动并通过 /api/health 验证（PID " + started.ProcessId + "）";
            }
            return "进程已启动（PID " + started.ProcessId + "）但 /api/health 在 10 秒内未就绪；浏览器将照常启动";
        }

        private static string JoinPids(System.Collections.Generic.IList<int> pids)
        {
            if (pids == null || pids.Count == 0) return string.Empty;
            string[] parts = new string[pids.Count];
            for (int i = 0; i < pids.Count; i++) parts[i] = pids[i].ToString();
            return string.Join(", ", parts);
        }

        public StopResult StopTool(ToolDefinition tool, bool force)
        {
            string toolId = tool == null ? string.Empty : tool.Id;
            StopResult result = force
                ? _context.Supervisor.ForceStop(toolId)
                : _context.Supervisor.RequestStop(toolId, 5000);
            _context.Log.Info(string.Format("停止 {0}（{1}）: {2}", toolId, force ? "强制" : "正常", result.Message));
            return result;
        }

        public void StopAnswerServiceIfOwned()
        {
            if (_context.Supervisor.IsRunning("domooc-answer-service"))
            {
                StopResult result = _context.Supervisor.RequestStop("domooc-answer-service", 3000);
                _context.Log.Info("停止本地答题服务: " + result.Message);
            }
        }
    }
}

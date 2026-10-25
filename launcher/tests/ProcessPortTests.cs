using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TimeSaver.Core;
using TimeSaver.Model;

namespace TimeSaver.Tests
{
    /// <summary>
    /// Dispatch-level verification with controlled helper processes (this executable in
    /// "--sleep" mode). No real tool is started, so nothing can touch a login, a course or a
    /// model request.
    /// </summary>
    public static class ProcessAndPortTests
    {
        public static void Run(Runner runner)
        {
            ProcessOwnership(runner);
            ForeignProcessUntouched(runner);
            PortIdentity(runner);
            Catalog(runner);
        }

        private static string SelfPath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        private static void ProcessOwnership(Runner runner)
        {
            runner.Section("进程所有权 / 防重复 / 安全退出");

            ProcessSupervisor supervisor = new ProcessSupervisor();
            string workDirectory = Temp.NewDirectory("proc");
            string self = SelfPath();

            LaunchResult started = supervisor.Start(
                "helper", self, new string[] { "--sleep", "30000" }, workDirectory, null, null);

            runner.Equal("启动自己启动的辅助进程", "Started", started.Outcome.ToString());
            runner.True("记录包含 PID", started.ProcessId > 0, "pid=" + started.ProcessId);
            runner.True("查询为运行中", supervisor.IsRunning("helper"), "IsRunning=false");
            runner.True("GetOwned 返回进程记录", supervisor.GetOwned("helper") != null, "null");

            LaunchResult duplicate = supervisor.Start(
                "helper", self, new string[] { "--sleep", "30000" }, workDirectory, null, null);
            runner.Equal("同一工具重复启动被拒绝", "AlreadyRunning", duplicate.Outcome.ToString());

            LaunchResult otherTool = supervisor.Start(
                "helper-2", self, new string[] { "--sleep", "30000" }, workDirectory, null, null);
            runner.Equal("不同工具可以并行启动", "Started", otherTool.Outcome.ToString());

            LaunchResult missing = supervisor.Start(
                "missing", Path.Combine(workDirectory, "does-not-exist.exe"), null, workDirectory, null, null);
            runner.Equal("可执行文件缺失时失败", "Failed", missing.Outcome.ToString());
            runner.True("失败信息包含路径", missing.Message.IndexOf("does-not-exist", StringComparison.Ordinal) >= 0,
                missing.Message);

            StopResult graceful = supervisor.RequestStop("helper", 400);
            runner.True("控制台进程无法优雅关闭（返回 StillRunning）或已退出",
                graceful.Outcome == StopOutcome.StillRunning || graceful.Outcome == StopOutcome.ExitedGracefully,
                graceful.Outcome + ": " + graceful.Message);
            runner.True("未退出时仍被认为拥有该进程", supervisor.IsRunning("helper") || graceful.Outcome == StopOutcome.ExitedGracefully,
                "IsRunning=false 但进程应仍在");

            StopResult forced = supervisor.ForceStop("helper");
            runner.Equal("强制结束只作用于自己那个进程", "ForceKilled", forced.Outcome.ToString());
            runner.True("强制结束后不再运行", !supervisor.IsRunning("helper"), "IsRunning=true");

            StopResult notOwned = supervisor.RequestStop("never-started", 100);
            runner.Equal("未启动过的工具返回 NotOwned", "NotOwned", notOwned.Outcome.ToString());
            StopResult forceNotOwned = supervisor.ForceStop("never-started");
            runner.Equal("未启动过的工具拒绝强制结束", "NotOwned", forceNotOwned.Outcome.ToString());

            StopResult second = supervisor.ForceStop("helper-2");
            runner.Equal("第二个进程同样只被强制结束一次", "ForceKilled", second.Outcome.ToString());
            Thread.Sleep(200);
        }

        private static void ForeignProcessUntouched(Runner runner)
        {
            runner.Section("外部实例：只提示，不接管、不结束");

            string self = SelfPath();
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = self;
            info.Arguments = ArgumentList.Join(new string[] { "--sleep", "25000" });
            info.UseShellExecute = false;
            info.CreateNoWindow = true;

            Process foreign = null;
            try
            {
                foreign = Process.Start(info);
                runner.True("外部辅助进程启动成功", foreign != null, "null");
                Thread.Sleep(300);

                IList<int> byPath = ProcessSupervisor.FindRunningByExecutable(self);
                runner.True("按镜像路径能发现外部实例", byPath.Count >= 1, "count=" + byPath.Count);
                bool contains = false;
                for (int i = 0; i < byPath.Count; i++) if (byPath[i] == foreign.Id) contains = true;
                string pidList = string.Empty;
                for (int i = 0; i < byPath.Count; i++)
                    pidList += (i == 0 ? string.Empty : ",") + byPath[i];
                runner.True("发现结果包含该 PID", contains, "pids=" + pidList);

                ProcessSupervisor supervisor = new ProcessSupervisor();
                runner.True("外部实例不在启动器记录里", supervisor.GetOwned("foreign") == null, "GetOwned 不为 null");
                StopResult attempt = supervisor.RequestStop("foreign", 100);
                runner.Equal("对外部实例返回 NotOwned", "NotOwned", attempt.Outcome.ToString());
                runner.True("外部实例仍在运行（没有被结束）", !foreign.HasExited, "HasExited=true");
            }
            finally
            {
                if (foreign != null)
                {
                    try { if (!foreign.HasExited) foreign.Kill(); } catch { }
                    try { foreign.Dispose(); } catch { }
                }
            }
        }

        private static void PortIdentity(Runner runner)
        {
            runner.Section("端口身份识别（不按端口杀进程）");

            PortProbeResult closed = PortProbe.ProbeAnswerService(FindFreePort(), 300);
            runner.Equal("空闲端口判定为 Closed", "Closed", closed.State.ToString());

            using (FakeHttpServer ours = new FakeHttpServer(
                "{\"ok\":true,\"mode\":\"local-only\",\"bank\":3,\"port\":8791}", 200))
            {
                PortProbeResult result = PortProbe.ProbeAnswerService(ours.Port, 500);
                runner.Equal("domooc 健康体判定为 OurService", "OurService", result.State.ToString());
                runner.True("识别细节含 local-only", result.Detail.IndexOf("local-only", StringComparison.Ordinal) >= 0,
                    result.Detail);
            }

            using (FakeHttpServer foreign = new FakeHttpServer("{\"hello\":\"world\"}", 200))
            {
                PortProbeResult result = PortProbe.ProbeAnswerService(foreign.Port, 500);
                runner.Equal("其它 HTTP 服务判定为 Foreign", "Foreign", result.State.ToString());
                runner.True("冲突信息明确说明未识别", result.Detail.IndexOf("其它程序", StringComparison.Ordinal) >= 0,
                    result.Detail);
            }

            using (FakeHttpServer unauthorized = new FakeHttpServer("{\"error\":\"denied\"}", 401))
            {
                PortProbeResult result = PortProbe.ProbeAnswerService(unauthorized.Port, 500);
                runner.Equal("401 服务判定为 Foreign", "Foreign", result.State.ToString());
            }
        }

        private static void Catalog(Runner runner)
        {
            runner.Section("工具清单与入口证据");

            PathResolver paths = PathResolver.ForRoot(Temp.NewDirectory("catalog"));
            ToolCatalog catalog = ToolCatalog.CreateDefault(paths);

            runner.EqualInt("内置 4 个工具", 4, catalog.Tools.Count);
            runner.True("包含 autovisor", catalog.Find("autovisor") != null, "未找到");
            runner.True("包含 unipus", catalog.Find("unipus") != null, "未找到");
            runner.True("包含 domooc", catalog.Find("domooc") != null, "未找到");
            runner.True("包含 xuexitong", catalog.Find("xuexitong") != null, "未找到");

            ToolDefinition unipus = catalog.Find("unipus");
            ToolEntryPoint unipusEntry = unipus.Entries[0];
            runner.True("Unipus 带 HF_HOME", unipusEntry.Environment.ContainsKey("HF_HOME"), "缺 HF_HOME");
            runner.True("Unipus 带 HF_ENDPOINT", unipusEntry.Environment.ContainsKey("HF_ENDPOINT"), "缺 HF_ENDPOINT");
            runner.Equal("HF_ENDPOINT 指向镜像", "https://hf-mirror.com", unipusEntry.Environment["HF_ENDPOINT"]);

            ToolDefinition domooc = catalog.Find("domooc");
            runner.EqualInt("domooc 三种模式", 3, domooc.Modes.Count);
            runner.True("domooc 含自动化模式", domooc.FindEntry("browser-automation") != null, "缺入口");
            runner.True("自动化模式带 -marionette",
                Array.IndexOf(domooc.FindEntry("browser-automation").Arguments, "-marionette") >= 0, "缺参数");
            runner.True("domooc 要求 user.js（远程通道检查）",
                Array.IndexOf(domooc.RequiredFiles, @"Profile\user.js") >= 0, "未要求 user.js");

            ToolDefinition xuexitong = catalog.Find("xuexitong");
            runner.True("xuexitong 目录保留字面量 exe)", xuexitong.Directory.IndexOf("exe)") >= 0, xuexitong.Directory);
            runner.True("xuexitong 有两个候选入口", xuexitong.Entries.Count == 2, "count=" + xuexitong.Entries.Count);
            bool anyUnconfirmed = false;
            foreach (ToolEntryPoint entry in xuexitong.Entries) if (!entry.Confirmed) anyUnconfirmed = true;
            runner.True("xuexitong 入口标记为未验证", anyUnconfirmed, "没有任何未验证入口");
            runner.True("xuexitong 有警告说明", xuexitong.Warnings.Count > 0, "Warnings=0");
            runner.True("入口证据引用说明文件",
                xuexitong.FindEntry("start").Evidence.IndexOf("Help.txt", StringComparison.Ordinal) >= 0,
                xuexitong.FindEntry("start").Evidence);
        }

        private static int FindFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>Minimal TCP server that answers every request with a fixed HTTP response.</summary>
        private sealed class FakeHttpServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _thread;
            private readonly string _body;
            private readonly int _status;
            private volatile bool _stop;

            public FakeHttpServer(string body, int status)
            {
                _body = body ?? string.Empty;
                _status = status;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

                _thread = new Thread(Loop);
                _thread.IsBackground = true;
                _thread.Start();
            }

            public int Port { get; private set; }

            private void Loop()
            {
                while (!_stop)
                {
                    TcpClient client = null;
                    try
                    {
                        client = _listener.AcceptTcpClient();
                        using (NetworkStream stream = client.GetStream())
                        {
                            stream.ReadTimeout = 1000;
                            byte[] buffer = new byte[4096];
                            try { stream.Read(buffer, 0, buffer.Length); }
                            catch (IOException) { }

                            byte[] body = Encoding.UTF8.GetBytes(_body);
                            string head = "HTTP/1.1 " + _status + " X\r\n" +
                                          "Content-Type: application/json\r\n" +
                                          "Content-Length: " + body.Length + "\r\n" +
                                          "Connection: close\r\n\r\n";
                            byte[] headBytes = Encoding.ASCII.GetBytes(head);
                            stream.Write(headBytes, 0, headBytes.Length);
                            stream.Write(body, 0, body.Length);
                            stream.Flush();
                        }
                    }
                    catch
                    {
                        // ignore and keep serving
                    }
                    finally
                    {
                        if (client != null) { try { client.Close(); } catch { } }
                    }
                }
            }

            public void Dispose()
            {
                _stop = true;
                try { _listener.Stop(); } catch { }
                try { _thread.Join(1000); } catch { }
            }
        }
    }
}

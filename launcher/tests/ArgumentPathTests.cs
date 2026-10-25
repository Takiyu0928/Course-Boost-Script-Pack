using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using TimeSaver.Core;
using TimeSaver.Ui;

namespace TimeSaver.Tests
{
    /// <summary>
    /// Proves the Windows argument quoting is correct - not by eyeballing strings, but by launching
    /// this very test executable with the quoted command line and comparing what the child actually
    /// received. .NET Framework 4.8 has no ProcessStartInfo.ArgumentList, so this is the only thing
    /// standing between a path like "…\Example User's PC\…\exe)" and a broken launch.
    /// </summary>
    public static class ArgumentAndPathTests
    {
        public static void Run(Runner runner)
        {
            runner.Section("参数引用 (ArgumentList)");

            runner.Equal("普通参数不加引号", "plain", ArgumentList.Quote("plain"));
            runner.Equal("空参数写成 \"\"", "\"\"", ArgumentList.Quote(""));
            runner.Equal("含空格加引号", "\"a b\"", ArgumentList.Quote("a b"));
            runner.Equal("单引号不需要处理", "O'Brien", ArgumentList.Quote("O'Brien"));
            runner.Equal("中文加引号", "\"课程 1\"", ArgumentList.Quote("课程 1"));
            runner.Equal("内部引号转义", "\"a\\\"b\"", ArgumentList.Quote("a\"b"));
            runner.Equal("无空格时不加引号（尾随反斜杠原样）", "a\\", ArgumentList.Quote("a\\"));
            runner.Equal("需要引号时尾随反斜杠加倍", "\"a b\\\\\"", ArgumentList.Quote("a b\\"));
            runner.Equal("引号前反斜杠加倍", "\"a\\\\\\\"b\"", ArgumentList.Quote("a\\\"b"));
            runner.Equal("无空格的字面量 exe) 路径不加引号", "C:\\x\\exe)\\start.exe",
                ArgumentList.Quote("C:\\x\\exe)\\start.exe"));
            runner.Equal("含空格的 exe) 路径加引号", "\"C:\\a b\\exe)\\start.exe\"",
                ArgumentList.Quote("C:\\a b\\exe)\\start.exe"));

            string joined = ArgumentList.Join(new string[] { "-profile", "C:\\a b\\Profile", "-no-remote" });
            runner.Equal("Join 组合", "-profile \"C:\\a b\\Profile\" -no-remote", joined);

            // real round-trip through CreateProcess
            string self = Process.GetCurrentProcess().MainModule.FileName;
            string[] cases = new string[]
            {
                "plain",
                "with space",
                "Example User's PC",
                "C:\\Users\\Example User's PC\\Downloads\\time saver\\xuexitong_shuake26.10\\exe)\\start.exe",
                "中文 路径 测试",
                "quote\"inside",
                "trailing\\",
                "back\\slash\"quote",
                "",
                "tab\tinside"
            };

            List<string> arguments = new List<string>();
            arguments.Add("--echo-args");
            for (int i = 0; i < cases.Length; i++) arguments.Add(cases[i]);

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = self;
            info.Arguments = ArgumentList.Join(arguments);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);

            int roundTripOk = 0;
            string failure = string.Empty;
            using (Process process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);
                string[] lines = output.Replace("\r\n", "\n").Split('\n');
                List<string> received = new List<string>();
                foreach (string line in lines)
                {
                    if (!line.StartsWith("ARG=", StringComparison.Ordinal)) continue;
                    string base64 = line.Substring(4);
                    try { received.Add(Encoding.UTF8.GetString(Convert.FromBase64String(base64))); }
                    catch (Exception error) { failure = "无法解码子进程输出: " + error.Message; }
                }

                if (failure.Length == 0 && received.Count != cases.Length)
                    failure = "参数数量不符: 期望 " + cases.Length + " 实际 " + received.Count;

                if (failure.Length == 0)
                {
                    for (int i = 0; i < cases.Length; i++)
                    {
                        if (string.Equals(cases[i], received[i], StringComparison.Ordinal)) roundTripOk++;
                        else failure = "第 " + i + " 个参数不一致: 期望 [" + cases[i] + "] 实际 [" + received[i] + "]";
                    }
                }
            }

            runner.EqualInt("参数往返（真实进程）全部一致", cases.Length, roundTripOk);
            runner.True("参数往返没有差异", failure.Length == 0, failure);

            runner.Section("路径解析与令牌展开");

            string root = Temp.NewDirectory("root with space and Example User's PC");
            PathResolver paths = PathResolver.ForRoot(root);
            runner.True("安装根目录存在", Directory.Exists(paths.InstallRoot), paths.InstallRoot);

            string nested = paths.Combine("xuexitong_shuake26.10", "exe)", "start.exe");
            runner.Equal("字面量 exe) 拼接正确",
                Path.Combine(root, "xuexitong_shuake26.10", "exe)", "start.exe"), nested);
            runner.True("拼接结果在根目录内", paths.IsInsideRoot(nested), nested);
            runner.True(".. 逃逸被拒绝", !paths.IsInsideRoot(Path.Combine(root, "..", "outside.txt")), "IsInsideRoot 应返回 false");

            paths.EnsureDirectories();
            runner.True("launcher 目录已创建", Directory.Exists(paths.LauncherDirectory), paths.LauncherDirectory);
            runner.True("logs 目录已创建", Directory.Exists(paths.LogsDirectory), paths.LogsDirectory);
            runner.True("backups 目录已创建", Directory.Exists(paths.BackupsDirectory), paths.BackupsDirectory);

            Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            tokens["dir"] = "C:\\x\\domooc_free";
            tokens["profile"] = "C:\\x\\domooc_free\\Profile";
            string[] expanded = ArgumentTemplate.Expand(
                new string[] { "-profile", "{profile}", "-no-remote" }, tokens);
            runner.EqualInt("令牌展开后参数个数不变", 3, expanded.Length);
            runner.Equal("令牌 {profile} 展开", "C:\\x\\domooc_free\\Profile", expanded[1]);
            runner.Equal("未知令牌保持原样", "{unknown}", ArgumentTemplate.Expand(new string[] { "{unknown}" }, tokens)[0]);

            runner.Section("窗口状态持久化");

            string stateFile = Path.Combine(root, "state.json");
            WindowStateStore store = new WindowStateStore();
            store.Left = 120;
            store.Top = 80;
            store.Width = 1280;
            store.Height = 800;
            store.Maximized = true;
            store.CloseToTray = false;
            store.Save(stateFile);

            WindowStateStore loaded = WindowStateStore.Load(stateFile);
            runner.EqualDouble("窗口 left 往返", 120, loaded.Left, 0.01);
            runner.EqualDouble("窗口 width 往返", 1280, loaded.Width, 0.01);
            runner.True("最大化状态往返", loaded.Maximized, "Maximized=" + loaded.Maximized);
            runner.True("关闭行为往返", !loaded.CloseToTray, "CloseToTray=" + loaded.CloseToTray);

            File.WriteAllText(stateFile, "{ 这不是 JSON", new UTF8Encoding(false));
            WindowStateStore broken = WindowStateStore.Load(stateFile);
            runner.EqualDouble("损坏的 state.json 回退默认宽度", 1100, broken.Width, 0.01);
        }
    }
}

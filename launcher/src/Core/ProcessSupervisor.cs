using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TimeSaver.Core
{
    public enum LaunchOutcome
    {
        Started,
        AlreadyRunning,
        Failed
    }

    public sealed class LaunchResult
    {
        public LaunchOutcome Outcome;
        public int ProcessId;
        public string Message = string.Empty;
    }

    public enum StopOutcome
    {
        NotOwned,
        ExitedGracefully,
        StillRunning,
        ForceKilled,
        Failed
    }

    public sealed class StopResult
    {
        public StopOutcome Outcome;
        public string Message = string.Empty;
    }

    /// <summary>One process this launcher started, identified by pid **and** start time (pid reuse safe).</summary>
    public sealed class OwnedProcess
    {
        public string ToolId;
        public string ExePath;
        public int ProcessId;
        public DateTime StartTimeUtc;
        public DateTime StartedAtUtc;

        public bool Matches(Process process)
        {
            if (process == null) return false;
            try
            {
                if (process.Id != ProcessId) return false;
                return process.StartTime.ToUniversalTime() == StartTimeUtc;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Starts and stops tool processes with three hard rules:
    ///
    ///  1. No shell. The executable path and its arguments are passed as an explicit list; the
    ///     command line is built only by ArgumentList.Join (never by concatenating user text into
    ///     "cmd /c ...").
    ///  2. Ownership. Only processes this launcher started are ever stopped; ownership is
    ///     re-verified with pid + start time so a recycled pid is not mistaken for ours.
    ///  3. Graceful first. RequestStop asks the window to close and waits; ForceStop exists but is
    ///     only reachable through an explicit user action, and it only affects the single process
    ///     we own (.NET Framework has no Kill(entireProcessTree), and child processes such as a
    ///     browser started by a tool are deliberately left to the user).
    /// </summary>
    public sealed class ProcessSupervisor
    {
        private readonly Dictionary<string, OwnedProcess> _owned =
            new Dictionary<string, OwnedProcess>(StringComparer.OrdinalIgnoreCase);

        public OwnedProcess GetOwned(string toolId)
        {
            OwnedProcess owned;
            if (!_owned.TryGetValue(toolId, out owned)) return null;
            if (IsStillRunning(owned)) return owned;
            _owned.Remove(toolId);
            return null;
        }

        public bool IsRunning(string toolId)
        {
            return GetOwned(toolId) != null;
        }

        public IList<OwnedProcess> Snapshot()
        {
            List<OwnedProcess> list = new List<OwnedProcess>();
            foreach (KeyValuePair<string, OwnedProcess> pair in _owned)
                if (IsStillRunning(pair.Value)) list.Add(pair.Value);
            return list;
        }

        private static bool IsStillRunning(OwnedProcess owned)
        {
            if (owned == null) return false;
            try
            {
                Process process = Process.GetProcessById(owned.ProcessId);
                if (process == null) return false;
                if (!owned.Matches(process)) return false;
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false; // no such process
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public LaunchResult Start(
            string toolId,
            string exePath,
            IEnumerable<string> arguments,
            string workingDirectory,
            IDictionary<string, string> environment,
            string expectedProcessNameHint)
        {
            if (string.IsNullOrEmpty(toolId)) throw new ArgumentNullException("toolId");
            if (string.IsNullOrEmpty(exePath)) throw new ArgumentNullException("exePath");

            OwnedProcess existing = GetOwned(toolId);
            if (existing != null)
            {
                LaunchResult duplicate = new LaunchResult();
                duplicate.Outcome = LaunchOutcome.AlreadyRunning;
                duplicate.ProcessId = existing.ProcessId;
                duplicate.Message = "该工具已由启动器启动并在运行（PID " + existing.ProcessId + "）";
                return duplicate;
            }

            LaunchResult result = new LaunchResult();
            if (!File.Exists(exePath))
            {
                result.Outcome = LaunchOutcome.Failed;
                result.Message = "找不到可执行文件: " + exePath;
                return result;
            }
            if (!string.IsNullOrEmpty(workingDirectory) && !Directory.Exists(workingDirectory))
            {
                result.Outcome = LaunchOutcome.Failed;
                result.Message = "找不到工作目录: " + workingDirectory;
                return result;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = exePath;
            startInfo.Arguments = ArgumentList.Join(arguments ?? new string[0]);
            startInfo.WorkingDirectory = string.IsNullOrEmpty(workingDirectory)
                ? Path.GetDirectoryName(exePath)
                : workingDirectory;
            startInfo.UseShellExecute = false;   // never route through cmd.exe / explorer
            startInfo.CreateNoWindow = false;    // console tools must keep their own console window
            startInfo.WindowStyle = ProcessWindowStyle.Normal;

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment)
                {
                    if (pair.Key == null) continue;
                    startInfo.EnvironmentVariables[pair.Key] = pair.Value ?? string.Empty;
                }
            }

            try
            {
                Process process = Process.Start(startInfo);
                if (process == null)
                {
                    result.Outcome = LaunchOutcome.Failed;
                    result.Message = "进程未能启动（Process.Start 返回 null）";
                    return result;
                }

                OwnedProcess owned = new OwnedProcess();
                owned.ToolId = toolId;
                owned.ExePath = exePath;
                owned.ProcessId = process.Id;
                try { owned.StartTimeUtc = process.StartTime.ToUniversalTime(); }
                catch { owned.StartTimeUtc = DateTime.UtcNow; }
                owned.StartedAtUtc = DateTime.UtcNow;
                _owned[toolId] = owned;

                if (!string.IsNullOrEmpty(expectedProcessNameHint))
                {
                    string actual;
                    try { actual = process.ProcessName; }
                    catch { actual = string.Empty; }
                    if (actual.Length > 0 &&
                        !string.Equals(actual, expectedProcessNameHint, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Outcome = LaunchOutcome.Started;
                        result.ProcessId = owned.ProcessId;
                        result.Message = "已启动 PID " + owned.ProcessId + "，但进程名是 " + actual +
                                         "（预期 " + expectedProcessNameHint + ")，请确认该入口是否正确";
                        return result;
                    }
                }

                result.Outcome = LaunchOutcome.Started;
                result.ProcessId = owned.ProcessId;
                result.Message = "已启动 PID " + owned.ProcessId;
                return result;
            }
            catch (Exception error)
            {
                result.Outcome = LaunchOutcome.Failed;
                result.Message = "启动失败: " + error.Message;
                return result;
            }
        }

        /// <summary>Ask the owned process to close (CloseMainWindow) and wait for the grace period.</summary>
        public StopResult RequestStop(string toolId, int graceMilliseconds)
        {
            StopResult result = new StopResult();
            OwnedProcess owned = GetOwned(toolId);
            if (owned == null)
            {
                result.Outcome = StopOutcome.NotOwned;
                result.Message = "该工具不是由启动器启动的，启动器不会结束它";
                return result;
            }

            Process process;
            try { process = Process.GetProcessById(owned.ProcessId); }
            catch
            {
                _owned.Remove(toolId);
                result.Outcome = StopOutcome.ExitedGracefully;
                result.Message = "进程已经结束";
                return result;
            }

            if (!owned.Matches(process))
            {
                _owned.Remove(toolId);
                result.Outcome = StopOutcome.NotOwned;
                result.Message = "PID " + owned.ProcessId + " 已不是启动器启动的那个进程，未做任何操作";
                return result;
            }

            bool signalled = false;
            try { signalled = process.CloseMainWindow(); }
            catch { signalled = false; }

            if (!signalled)
            {
                result.Outcome = StopOutcome.StillRunning;
                result.Message = "该进程没有可关闭的窗口（控制台/后台程序），需要你手动关闭，或使用“强制结束”";
                return result;
            }

            try
            {
                if (process.WaitForExit(graceMilliseconds))
                {
                    _owned.Remove(toolId);
                    result.Outcome = StopOutcome.ExitedGracefully;
                    result.Message = "已请求退出，进程已结束";
                    return result;
                }
            }
            catch (Exception error)
            {
                result.Outcome = StopOutcome.Failed;
                result.Message = "等待退出时出错: " + error.Message;
                return result;
            }

            result.Outcome = StopOutcome.StillRunning;
            result.Message = "已请求关闭，但 " + graceMilliseconds + " ms 内没有退出";
            return result;
        }

        /// <summary>Explicit user action only. Kills the single owned process; never a tree, never a stranger.</summary>
        public StopResult ForceStop(string toolId)
        {
            StopResult result = new StopResult();
            OwnedProcess owned = GetOwned(toolId);
            if (owned == null)
            {
                result.Outcome = StopOutcome.NotOwned;
                result.Message = "该工具不是由启动器启动的，拒绝强制结束";
                return result;
            }

            try
            {
                Process process = Process.GetProcessById(owned.ProcessId);
                if (!owned.Matches(process))
                {
                    _owned.Remove(toolId);
                    result.Outcome = StopOutcome.NotOwned;
                    result.Message = "PID 已变化，未做任何操作";
                    return result;
                }
                process.Kill();
                process.WaitForExit(5000);
                _owned.Remove(toolId);
                result.Outcome = StopOutcome.ForceKilled;
                result.Message = "已强制结束 PID " + owned.ProcessId + "（其子进程不会被结束）";
                return result;
            }
            catch (Exception error)
            {
                result.Outcome = StopOutcome.Failed;
                result.Message = "强制结束失败: " + error.Message;
                return result;
            }
        }

        public void Forget(string toolId)
        {
            _owned.Remove(toolId);
        }

        /// <summary>
        /// Finds processes running from the same executable that this launcher did NOT start.
        ///
        /// Used only to warn about a duplicate launch: the launcher never stops what it does not
        /// own. Matching is by full image path, so a same-named program elsewhere is ignored.
        /// </summary>
        public static IList<int> FindRunningByExecutable(string exePath)
        {
            List<int> found = new List<int>();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return found;

            string full;
            try { full = Path.GetFullPath(exePath); }
            catch { return found; }

            string name = Path.GetFileNameWithoutExtension(full);
            Process[] candidates;
            try { candidates = Process.GetProcessesByName(name); }
            catch { return found; }

            for (int i = 0; i < candidates.Length; i++)
            {
                Process process = candidates[i];
                try
                {
                    string module = process.MainModule == null ? null : process.MainModule.FileName;
                    if (string.IsNullOrEmpty(module)) continue;
                    if (string.Equals(Path.GetFullPath(module), full, StringComparison.OrdinalIgnoreCase))
                        found.Add(process.Id);
                }
                catch
                {
                    // access denied for other users' processes: skip rather than guess
                }
            }
            return found;
        }
    }
}

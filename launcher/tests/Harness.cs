using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TimeSaver.Tests
{
    public sealed class Runner
    {
        private int _pass;
        private int _fail;
        private readonly List<string> _failures = new List<string>();
        private string _section = string.Empty;

        public void Section(string name)
        {
            _section = name;
            Console.WriteLine();
            Console.WriteLine("[" + name + "]");
        }

        public void True(string name, bool condition, string detail)
        {
            if (condition)
            {
                _pass++;
                Console.WriteLine("  PASS " + name);
            }
            else
            {
                _fail++;
                string text = "  FAIL " + name + (string.IsNullOrEmpty(detail) ? string.Empty : "  -> " + detail);
                Console.WriteLine(text);
                _failures.Add(_section + " / " + name + " -> " + detail);
            }
        }

        public void True(string name, bool condition) { True(name, condition, string.Empty); }

        public void Equal(string name, string expected, string actual)
        {
            bool ok = string.Equals(expected, actual, StringComparison.Ordinal);
            True(name, ok, ok ? string.Empty : "expected=[" + expected + "] actual=[" + actual + "]");
        }

        public void EqualInt(string name, int expected, int actual)
        {
            True(name, expected == actual, "expected=" + expected + " actual=" + actual);
        }

        public void EqualDouble(string name, double expected, double actual, double tolerance)
        {
            bool ok = Math.Abs(expected - actual) <= tolerance;
            True(name, ok, "expected=" + expected + " actual=" + actual);
        }

        public void Throws(string name, Action action)
        {
            try
            {
                action();
                True(name, false, "没有抛出异常");
            }
            catch
            {
                True(name, true);
            }
        }

        public void NoThrow(string name, Action action)
        {
            try
            {
                action();
                True(name, true);
            }
            catch (Exception error)
            {
                True(name, false, error.GetType().Name + ": " + error.Message);
            }
        }

        public int Summarize()
        {
            Console.WriteLine();
            Console.WriteLine("==============================================");
            Console.WriteLine("  PASS " + _pass + "    FAIL " + _fail);
            if (_fail > 0)
            {
                Console.WriteLine("  失败详情:");
                foreach (string failure in _failures) Console.WriteLine("   - " + failure);
            }
            Console.WriteLine("==============================================");
            return _fail == 0 ? 0 : 1;
        }
    }

    /// <summary>Temporary folders for synthetic fixtures. The real tool folders are never touched.</summary>
    public static class Temp
    {
        private static readonly List<string> Created = new List<string>();

        public static string NewDirectory(string tag)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "TimeSaverTests-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(path);
            Created.Add(path);
            return path;
        }

        public static void CleanupAll()
        {
            foreach (string directory in Created)
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch { }
            }
            Created.Clear();
        }
    }

    public static class Harness
    {
        /// <summary>
        /// Entry point.
        ///   --echo-args [args...]  prints each received argument as base64(UTF-8), used to verify
        ///                          that ArgumentList round-trips through a real process launch.
        ///   --sleep &lt;ms&gt;         long-running child process used by the ownership tests.
        ///   (no argument)          runs the test suite.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--ui-selftest")
            {
                Console.OutputEncoding = new UTF8Encoding(false);
                return UiSmoke.Run();
            }

            if (args.Length >= 1 && args[0] == "--echo-args")
            {
                Console.OutputEncoding = new UTF8Encoding(false);
                for (int i = 1; i < args.Length; i++)
                {
                    string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(args[i] ?? string.Empty));
                    Console.WriteLine("ARG=" + encoded);
                }
                Console.WriteLine("COUNT=" + (args.Length - 1));
                Console.Out.Flush();
                return 0;
            }

            if (args.Length >= 2 && args[0] == "--sleep")
            {
                int milliseconds;
                if (int.TryParse(args[1], out milliseconds)) Thread.Sleep(milliseconds);
                return 0;
            }

            Console.OutputEncoding = new UTF8Encoding(false);
            Runner runner = new Runner();
            Console.WriteLine("Time Saver launcher tests");
            Console.WriteLine("working directory: " + Environment.CurrentDirectory);

            try
            {
                ArgumentAndPathTests.Run(runner);
                ConfigTests.Run(runner);
                ProcessAndPortTests.Run(runner);
                RegressionTests.Run(runner);
            }
            catch (Exception error)
            {
                runner.Section("未捕获异常");
                runner.True("测试运行器没有崩溃", false, error.GetType().Name + ": " + error.Message + " @ " + error.StackTrace);
            }
            finally
            {
                Temp.CleanupAll();
            }

            return runner.Summarize();
        }
    }
}

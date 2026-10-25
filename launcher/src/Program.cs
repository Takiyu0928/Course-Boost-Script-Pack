using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TimeSaver.App;
using TimeSaver.Core;
using TimeSaver.Diagnostics;
using TimeSaver.Ui;

namespace TimeSaver
{
    /// <summary>
    /// Entry point. No administrator rights, no registry/service changes, no single global hook:
    /// the launcher only starts the tools the user already has and edits their config files after
    /// an explicit save.
    /// </summary>
    public static class Program
    {
        private const string MutexName = @"Local\TimeSaver.Launcher.SingleInstance";

        [STAThread]
        public static int Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Time Saver 已经在运行（看任务栏或托盘）。", "Time Saver",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return 0;
                }

                PathResolver paths;
                try
                {
                    paths = PathResolver.FromExecutableDirectory();
                }
                catch (Exception error)
                {
                    MessageBox.Show("无法确定程序目录: " + error.Message, "Time Saver",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return 2;
                }

                LauncherContext context;
                try
                {
                    context = LauncherContext.Create(paths);
                }
                catch (Exception error)
                {
                    MessageBox.Show("初始化失败: " + error.Message, "Time Saver",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return 3;
                }

                Application application = new Application();
                application.ShutdownMode = ShutdownMode.OnMainWindowClose;
                application.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                {
                    context.Log.Error("未处理的界面异常: " + e.Exception.Message + " @ " + e.Exception.StackTrace);
                    MessageBox.Show("发生未处理的错误（已写入日志）:\n" + e.Exception.Message,
                        "Time Saver", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };

                WindowStateStore state = WindowStateStore.Load(paths.StateFile);
                MainWindow window = new MainWindow(context, state, paths.StateFile);
                application.Run(window);
                context.Log.Info("启动器结束");
                return 0;
            }
        }
    }
}

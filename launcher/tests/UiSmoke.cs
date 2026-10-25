using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimeSaver.App;
using TimeSaver.Core;
using TimeSaver.Ui;

namespace TimeSaver.Tests
{
    /// <summary>
    /// UI smoke test without a human looking at the screen: the window is created off-screen,
    /// laid out, and the tab strip / action buttons are inspected through the visual tree.
    ///
    /// It verifies the two review points that were raised: "设置" must be reachable as its own tab
    /// (not buried under long explanatory text), and 正常停止 / 强制结束 must be disabled while the
    /// tool is not running.
    /// </summary>
    public static class UiSmoke
    {
        public static int Run()
        {
            Runner runner = new Runner();
            runner.Section("界面契约（离屏渲染，不需要人工看点）");

            string root = Temp.NewDirectory("uiroot");
            PathResolver paths = PathResolver.ForRoot(root);
            LauncherContext context = LauncherContext.Create(paths);

            WindowStateStore state = new WindowStateStore();
            state.CloseToTray = false; // let Close() really close during the test

            MainWindow.SuppressTrayForTests = true; // 不创建托盘图标：测试不产生任何桌面可见元素

            MainWindow window = new MainWindow(context, state, Path.Combine(root, "state.json"));
            window.Width = 1200;
            window.Height = 820;

            try
            {
                // 只做测量/布局，不 Show()：不打开窗口、不抢焦点
                FrameworkElement content = OffscreenUi.Layout(window);

                List<TabItem> tabs = new List<TabItem>();
                List<Button> buttons = new List<Button>();
                List<TextBlock> texts = new List<TextBlock>();
                Collect(content, tabs, buttons, texts);

                List<string> headers = new List<string>();
                for (int i = 0; i < tabs.Count; i++) headers.Add(Convert.ToString(tabs[i].Header));
                runner.True("存在 5 个页签", tabs.Count == 5, "实际 " + tabs.Count + ": " + string.Join("/", headers.ToArray()));
                runner.True("页签包含 启动/设置/备份/诊断/帮助",
                    headers.Contains("启动") && headers.Contains("设置") && headers.Contains("备份") &&
                    headers.Contains("诊断") && headers.Contains("帮助"), string.Join("/", headers.ToArray()));

                Button stopButton = FindButton(buttons, "正常停止");
                Button forceButton = FindButton(buttons, "强制结束");
                Button launchButton = FindButton(buttons, "启动");
                runner.True("存在“启动”按钮", launchButton != null, "未找到");
                runner.True("存在“正常停止”按钮", stopButton != null, "未找到");
                runner.True("存在“强制结束”按钮", forceButton != null, "未找到");
                runner.True("未运行时“正常停止”被禁用", stopButton != null && !stopButton.IsEnabled,
                    stopButton == null ? "缺少按钮" : "IsEnabled=" + stopButton.IsEnabled);
                runner.True("未运行时“强制结束”被禁用", forceButton != null && !forceButton.IsEnabled,
                    forceButton == null ? "缺少按钮" : "IsEnabled=" + forceButton.IsEnabled);

                bool crowded = false;
                string crowdedDetail = string.Empty;
                for (int i = 0; i < texts.Count; i++)
                {
                    string value = texts[i].Text ?? string.Empty;
                    if (value.Length > 160)
                    {
                        crowded = true;
                        crowdedDetail = "找到 " + value.Length + " 字的说明段落（应折叠进帮助页）";
                        break;
                    }
                }
                runner.True("启动页没有超长说明段落", !crowded, crowdedDetail);

                bool mentionsHonesty = false;
                for (int i = 0; i < texts.Count; i++)
                {
                    string value = texts[i].Text ?? string.Empty;
                    if (value.IndexOf("课程进度以工具自身界面和平台显示为准", StringComparison.Ordinal) >= 0) mentionsHonesty = true;
                }
                runner.True("启动页保留了简短的真实性提示", mentionsHonesty, "未找到该提示");
            }
            catch (Exception error)
            {
                runner.True("界面构建与布局没有异常", false, error.GetType().Name + ": " + error.Message);
            }
            finally
            {
                MainWindow.SuppressTrayForTests = false;
                Temp.CleanupAll();
            }

            try
            {
                DraftTests.Run(runner);
            }
            catch (Exception error)
            {
                runner.True("草稿测试可执行", false, error.GetType().Name + ": " + error.Message);
            }

            return runner.Summarize();
        }

        private static Button FindButton(List<Button> buttons, string text)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                string content = Convert.ToString(buttons[i].Content);
                if (string.Equals(content, text, StringComparison.Ordinal)) return buttons[i];
            }
            return null;
        }

        private static void Collect(DependencyObject root, List<TabItem> tabs, List<Button> buttons, List<TextBlock> texts)
        {
            if (root == null) return;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                TabItem tab = child as TabItem;
                if (tab != null) tabs.Add(tab);
                Button button = child as Button;
                if (button != null) buttons.Add(button);
                TextBlock block = child as TextBlock;
                if (block != null) texts.Add(block);
                Collect(child, tabs, buttons, texts);
            }
        }
    }
}

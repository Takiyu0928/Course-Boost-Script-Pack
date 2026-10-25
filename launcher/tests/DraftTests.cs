using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TimeSaver.App;
using TimeSaver.Core;
using TimeSaver.Ui;

namespace TimeSaver.Tests
{
    /// <summary>
    /// P2-3 regression: an unsaved settings draft must survive every rebuild of the detail area
    /// (switching tools, switching modes, backing up, a cancelled save), be discarded only after a
    /// successful save, and never be written to disk while it is still a draft.
    ///
    /// Added after the final review found that BindCombo's "same value" branch deleted a draft that
    /// had just been restored from itself: the ComboBox template pushes the programmed text into its
    /// inner TextBox, which bubbles one TextChanged with an identical value. Both dropdowns
    /// (Autovisor 浏览器驱动 / Unipus learning_strategy) are covered with two consecutive rebuilds.
    ///
    /// Everything runs off-screen: the window is never shown, no tray icon is created.
    /// </summary>
    public static class DraftTests
    {
        private const string AutovisorIni =
            "[user-account]\r\n" +
            "; 保留注释\r\n" +
            "username = file-user\r\n" +
            "password =\r\n" +
            "\r\n" +
            "[browser-option]\r\n" +
            "driver = Edge\r\n" +
            "EXE_PATH =\r\n" +
            "\r\n" +
            "[course-url]\r\n" +
            "URL1 = https://file.example/1\r\n" +
            "URLn =\r\n";

        private const string UnipusJson =
            "{\r\n" +
            "  \"base_url\": \"https://api.deepseek.com\",\r\n" +
            "  \"model\": \"deepseek-flash\",\r\n" +
            "  \"learning_strategy\": \"learn_all_compusory_course\",\r\n" +
            "  \"max_tokens\": 2000,\r\n" +
            "  \"temperature\": 0.3,\r\n" +
            "  \"interval_ms\": 3000,\r\n" +
            "  \"api_key\": \"sk-file\"\r\n" +
            "}\r\n";

        public static void Run(Runner runner)
        {
            runner.Section("P2-3 未保存草稿跨重建保留");

            string root = Temp.NewDirectory("draft root Example User's 草稿");
            Directory.CreateDirectory(Path.Combine(root, "launcher"));
            Directory.CreateDirectory(Path.Combine(root, "domooc_free"));

            string autovisorDir = Path.Combine(root, "Autovisor-3.18.4-windows-amd64");
            Directory.CreateDirectory(autovisorDir);
            string iniPath = Path.Combine(autovisorDir, "config.ini");
            File.WriteAllText(iniPath, AutovisorIni, new UTF8Encoding(false));

            string unipusDir = Path.Combine(root, "UnipusAI-v3.6-windows-x86-64");
            Directory.CreateDirectory(unipusDir);
            File.WriteAllText(Path.Combine(unipusDir, "config.json"), UnipusJson, new UTF8Encoding(false));

            PathResolver paths = PathResolver.ForRoot(root);
            LauncherContext context = LauncherContext.Create(paths);
            MainWindow.SuppressTrayForTests = true;

            WindowStateStore state = new WindowStateStore();
            state.CloseToTray = false;
            MainWindow window = new MainWindow(context, state, Path.Combine(root, "state.json"));

            List<string> alerts = new List<string>();
            bool confirmResult = true;
            window.ConfirmHook = delegate(string text, string caption) { return confirmResult; };
            window.AlertHook = delegate(string text, string caption) { alerts.Add(caption + ": " + text); };

            try
            {
                FrameworkElement content = OffscreenUi.Layout(window);
                ListBox toolList = FindToolList(content);
                TabControl tabs = OffscreenUi.FindFirst<TabControl>(content);
                runner.True("找到工具列表", toolList != null && toolList.Items.Count >= 3,
                    toolList == null ? "null" : "count=" + toolList.Items.Count);
                runner.True("找到页签控件", tabs != null, "null");
                if (toolList == null || tabs == null) return;

                /* --- A. 新建、未修改的表单不能被标成脏 --- */
                toolList.SelectedIndex = 0;
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);
                runner.True("新建未修改表单不显示脏标记",
                    HintOf(toolList, 0).IndexOf("有未保存修改", StringComparison.Ordinal) < 0, HintOf(toolList, 0));

                /* --- B. 浏览器驱动下拉：草稿 + 两次“回填事件 + 整页重建” --- */
                ComboBox driver = OffscreenUi.FindCombo(tabs);
                runner.True("Autovisor 设置页有驱动下拉框", driver != null, "null");
                if (driver == null) return;
                runner.Equal("驱动初始值来自文件", "Edge", driver.Text);

                driver.Text = "Chrome";
                OffscreenUi.Relayout(content);
                runner.True("编辑下拉框后出现脏标记",
                    HintOf(toolList, 0).IndexOf("有未保存修改", StringComparison.Ordinal) >= 0, HintOf(toolList, 0));

                // 正是终审复现的那一步：模板回填冒泡一次同值 TextChanged
                OffscreenUi.RaiseBackfill(driver);
                OffscreenUi.Relayout(content);
                ComboBox afterBackfill = OffscreenUi.FindCombo(tabs);
                runner.Equal("回填事件后下拉框仍是草稿值", "Chrome", afterBackfill == null ? "(null)" : afterBackfill.Text);

                for (int round = 1; round <= 2; round++)
                {
                    ComboBox current = OffscreenUi.FindCombo(tabs);
                    if (current != null) OffscreenUi.RaiseBackfill(current);
                    toolList.SelectedIndex = 1;                 // 切到 unipus（整页重建）
                    OffscreenUi.Relayout(content);
                    toolList.SelectedIndex = 0;                 // 切回 autovisor（再来一次重建）
                    OffscreenUi.Relayout(content);
                    OffscreenUi.SelectTab(tabs, "设置");
                    OffscreenUi.Relayout(content);
                    ComboBox restored = OffscreenUi.FindCombo(tabs);
                    runner.Equal("第 " + round + " 次重建后驱动草稿仍在", "Chrome",
                        restored == null ? "(null)" : restored.Text);
                    runner.True("第 " + round + " 次重建后仍标记为脏",
                        HintOf(toolList, 0).IndexOf("有未保存修改", StringComparison.Ordinal) >= 0, HintOf(toolList, 0));
                }

                /* --- C. 学习策略下拉（Unipus）同样要有草稿保护 --- */
                toolList.SelectedIndex = 1;
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);
                ComboBox strategy = OffscreenUi.FindCombo(tabs);
                runner.True("Unipus 设置页有学习策略下拉框", strategy != null, "null");
                if (strategy == null) return;

                strategy.Text = "learn_all_course";
                OffscreenUi.Relayout(content);
                for (int round = 1; round <= 2; round++)
                {
                    ComboBox current = OffscreenUi.FindCombo(tabs);
                    if (current != null) OffscreenUi.RaiseBackfill(current);
                    toolList.SelectedIndex = 0;
                    OffscreenUi.Relayout(content);
                    toolList.SelectedIndex = 1;
                    OffscreenUi.Relayout(content);
                    OffscreenUi.SelectTab(tabs, "设置");
                    OffscreenUi.Relayout(content);
                    ComboBox restored = OffscreenUi.FindCombo(tabs);
                    runner.Equal("第 " + round + " 次重建后策略草稿仍在", "learn_all_course",
                        restored == null ? "(null)" : restored.Text);
                }

                /* --- D. 文本 / 密码草稿：切工具往返 --- */
                toolList.SelectedIndex = 0;
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);
                TextBox username = OffscreenUi.FindFirst<TextBox>(tabs);
                PasswordBox password = OffscreenUi.FindFirst<PasswordBox>(tabs);
                runner.True("设置页有账号输入框", username != null, "null");
                runner.True("设置页有密码输入框", password != null, "null");
                if (username == null || password == null) return;
                runner.Equal("账号初始值是文件里的值", "file-user", username.Text);

                username.Text = "草稿用户";
                password.Password = "草稿秘密";
                toolList.SelectedIndex = 1;
                OffscreenUi.Relayout(content);
                toolList.SelectedIndex = 0;
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);
                TextBox username2 = OffscreenUi.FindFirst<TextBox>(tabs);
                PasswordBox password2 = OffscreenUi.FindFirst<PasswordBox>(tabs);
                runner.Equal("切工具往返后账号草稿还在", "草稿用户", username2 == null ? "(null)" : username2.Text);
                runner.Equal("切工具往返后密码草稿还在（内存中）", "草稿秘密", password2 == null ? "(null)" : password2.Password);

                /* --- E. 切换 domooc 启动模式（整页重建）后再切回 --- */
                toolList.SelectedIndex = 2;
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "启动");
                OffscreenUi.Relayout(content);
                List<RadioButton> radios = OffscreenUi.FindAll<RadioButton>(tabs);
                runner.True("domooc 启动页有多模式单选", radios.Count >= 2, "count=" + radios.Count);
                if (radios.Count >= 2) radios[1].IsChecked = true;
                OffscreenUi.Relayout(content);

                /* --- F. 立即备份（也会重建页签） --- */
                toolList.SelectedIndex = 0;
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "备份");
                OffscreenUi.Relayout(content);
                OffscreenUi.Click(OffscreenUi.FindButton(tabs, "立即备份"));
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);
                TextBox username3 = OffscreenUi.FindFirst<TextBox>(tabs);
                runner.Equal("立即备份后账号草稿还在", "草稿用户", username3 == null ? "(null)" : username3.Text);

                /* --- G. 取消保存：文件不动，草稿与表单都保留 --- */
                confirmResult = false;
                OffscreenUi.Click(OffscreenUi.FindButton(tabs, "保存设置"));
                OffscreenUi.Relayout(content);
                string afterCancel = File.ReadAllText(iniPath, new UTF8Encoding(false));
                runner.True("取消保存后文件未改动",
                    afterCancel.IndexOf("草稿用户", StringComparison.Ordinal) < 0, afterCancel);
                TextBox username4 = OffscreenUi.FindFirst<TextBox>(tabs);
                runner.Equal("取消保存后表单仍显示草稿", "草稿用户", username4 == null ? "(null)" : username4.Text);

                /* --- H. 草稿期间秘密不落盘 --- */
                runner.True("state.json 不含密码草稿", !FileContains(root, "state.json", "草稿秘密"), "state.json 泄漏");
                runner.True("日志不含密码草稿", !AnyFileContains(Path.Combine(root, "launcher", "logs"), "草稿秘密"), "日志泄漏");

                /* --- I. 确认保存：写入文件并清掉草稿 --- */
                confirmResult = true;
                OffscreenUi.Click(OffscreenUi.FindButton(tabs, "保存设置"));
                OffscreenUi.Relayout(content);
                string afterSave = File.ReadAllText(iniPath, new UTF8Encoding(false));
                runner.True("保存后文件里出现新账号", afterSave.IndexOf("username = 草稿用户", StringComparison.Ordinal) >= 0, afterSave);
                runner.True("保存后文件里出现草稿选择的驱动",
                    afterSave.IndexOf("driver = Chrome", StringComparison.Ordinal) >= 0, afterSave);
                runner.True("保存后注释仍保留", afterSave.IndexOf("; 保留注释", StringComparison.Ordinal) >= 0, afterSave);
                runner.True("保存后不再是脏状态",
                    HintOf(toolList, 0).IndexOf("有未保存修改", StringComparison.Ordinal) < 0, HintOf(toolList, 0));

                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);
                TextBox username5 = OffscreenUi.FindFirst<TextBox>(tabs);
                ComboBox driver5 = OffscreenUi.FindCombo(tabs);
                runner.Equal("保存后表单显示已保存的账号", "草稿用户", username5 == null ? "(null)" : username5.Text);
                runner.Equal("保存后下拉框显示已保存的值", "Chrome", driver5 == null ? "(null)" : driver5.Text);
                PasswordBox password5 = OffscreenUi.FindFirst<PasswordBox>(tabs);
                runner.True("保存后不再留下密码草稿", password5 == null || password5.Password.Length == 0, "密码框仍有内容");

                runner.True("整个过程没有弹出错误提示", alerts.Count == 0, string.Join(" | ", alerts.ToArray()));

                ComboRevert(runner);
            }
            catch (Exception error)
            {
                runner.True("草稿测试没有异常", false, error.GetType().Name + ": " + error.Message);
            }
            finally
            {
                MainWindow.SuppressTrayForTests = false;
                Temp.CleanupAll();
            }
        }

        /// <summary>
        /// 下拉框改回文件原值：界面显示什么，草案/保存就必须是什么。
        ///
        /// 覆盖两个方向：
        ///   A 文件 Edge → 用户 Chrome → 用户 Edge → 两次重建仍是 Edge，保存写 Edge
        ///   B 已有 Chrome 草稿 → 用户 Edge → 用户 Chrome → 两次重建仍是 Chrome（保留最后选择）
        ///   两个下拉框（Autovisor 浏览器驱动 / Unipus learning_strategy）各跑一遍。
        /// </summary>
        private static void ComboRevert(Runner runner)
        {
            runner.Section("P2-3 下拉框改回原值（界面值=草稿值=保存值）");

            string root = Temp.NewDirectory("combo revert Example User's 草稿");
            Directory.CreateDirectory(Path.Combine(root, "launcher"));
            Directory.CreateDirectory(Path.Combine(root, "domooc_free"));

            string autovisorDir = Path.Combine(root, "Autovisor-3.18.4-windows-amd64");
            Directory.CreateDirectory(autovisorDir);
            string iniPath = Path.Combine(autovisorDir, "config.ini");
            File.WriteAllText(iniPath, AutovisorIni, new UTF8Encoding(false));

            string unipusDir = Path.Combine(root, "UnipusAI-v3.6-windows-x86-64");
            Directory.CreateDirectory(unipusDir);
            File.WriteAllText(Path.Combine(unipusDir, "config.json"), UnipusJson, new UTF8Encoding(false));

            LauncherContext context = LauncherContext.Create(PathResolver.ForRoot(root));
            WindowStateStore state = new WindowStateStore();
            state.CloseToTray = false;
            MainWindow window = new MainWindow(context, state, Path.Combine(root, "state.json"));
            window.ConfirmHook = delegate(string text, string caption) { return true; };
            window.AlertHook = delegate(string text, string caption) { };

            try
            {
                FrameworkElement content = OffscreenUi.Layout(window);
                ListBox toolList = FindToolList(content);
                TabControl tabs = OffscreenUi.FindFirst<TabControl>(content);
                if (toolList == null || tabs == null)
                {
                    runner.True("下拉框场景：找到列表与页签", false, "null");
                    return;
                }

                /* --- A. 文件 Edge → Chrome → Edge --- */
                toolList.SelectedIndex = 0;
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);

                ComboBox driver = OffscreenUi.FindCombo(tabs);
                runner.Equal("A 初始值为文件里的 Edge", "Edge", driver == null ? "(null)" : driver.Text);
                if (driver == null) return;

                driver.Text = "Chrome";
                OffscreenUi.Relayout(content);
                driver = OffscreenUi.FindCombo(tabs);
                driver.Text = "Edge"; // 用户手动改回文件原值（== initial）
                OffscreenUi.Relayout(content);

                for (int round = 1; round <= 2; round++)
                {
                    toolList.SelectedIndex = 1;
                    OffscreenUi.Relayout(content);
                    toolList.SelectedIndex = 0;
                    OffscreenUi.Relayout(content);
                    OffscreenUi.SelectTab(tabs, "设置");
                    OffscreenUi.Relayout(content);
                    ComboBox after = OffscreenUi.FindCombo(tabs);
                    runner.Equal("A 第 " + round + " 次重建后下拉框仍是 Edge（改回值生效）", "Edge",
                        after == null ? "(null)" : after.Text);
                }

                OffscreenUi.Click(OffscreenUi.FindButton(tabs, "保存设置"));
                OffscreenUi.Relayout(content);
                string afterSave = File.ReadAllText(iniPath, new UTF8Encoding(false));
                runner.True("A 保存后文件里是 Edge（没有写回旧 Chrome）",
                    afterSave.IndexOf("driver = Edge", StringComparison.Ordinal) >= 0, afterSave);
                runner.True("A 保存后文件里没有 Chrome",
                    afterSave.IndexOf("driver = Chrome", StringComparison.Ordinal) < 0, afterSave);

                /* --- B. 已有 Chrome 草稿 → Edge → Chrome --- */
                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);
                driver = OffscreenUi.FindCombo(tabs);
                driver.Text = "Chrome";
                OffscreenUi.Relayout(content);
                toolList.SelectedIndex = 1;   // 触发重建：initial 变成 Chrome（来自草稿）
                OffscreenUi.Relayout(content);
                toolList.SelectedIndex = 0;
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);

                driver = OffscreenUi.FindCombo(tabs);
                runner.Equal("B 重建后恢复的草稿是 Chrome", "Chrome", driver == null ? "(null)" : driver.Text);
                if (driver == null) return;
                OffscreenUi.RaiseBackfill(driver); // 模板回填事件不能清掉草稿
                OffscreenUi.Relayout(content);
                driver = OffscreenUi.FindCombo(tabs);
                driver.Text = "Edge";              // 用户改成 Edge（!= initial）
                OffscreenUi.Relayout(content);
                driver = OffscreenUi.FindCombo(tabs);
                driver.Text = "Chrome";            // 用户又改回 Chrome（== initial，但草稿是 Edge）
                OffscreenUi.Relayout(content);

                for (int round = 1; round <= 2; round++)
                {
                    toolList.SelectedIndex = 1;
                    OffscreenUi.Relayout(content);
                    toolList.SelectedIndex = 0;
                    OffscreenUi.Relayout(content);
                    OffscreenUi.SelectTab(tabs, "设置");
                    OffscreenUi.Relayout(content);
                    ComboBox after = OffscreenUi.FindCombo(tabs);
                    runner.Equal("B 第 " + round + " 次重建后仍是最后选择的 Chrome", "Chrome",
                        after == null ? "(null)" : after.Text);
                }

                /* --- C. Unipus 学习策略同样 --- */
                toolList.SelectedIndex = 1;
                OffscreenUi.Relayout(content);
                OffscreenUi.SelectTab(tabs, "设置");
                OffscreenUi.Relayout(content);
                ComboBox strategy = OffscreenUi.FindCombo(tabs);
                runner.True("C Unipus 有学习策略下拉框", strategy != null, "null");
                if (strategy == null) return;

                string fileStrategy = "learn_all_compusory_course";
                runner.Equal("C 初始值来自文件", fileStrategy, strategy.Text);
                strategy.Text = "learn_all_course";
                OffscreenUi.Relayout(content);
                strategy = OffscreenUi.FindCombo(tabs);
                strategy.Text = fileStrategy; // 改回文件原值
                OffscreenUi.Relayout(content);
                for (int round = 1; round <= 2; round++)
                {
                    toolList.SelectedIndex = 0;
                    OffscreenUi.Relayout(content);
                    toolList.SelectedIndex = 1;
                    OffscreenUi.Relayout(content);
                    OffscreenUi.SelectTab(tabs, "设置");
                    OffscreenUi.Relayout(content);
                    ComboBox after = OffscreenUi.FindCombo(tabs);
                    runner.Equal("C 第 " + round + " 次重建后策略仍是文件原值", fileStrategy,
                        after == null ? "(null)" : after.Text);
                }

                strategy = OffscreenUi.FindCombo(tabs);
                strategy.Text = "learn_all_course";
                OffscreenUi.Relayout(content);
                strategy = OffscreenUi.FindCombo(tabs);
                strategy.Text = fileStrategy; // 再回到原值：仍必须是原值
                OffscreenUi.Relayout(content);
                ComboBox finalCombo = OffscreenUi.FindCombo(tabs);
                runner.Equal("C 再改回原值后仍是原值（不残留上一次选择）", fileStrategy,
                    finalCombo == null ? "(null)" : finalCombo.Text);
            }
            catch (Exception error)
            {
                runner.True("下拉框场景没有异常", false, error.GetType().Name + ": " + error.Message);
            }
        }

        /* ---------------- 辅助 ---------------- */

        private static ListBox FindToolList(DependencyObject root)
        {
            List<ListBox> boxes = OffscreenUi.FindAll<ListBox>(root);
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Items.Count == 0) continue;
                ListBoxItem first = boxes[i].Items[0] as ListBoxItem;
                if (first != null && first.Tag is TimeSaver.Model.ToolDefinition) return boxes[i];
            }
            return null;
        }

        private static string HintOf(ListBox toolList, int index)
        {
            ListBoxItem item = toolList.Items[index] as ListBoxItem;
            if (item == null) return string.Empty;
            StackPanel panel = item.Content as StackPanel;
            if (panel == null || panel.Children.Count < 2) return string.Empty;
            TextBlock hint = panel.Children[1] as TextBlock;
            return hint == null ? string.Empty : hint.Text;
        }

        private static bool FileContains(string root, string fileName, string needle)
        {
            string path = Path.Combine(root, fileName);
            if (!File.Exists(path)) return false;
            return File.ReadAllText(path, new UTF8Encoding(false)).IndexOf(needle, StringComparison.Ordinal) >= 0;
        }

        private static bool AnyFileContains(string directory, string needle)
        {
            try
            {
                if (!Directory.Exists(directory)) return false;
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    if (File.ReadAllText(file, new UTF8Encoding(false)).IndexOf(needle, StringComparison.Ordinal) >= 0)
                        return true;
            }
            catch
            {
                // ignore unreadable files
            }
            return false;
        }
    }
}

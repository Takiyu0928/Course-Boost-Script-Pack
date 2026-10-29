using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TimeSaver.App;
using TimeSaver.Config;
using TimeSaver.Core;
using TimeSaver.Model;
using TimeSaver.Ui;

namespace TimeSaver.Tests
{
    public static class OtherAccountTests
    {
        private const string Fixture = "{\"phone_number\":\"old-user\",\"password\":\"old-password\",\"API\":\"test-api\",\"cour\":[\"old-course\"],\"file_name\":[\"old-file\"],\"browser\":\"edge\",\"unknown\":{\"keep\":true}}";
        private static JsonValue Read(string path) { return JsonDocument.Parse(File.ReadAllText(path)).Root; }

        public static void Model(Runner runner)
        {
            runner.Section("学习通账号保存、备份与配置保留");
            string directory = Temp.NewDirectory("xue-account");
            string path = Path.Combine(directory, "account_info.json");
            File.WriteAllText(path, Fixture, new UTF8Encoding(false));
            BackupService backups = new BackupService(Path.Combine(directory, "backups"));
            XuexitongAccountSettings settings = XuexitongAccountSettings.Load(path);
            runner.Equal("读取学习通账号", "old-user", settings.Username);
            runner.True("识别学习通已保存密码", settings.PasswordConfigured);
            settings.Save(backups, "old-user", SecretChange.Keep, "ignored");
            runner.Equal("留空保留学习通密码", "old-password", Read(path).Get("password").Text);
            runner.EqualInt("同账号保留课程", 1, Read(path).Get("cour").Items.Count);
            string before = File.ReadAllText(path);
            runner.Throws("换学习通账号必须提供新密码", delegate { settings.Save(backups, "new-user", SecretChange.Keep, ""); });
            runner.Equal("校验失败文件不变", before, File.ReadAllText(path));
            string backup = settings.Save(backups, "new-user", SecretChange.Replace, "new-password");
            runner.Equal("账号保存前备份原账号", "old-user", Read(backup).Get("phone_number").Text);
            runner.Equal("学习通账号已替换", "new-user", Read(path).Get("phone_number").Text);
            runner.Equal("学习通密码已替换", "new-password", Read(path).Get("password").Text);
            runner.Equal("保留学习通 API", "test-api", Read(path).Get("API").Text);
            runner.Equal("保留学习通浏览器设置", "edge", Read(path).Get("browser").Text);
            runner.True("保留学习通未知字段", Read(path).Get("unknown") != null);
            runner.EqualInt("换账号清除旧课程选择", 0, Read(path).Get("cour").Items.Count);
            runner.EqualInt("换账号清除旧文件选择", 0, Read(path).Get("file_name").Items.Count);
            XuexitongAccountSettings.Load(path).Save(backups, "new-user", SecretChange.Clear, "");
            runner.True("允许显式清除学习通密码", !XuexitongAccountSettings.Load(path).PasswordConfigured);
        }

        public static void Ui(Runner runner)
        {
            runner.Section("四工具账号入口与学习通路径（离屏）");
            string root = Temp.NewDirectory("all account tools");
            Directory.CreateDirectory(Path.Combine(root, "launcher"));
            string auto = Path.Combine(root, "Autovisor-3.18.4-windows-amd64");
            Directory.CreateDirectory(auto);
            File.WriteAllText(Path.Combine(auto, "config.ini"), "[user-account]\nusername=test-user\npassword=test-password\n[browser-option]\ndriver=Edge\n", new UTF8Encoding(false));
            string uni = Path.Combine(root, "UnipusAI-v3.6-windows-x86-64");
            Directory.CreateDirectory(uni);
            File.WriteAllText(Path.Combine(uni, "config.json"), "{}", new UTF8Encoding(false));
            string xue = Path.Combine(root, @"学习通\exe)\task\tool");
            Directory.CreateDirectory(xue);
            string path = Path.Combine(xue, "account_info.json");
            File.WriteAllText(path, Fixture, new UTF8Encoding(false));
            LauncherContext context = LauncherContext.Create(PathResolver.ForRoot(root));
            ToolDefinition definition = context.Catalog.Find("xuexitong");
            runner.Equal("识别改名后的学习通目录", @"学习通\exe)", definition.Directory);
            runner.Equal("学习通配置指向真实文件", path, context.ConfigPath(definition));
            runner.Equal("学习通适配 task/main.exe", @"task\main.exe", definition.FindEntry("main").ExeRelativePath);
            ToolEntryPoint login = context.Catalog.Find("domooc").FindEntry("login");
            runner.True("MOOC 登录使用独立 Profile", Array.IndexOf(login.Arguments, "{profile}") >= 0);
            runner.True("MOOC 登录打开官方网页", Array.IndexOf(login.Arguments, "https://www.icourse163.org/") >= 0);
            runner.True("MOOC 登录不启用自动化", Array.IndexOf(login.Arguments, "-marionette") < 0);
            MainWindow.SuppressTrayForTests = true;
            MainWindow window = new MainWindow(context, new WindowStateStore { CloseToTray = false }, Path.Combine(root, "state.json"));
            window.ConfirmHook = delegate { return true; };
            bool alerted = false;
            window.AlertHook = delegate { alerted = true; };
            try
            {
                FrameworkElement content = OffscreenUi.Layout(window);
                ListBox tools = OffscreenUi.FindFirst<ListBox>(content);
                TabControl tabs = OffscreenUi.FindFirst<TabControl>(content);
                for (int i = 0; i < 4; i++)
                {
                    tools.SelectedIndex = i;
                    OffscreenUi.SelectTab(tabs, "启动");
                    OffscreenUi.Relayout(content);
                    Button entry = OffscreenUi.FindButton(content, "账号配置");
                    runner.True("工具 " + i + " 有账号入口", entry != null);
                    runner.True("工具 " + i + " 有路径探测入口", OffscreenUi.FindButton(content, "探测启动路径") != null);
                    OffscreenUi.Click(entry);
                    OffscreenUi.Relayout(content);
                    runner.Equal("工具 " + i + " 进入设置页", "设置", Convert.ToString(((TabItem)tabs.SelectedItem).Header));
                    if (i == 2) runner.True("domooc 显示网页登录按钮", OffscreenUi.FindButton(tabs, "打开 MOOC 登录页面") != null);
                    if (i == 3)
                    {
                        foreach (TextBox box in OffscreenUi.FindAll<TextBox>(tabs))
                            if (box.Name == "XuexitongUsername") box.Text = "new-user";
                        foreach (PasswordBox box in OffscreenUi.FindAll<PasswordBox>(tabs))
                            if (box.Name == "XuexitongPassword") box.Password = "new-password";
                        OffscreenUi.Click(OffscreenUi.FindButton(tabs, "保存账号"));
                        OffscreenUi.Relayout(content);
                        runner.Equal("界面写入学习通账号", "new-user", Read(path).Get("phone_number").Text);
                        runner.Equal("界面写入学习通密码", "new-password", Read(path).Get("password").Text);
                        runner.Equal("界面保留学习通 API", "test-api", Read(path).Get("API").Text);
                        foreach (PasswordBox box in OffscreenUi.FindAll<PasswordBox>(tabs))
                            runner.Equal("保存后不回显学习通密码", "", box.Password);
                    }
                }
                runner.True("账号配置无错误弹窗", !alerted);
                runner.True("学习通密码不写入日志", context.Log.Tail(500).IndexOf("new-password", StringComparison.Ordinal) < 0);
            }
            finally
            {
                window.Close();
                MainWindow.SuppressTrayForTests = false;
                Temp.CleanupAll();
            }
        }
    }
}

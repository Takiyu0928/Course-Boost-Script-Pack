using System;
using System.IO;
using System.Text;
using System.Windows.Controls;
using TimeSaver.App;
using TimeSaver.Config;
using TimeSaver.Core;
using TimeSaver.Model;
using TimeSaver.Ui;

namespace TimeSaver.Tests
{
    public static class AccountTests
    {
        private const string Fixture = "{\"username\":\"old-user\",\"password\":\"old-password\",\"api_key\":\"test-api\",\"model\":\"test-model\",\"cookie\":\"old-session\",\"refresh_token\":\"old-refresh\",\"open_id\":\"old-id\",\"course_id\":\"old-course\",\"class_id\":\"old-class\",\"unknown\":{\"keep\":true}}";

        private static JsonValue Read(string path) { return JsonDocument.Parse(File.ReadAllText(path)).Root; }

        public static void Model(Runner runner)
        {
            runner.Section("Unipus 账号保存与凭证隔离");
            string directory = Temp.NewDirectory("account-model");
            string path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, Fixture, new UTF8Encoding(false));
            BackupService backups = new BackupService(Path.Combine(directory, "backups"));
            UnipusSettings settings = UnipusSettings.Load(path);
            runner.Equal("读取账号", "old-user", settings.Username);
            runner.True("识别已保存密码", settings.PasswordConfigured);
            settings.SaveAccount(backups, "old-user", SecretChange.Keep, "ignored");
            runner.Equal("留空保存保留密码", "old-password", Read(path).Get("password").Text);
            runner.Equal("未更改凭证时保留会话", "old-session", Read(path).Get("cookie").Text);
            string before = File.ReadAllText(path);
            runner.Throws("换账号不允许复用旧密码", delegate { settings.SaveAccount(backups, "new-user", SecretChange.Keep, ""); });
            runner.Equal("校验失败不改文件", before, File.ReadAllText(path));
            // Simulate a separate AI settings save after the account form loaded.
            UnipusSettings newer = UnipusSettings.Load(path);
            newer.Model = "newer-model";
            newer.Save(backups, SecretChange.Keep, "");
            string backup = settings.SaveAccount(backups, "new-user", SecretChange.Replace, "new-password");
            JsonValue root = Read(path);
            runner.True("换账号前生成备份", File.Exists(backup));
            runner.Equal("备份保留原账号", "old-user", Read(backup).Get("username").Text);
            runner.Equal("新账号保存", "new-user", root.Get("username").Text);
            runner.Equal("新密码保存", "new-password", root.Get("password").Text);
            runner.Equal("API Key 不被账号密码覆盖", "test-api", root.Get("api_key").Text);
            runner.Equal("保留最新 AI 设置", "newer-model", root.Get("model").Text);
            runner.Equal("换账号清除旧会话", "", root.Get("cookie").Text);
            runner.Equal("换账号清除旧刷新令牌", "", root.Get("refresh_token").Text);
            runner.Equal("换账号清除旧用户标识", "", root.Get("open_id").Text);
            runner.Equal("换账号清除旧课程", "", root.Get("course_id").Text);
            runner.True("保留未知字段", root.Get("unknown").Get("keep").AsString("") == "true");
            UnipusSettings.Load(path).SaveAccount(backups, "new-user", SecretChange.Clear, "");
            runner.Equal("显式清除账号密码", "", Read(path).Get("password").Text);
            runner.Equal("清除账号密码不清除 API Key", "test-api", Read(path).Get("api_key").Text);
        }

        public static void Ui(Runner runner)
        {
            runner.Section("Unipus 账号入口与双密码草稿（离屏）");
            string root = Temp.NewDirectory("account-ui");
            Directory.CreateDirectory(Path.Combine(root, "launcher"));
            string tool = Path.Combine(root, "UnipusAI-v3.6-windows-x86-64");
            Directory.CreateDirectory(tool);
            string path = Path.Combine(tool, "config.json");
            File.WriteAllText(path, Fixture, new UTF8Encoding(false));
            LauncherContext context = LauncherContext.Create(PathResolver.ForRoot(root));
            MainWindow.SuppressTrayForTests = true;
            MainWindow window = new MainWindow(context, new WindowStateStore { CloseToTray = false }, Path.Combine(root, "state.json"));
            window.ConfirmHook = delegate { return true; };
            bool alerted = false;
            window.AlertHook = delegate { alerted = true; };
            try
            {
                System.Windows.FrameworkElement content = OffscreenUi.Layout(window);
                ListBox tools = OffscreenUi.FindFirst<ListBox>(content);
                tools.SelectedIndex = 1;
                OffscreenUi.Relayout(content);
                TabControl tabs = OffscreenUi.FindFirst<TabControl>(content);
                Button entry = OffscreenUi.FindButton(content, "账号配置");
                runner.True("启动页显示账号配置入口", entry != null);
                OffscreenUi.Click(entry);
                OffscreenUi.Relayout(content);
                runner.Equal("账号入口打开设置页", "设置", Convert.ToString(((TabItem)tabs.SelectedItem).Header));
                PasswordBox accountPassword = FindPassword(tabs, "UnipusPassword");
                runner.Equal("账号密码默认不回显", "", accountPassword.Password);
                foreach (TextBox box in OffscreenUi.FindAll<TextBox>(tabs))
                    if (box.Name == "UnipusUsername") box.Text = "new-user";
                accountPassword.Password = "account-draft";
                foreach (PasswordBox box in OffscreenUi.FindAll<PasswordBox>(tabs))
                    if (box.Name != "UnipusPassword") box.Password = "api-draft";
                tools.SelectedIndex = 0;
                tools.SelectedIndex = 1;
                OffscreenUi.Relayout(content);
                runner.Equal("切换工具保留账号密码草稿", "account-draft", FindPassword(tabs, "UnipusPassword").Password);
                // AI save must preserve unsaved account values/password separately.
                OffscreenUi.Click(OffscreenUi.FindButton(tabs, "保存设置"));
                OffscreenUi.Relayout(content);
                runner.Equal("AI 保存只写 API Key", "api-draft", Read(path).Get("api_key").Text);
                runner.Equal("AI 保存不写账号密码草稿", "old-password", Read(path).Get("password").Text);
                runner.Equal("AI 保存保留账号密码草稿", "account-draft", FindPassword(tabs, "UnipusPassword").Password);
                OffscreenUi.Click(OffscreenUi.FindButton(tabs, "保存账号"));
                OffscreenUi.Relayout(content);
                runner.Equal("账号按钮保存新账号", "new-user", Read(path).Get("username").Text);
                runner.Equal("账号按钮保存正确密码", "account-draft", Read(path).Get("password").Text);
                runner.Equal("账号保存保留 API Key", "api-draft", Read(path).Get("api_key").Text);
                runner.Equal("保存后清除内存密码输入", "", FindPassword(tabs, "UnipusPassword").Password);
                FindPassword(tabs, "UnipusPassword").Password = "second-password";
                foreach (PasswordBox box in OffscreenUi.FindAll<PasswordBox>(tabs))
                    if (box.Name != "UnipusPassword") box.Password = "second-api";
                OffscreenUi.Click(OffscreenUi.FindButton(tabs, "保存账号"));
                OffscreenUi.Relayout(content);
                foreach (PasswordBox box in OffscreenUi.FindAll<PasswordBox>(tabs))
                    if (box.Name != "UnipusPassword") runner.Equal("账号保存保留 API 密钥草稿", "second-api", box.Password);
                runner.Equal("账号保存不提前写入 API 草稿", "api-draft", Read(path).Get("api_key").Text);
                OffscreenUi.Click(OffscreenUi.FindButton(tabs, "保存设置"));
                OffscreenUi.Relayout(content);
                runner.Equal("分别保存后的密码正确", "second-password", Read(path).Get("password").Text);
                runner.Equal("分别保存后的 API Key 正确", "second-api", Read(path).Get("api_key").Text);
                runner.True("没有错误弹窗", !alerted);
                runner.True("日志不含账号密码", context.Log.Tail(500).IndexOf("account-draft", StringComparison.Ordinal) < 0);
                runner.True("窗口状态不含密码", !File.Exists(Path.Combine(root, "state.json")) || File.ReadAllText(Path.Combine(root, "state.json")).IndexOf("account-draft", StringComparison.Ordinal) < 0);
            }
            finally
            {
                window.Close();
                MainWindow.SuppressTrayForTests = false;
                Temp.CleanupAll();
            }
        }

        private static PasswordBox FindPassword(System.Windows.DependencyObject root, string name)
        {
            foreach (PasswordBox box in OffscreenUi.FindAll<PasswordBox>(root)) if (box.Name == name) return box;
            throw new Exception("Missing password field " + name);
        }
    }
}

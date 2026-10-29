using System;
using System.IO;
using System.Text;
using TimeSaver.Core;
using TimeSaver.Model;

namespace TimeSaver.Tests
{
    public static class ToolPathProbeTests
    {
        private static void Put(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        public static void Run(Runner runner)
        {
            runner.Section("启动路径探测、校验与持久化");
            string root = Temp.NewDirectory("path probe's 中文 root");
            PathResolver paths = PathResolver.ForRoot(root);
            paths.EnsureDirectories();
            ToolCatalog catalog = ToolCatalog.CreateDefault(paths);
            ToolDefinition unipus = catalog.Find("unipus");
            string moved = Path.Combine(root, "renamed tool");
            Put(Path.Combine(moved, "UnipusAI.exe"), "not an executable - discovery must never run this");
            runner.True("缺配置的候选不采用", ToolPathProbe.Inspect(unipus, moved) == null);
            Put(Path.Combine(moved, "config.json"), "{}");
            runner.EqualInt("能识别改名后的脚本文件夹", 1, ToolPathProbe.Find(unipus, root).Count);
            string ignored = Path.Combine(root, @"domooc_free\Profile\fake");
            Put(Path.Combine(ignored, "UnipusAI.exe"), "ignored");
            Put(Path.Combine(ignored, "config.json"), "{}");
            runner.EqualInt("跳过浏览器 Profile 私人目录", 1, ToolPathProbe.Find(unipus, root).Count);
            string deep = Path.Combine(root, @"a\b\c\d\e");
            Put(Path.Combine(deep, "UnipusAI.exe"), "ignored");
            Put(Path.Combine(deep, "config.json"), "{}");
            runner.EqualInt("不超过四层扫描范围", 1, ToolPathProbe.Find(unipus, root).Count);
            BackupService backups = new BackupService(paths.BackupsDirectory);
            ToolPathProbe.Save(paths, unipus, ToolPathProbe.Inspect(unipus, moved), backups);
            runner.Equal("应用后立即切换目录", moved, unipus.Directory);
            runner.Equal("重新加载仍使用所选目录", moved, ToolCatalog.CreateDefault(paths).Find("unipus").Directory);
            ToolPathProbe.Save(paths, unipus, ToolPathProbe.Inspect(unipus, moved), backups);
            runner.EqualInt("修改路径文件前备份", 1, backups.List("tool-paths").Count);
            string store = Path.Combine(paths.LauncherDirectory, "tool-paths.json");
            string before = File.ReadAllText(store);
            runner.Throws("文件缺失时拒绝应用", delegate { ToolPathProbe.Save(paths, unipus, new ToolPathCandidate { Directory = Path.Combine(root, "missing") }, backups); });
            runner.Equal("无效路径不改持久配置", before, File.ReadAllText(store));
            ToolDefinition xue = catalog.Find("xuexitong");
            string xueDir = Path.Combine(root, "other xue");
            Put(Path.Combine(xueDir, "start.exe"), "stub");
            Put(Path.Combine(xueDir, "main.exe"), "stub");
            Put(Path.Combine(xueDir, @"task\tool\account_info.json"), "{}");
            ToolPathProbe.Save(paths, xue, ToolPathProbe.Inspect(xue, xueDir), backups);
            runner.Equal("学习通根目录 main 适配", "main.exe", xue.FindEntry("main").ExeRelativePath);
            runner.Equal("学习通重载路径保留", xueDir, ToolCatalog.CreateDefault(paths).Find("xuexitong").Directory);
            runner.Equal("保存其他工具不影响已有选择", moved, ToolCatalog.CreateDefault(paths).Find("unipus").Directory);
            string browser = Path.Combine(root, "renamed browser");
            Put(Path.Combine(browser, @"App\Firefox64\firefox.exe"), "stub");
            Put(Path.Combine(browser, @"Profile\prefs.js"), "stub");
            ToolDefinition domooc = catalog.Find("domooc");
            runner.True("domooc 缺 user.js 拒绝候选", ToolPathProbe.Inspect(domooc, browser) == null);
            Put(Path.Combine(browser, @"Profile\user.js"), "stub");
            runner.True("domooc 完整目录可识别", ToolPathProbe.Inspect(domooc, browser) != null);
            Put(store, "{broken");
            runner.True("路径文件损坏不会使启动器崩溃", ToolCatalog.CreateDefault(paths).Find("unipus").Warnings.Count > 0);
            runner.Equal("损坏路径文件不被静默覆盖", "{broken", File.ReadAllText(store));
        }
    }
}

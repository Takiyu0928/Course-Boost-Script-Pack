using System;
using System.Collections.Generic;
using System.IO;
using TimeSaver.Config;
using TimeSaver.Core;

namespace TimeSaver.Model
{
    public enum ToolConfigKind
    {
        None,
        Ini,
        Json
    }

    public sealed class ToolEntryPoint
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string ExeRelativePath = string.Empty;
        public string[] Arguments = new string[0];
        /// <summary>Relative to the tool folder; null means "the executable's own folder".</summary>
        public string WorkingDirectoryRelative;
        public Dictionary<string, string> Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Where this entry point was determined from (file + quote). Shown in the UI.</summary>
        public string Evidence = string.Empty;
        /// <summary>false = the evidence is indirect; the UI asks the user instead of asserting.</summary>
        public bool Confirmed = true;
    }

    public sealed class ToolMode
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Summary = string.Empty;
        public string EntryPointId = string.Empty;
        public bool StartsAnswerService;
        public string[] Notes = new string[0];
    }

    public sealed class ToolDefinition
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Directory = string.Empty;
        public string Description = string.Empty;
        public ToolConfigKind ConfigKind = ToolConfigKind.None;
        public string ConfigRelativePath = string.Empty;
        public string[] RequiredFiles = new string[0];
        public string[] OptionalFiles = new string[0];
        public List<ToolEntryPoint> Entries = new List<ToolEntryPoint>();
        public List<ToolMode> Modes = new List<ToolMode>();
        public string DocsHint = string.Empty;
        public List<string> Warnings = new List<string>();

        public ToolEntryPoint FindEntry(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < Entries.Count; i++)
                if (string.Equals(Entries[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Entries[i];
            return null;
        }
    }

    /// <summary>
    /// The tool list. Four tools are built in; launcher\tools.json may add more (schema in
    /// IMPLEMENTATION-REPORT.md) so the catalog is extensible without recompiling.
    ///
    /// Entry points carry the evidence used to pick them, and xuexitong is deliberately marked
    /// "not confirmed" because its documentation only mentions Start/Main in a troubleshooting
    /// answer, not as a manifest.
    /// </summary>
    public sealed class ToolCatalog
    {
        public List<ToolDefinition> Tools = new List<ToolDefinition>();

        public ToolDefinition Find(string id)
        {
            for (int i = 0; i < Tools.Count; i++)
                if (string.Equals(Tools[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Tools[i];
            return null;
        }

        public static ToolCatalog CreateDefault(PathResolver paths)
        {
            ToolCatalog catalog = new ToolCatalog();
            catalog.Tools.Add(BuildAutovisor());
            catalog.Tools.Add(BuildUnipus());
            catalog.Tools.Add(BuildDomooc());
            catalog.Tools.Add(BuildXuexitong(paths));
            catalog.AppendExtras(paths);
            return catalog;
        }

        private static ToolDefinition BuildAutovisor()
        {
            ToolDefinition tool = new ToolDefinition();
            tool.Id = "autovisor";
            tool.Name = "Autovisor 3.18.4";
            tool.Directory = "Autovisor-3.18.4-windows-amd64";
            tool.Description = "超星/智慧树 自动刷课程序（PyInstaller 打包，读取同目录 config.ini）。";
            tool.ConfigKind = ToolConfigKind.Ini;
            tool.ConfigRelativePath = "config.ini";
            tool.RequiredFiles = new string[] { "Autovisor.exe", "config.ini" };
            tool.DocsHint = "账号密码留空时会打开浏览器要求手动登录；课程链接填在 [course-url] 的 URL1..URLn。";

            ToolEntryPoint entry = new ToolEntryPoint();
            entry.Id = "exe";
            entry.Name = "Autovisor.exe";
            entry.ExeRelativePath = "Autovisor.exe";
            entry.Evidence = "目录内只有 Autovisor.exe 一个可执行入口，config.ini 与其同级（PyInstaller onedir 结构）。";
            entry.Confirmed = true;
            tool.Entries.Add(entry);

            ToolMode mode = new ToolMode();
            mode.Id = "run";
            mode.Name = "启动";
            mode.Summary = "按 config.ini 的设置开始运行";
            mode.EntryPointId = "exe";
            tool.Modes.Add(mode);
            return tool;
        }

        private static ToolDefinition BuildUnipus()
        {
            ToolDefinition tool = new ToolDefinition();
            tool.Id = "unipus";
            tool.Name = "UnipusAI 3.6";
            tool.Directory = "UnipusAI-v3.6-windows-x86-64";
            tool.Description = "U 校园 AI 学习助手（读取同目录 config.json，首次运行会加载 whisper 模型缓存）。";
            tool.ConfigKind = ToolConfigKind.Json;
            tool.ConfigRelativePath = "config.json";
            tool.RequiredFiles = new string[] { "UnipusAI.exe", "config.json" };
            tool.OptionalFiles = new string[] { "config.example.json" };
            tool.DocsHint = "模型缓存目录由 HF_HOME 指向 .model_cache，镜像默认 hf-mirror.com（沿用原启动脚本的行为）。";

            ToolEntryPoint entry = new ToolEntryPoint();
            entry.Id = "exe";
            entry.Name = "UnipusAI.exe";
            entry.ExeRelativePath = "UnipusAI.exe";
            entry.Environment["HF_HOME"] = "{dir}\\.model_cache";
            entry.Environment["HF_ENDPOINT"] = "https://hf-mirror.com";
            entry.Evidence = "启动-UnipusAI.cmd：先 set HF_HOME=%~dp0.model_cache、HF_ENDPOINT=https://hf-mirror.com，" +
                             "再 cd /d %~dp0 并运行 UnipusAI.exe（缺 config.json 时从 config.example.json 复制）。";
            entry.Confirmed = true;
            tool.Entries.Add(entry);

            ToolMode mode = new ToolMode();
            mode.Id = "run";
            mode.Name = "启动";
            mode.Summary = "带 HF_HOME / HF_ENDPOINT 环境变量启动";
            mode.EntryPointId = "exe";
            mode.Notes = new string[] { "首次启动需要下载 whisper 模型，进度显示在它自己的控制台窗口里。" };
            tool.Modes.Add(mode);
            return tool;
        }

        private static ToolDefinition BuildDomooc()
        {
            ToolDefinition tool = new ToolDefinition();
            tool.Id = "domooc";
            tool.Name = "domooc 本机版";
            tool.Directory = "domooc_free";
            tool.Description = "打包的 Firefox + 本机化配置，可选本地题库服务（Node，127.0.0.1:8791）。";
            tool.ConfigKind = ToolConfigKind.None;
            tool.RequiredFiles = new string[]
            {
                @"App\Firefox64\firefox.exe",
                @"Profile\prefs.js",
                @"Profile\user.js"
            };
            tool.OptionalFiles = new string[] { @"answer-service\server.js", @"auto\run.py" };
            tool.DocsHint = "Profile\\user.js 缺失时浏览器会保留远程通道，原 .bat 会中止启动；启动器沿用该检查。" +
                            "原 .bat 会盲删 parent.lock / lock / storage 目录，启动器不自动删除，只检测并提示。";

            ToolEntryPoint browser = new ToolEntryPoint();
            browser.Id = "browser";
            browser.Name = "Firefox（本机版）";
            browser.ExeRelativePath = @"App\Firefox64\firefox.exe";
            browser.Arguments = new string[] { "-profile", "{profile}", "-no-remote" };
            browser.Evidence = "启动domooc（本机版）.bat：start \"\" \"%FF%\" -profile \"%PROFILE%\" -no-remote（工作目录为 domooc_free）。";
            browser.Confirmed = true;
            tool.Entries.Add(browser);

            ToolEntryPoint automation = new ToolEntryPoint();
            automation.Id = "browser-automation";
            automation.Name = "Firefox（自动化准备）";
            automation.ExeRelativePath = @"App\Firefox64\firefox.exe";
            automation.Arguments = new string[] { "-marionette", "-profile", "{profile}", "-no-remote" };
            automation.Evidence = "启动domooc（自动化）.bat：start \"\" \"%FF%\" -marionette -profile \"%PROFILE%\" -no-remote。" +
                                  "只开启 Marionette(127.0.0.1:2828)，不会自动刷课。";
            automation.Confirmed = true;
            tool.Entries.Add(automation);

            tool.Modes.Add(MakeMode("browser", "普通浏览器", "只启动本机化 Firefox（远程通道已关闭）", "browser", false,
                new string[] { "profile 锁文件不会被自动删除；如提示被占用，请确认没有另一个 Firefox 正在使用该 Profile。" }));
            tool.Modes.Add(MakeMode("browser-service", "浏览器 + 本地题库", "先起本地答题服务(8791)，再启动浏览器", "browser", true,
                new string[] { "端口占用时会先验证身份：只有确认是 domooc 答题服务才复用，其它程序一律不动。" }));
            tool.Modes.Add(MakeMode("automation", "自动化准备", "带 Marionette 启动浏览器，供 auto/run.py 驱动（不自动刷课）", "browser-automation", true,
                new string[] { "启动器只准备浏览器与本地服务，不会执行任何自动化脚本或课程动作。" }));
            return tool;
        }

        private static ToolMode MakeMode(string id, string name, string summary, string entryId, bool service, string[] notes)
        {
            ToolMode mode = new ToolMode();
            mode.Id = id;
            mode.Name = name;
            mode.Summary = summary;
            mode.EntryPointId = entryId;
            mode.StartsAnswerService = service;
            mode.Notes = notes;
            return mode;
        }

        private static ToolDefinition BuildXuexitong(PathResolver paths)
        {
            ToolDefinition tool = new ToolDefinition();
            tool.Id = "xuexitong";
            tool.Name = "学习通刷课 26.10";
            tool.Directory = @"xuexitong_shuake26.10\exe)";
            tool.Description = "学习通刷课工具（start.exe / main.exe，随附 edgedriver_win64 与用户须知）。";
            tool.ConfigKind = ToolConfigKind.None;
            string mainPath = "main.exe";
            if (!File.Exists(paths.Combine(tool.Directory, mainPath)) &&
                File.Exists(paths.Combine(tool.Directory, "task", "main.exe")))
                mainPath = @"task\main.exe";
            tool.RequiredFiles = new string[] { "start.exe", mainPath };
            tool.OptionalFiles = new string[] { "edgedriver_win64", "用户须知.pdf", @"task\tool\Help.txt" };
            tool.DocsHint = "官方说明只以问答形式提到 Start/Main，没有入口清单；启动器不宣称已验证，运行前请自行选择并核对。";

            ToolEntryPoint start = new ToolEntryPoint();
            start.Id = "start";
            start.Name = "start.exe";
            start.ExeRelativePath = "start.exe";
            start.Confirmed = false;
            start.Evidence = "task\\tool\\Help.txt 四、程序相关 1：「一切准备就绪后，点击启动程序…如果点击后没有反应，" +
                             "那就先把这个窗口关掉然后右键用管理员模式运行 Start」。据此推测 start.exe 是启动入口（待人工确认）。";
            tool.Entries.Add(start);

            ToolEntryPoint main = new ToolEntryPoint();
            main.Id = "main";
            main.Name = mainPath;
            main.ExeRelativePath = mainPath;
            main.Confirmed = false;
            main.Evidence = "同一问答的兜底说法：「如果还是没有反应，那就再把这个窗口关掉，直接双击运行那个 Main 程序」。";
            tool.Entries.Add(main);

            ToolMode startMode = new ToolMode();
            startMode.Id = "start";
            startMode.Name = "start.exe（默认尝试）";
            startMode.Summary = "说明文档首先提到的入口；未经验证，运行后请观察是否出现设置界面";
            startMode.EntryPointId = "start";
            startMode.Notes = new string[] { "入口依据仅为说明文档问答，启动器标记为「待人工确认」。" };
            tool.Modes.Add(startMode);

            ToolMode mainMode = new ToolMode();
            mainMode.Id = "main";
            mainMode.Name = "main.exe（备选）";
            mainMode.Summary = "说明文档给出的兜底入口";
            mainMode.EntryPointId = "main";
            mainMode.Notes = new string[] { "同上：未经验证，仅按文档兜底描述提供。" };
            tool.Modes.Add(mainMode);

            tool.Warnings.Add("此工具入口未经验证：Help.txt 只在「程序相关」问答里提到 Start / Main，没有安装清单或自述文件。");
            return tool;
        }

        /// <summary>Extra tools from launcher\tools.json (optional, never required).</summary>
        private void AppendExtras(PathResolver paths)
        {
            string manifestPath = paths.ToolsManifestFile;
            if (!File.Exists(manifestPath)) return;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            }
            catch (Exception error)
            {
                ToolDefinition broken = new ToolDefinition();
                broken.Id = "_manifest";
                broken.Name = "tools.json（无法解析）";
                broken.Warnings.Add("launcher\\tools.json 解析失败，已忽略: " + error.Message);
                Tools.Add(broken);
                return;
            }

            JsonValue root = document.Root;
            JsonValue tools = root == null ? null : root.Get("tools");
            if (tools == null || tools.Kind != JsonKind.Array) return;

            for (int i = 0; i < tools.Items.Count; i++)
            {
                JsonValue item = tools.Items[i];
                if (item == null || item.Kind != JsonKind.Object) continue;
                ToolDefinition extra = new ToolDefinition();
                extra.Id = item.Get("id") == null ? "extra-" + i : item.Get("id").AsString("extra-" + i);
                extra.Name = item.Get("name") == null ? extra.Id : item.Get("name").AsString(extra.Id);
                extra.Directory = item.Get("directory") == null ? string.Empty : item.Get("directory").AsString(string.Empty);
                extra.Description = item.Get("description") == null ? string.Empty : item.Get("description").AsString(string.Empty);

                JsonValue entry = item.Get("exe");
                if (entry == null) continue;
                ToolEntryPoint point = new ToolEntryPoint();
                point.Id = "exe";
                point.Name = entry.AsString(string.Empty);
                point.ExeRelativePath = entry.AsString(string.Empty);
                point.Evidence = "来自 launcher\\tools.json 的追加清单";
                point.Confirmed = true;
                extra.Entries.Add(point);

                ToolMode mode = new ToolMode();
                mode.Id = "run";
                mode.Name = "启动";
                mode.EntryPointId = "exe";
                extra.Modes.Add(mode);
                Tools.Add(extra);
            }
        }
    }
}

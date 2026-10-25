using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TimeSaver.Config;
using TimeSaver.Core;
using TimeSaver.Model;

namespace TimeSaver.Tests
{
    public static class ConfigTests
    {
        public static void Run(Runner runner)
        {
            IniLossless(runner);
            IniEncoding(runner);
            AutovisorSave(runner);
            JsonLossless(runner);
            UnipusSecretPolicy(runner);
            BackupRestore(runner);
            LogMasking(runner);
        }

        private const string IniFixture =
            "[user-account]\r\n" +
            "; 账号与密码注释\r\n" +
            "username = old-user\r\n" +
            "password = secret123\r\n" +
            "\r\n" +
            "[browser-option]\r\n" +
            "driver = Chrome\r\n" +
            "EXE_PATH =\r\n" +
            "unknownExtra = keepme ; inline note\r\n" +
            "\r\n" +
            "[course-option]\r\n" +
            "limitMaxTime = 30\r\n" +
            "limitSpeed = 1.0 ; 文档上限 1.8\r\n" +
            "soundOff = True\r\n" +
            "\r\n" +
            "[future-section]\r\n" +
            "weird = value\r\n";

        private static void IniLossless(Runner runner)
        {
            runner.Section("INI 无损编辑（注释/未知键/行内注释）");

            IniDocument document = IniDocument.Parse(IniFixture);
            runner.Equal("读取已有值", "old-user", document.Get("user-account", "username", ""));
            runner.True("未知键可见", document.Has("browser-option", "unknownExtra"), "unknownExtra 应存在");

            document.Set("user-account", "username", "new-user");
            document.Set("user-account", "password", "p@ss word");
            document.Set("course-option", "limitSpeed", "2.0");
            string rendered = document.Render("\r\n");

            runner.True("注释保留", rendered.IndexOf("; 账号与密码注释", StringComparison.Ordinal) >= 0, "缺少注释行");
            runner.True("未知键与行内注释保留",
                rendered.IndexOf("unknownExtra = keepme ; inline note", StringComparison.Ordinal) >= 0, rendered);
            runner.True("未知 section 保留", rendered.IndexOf("[future-section]", StringComparison.Ordinal) >= 0, rendered);
            runner.True("未知 section 的值保留", rendered.IndexOf("weird = value", StringComparison.Ordinal) >= 0, rendered);
            runner.True("目标值已改", rendered.IndexOf("username = new-user", StringComparison.Ordinal) >= 0, rendered);
            runner.True("原值已消失", rendered.IndexOf("old-user", StringComparison.Ordinal) < 0, rendered);
            runner.True("行内注释随目标值保留",
                rendered.IndexOf("limitSpeed = 2.0 ; 文档上限 1.8", StringComparison.Ordinal) >= 0, rendered);

            int originalLines = IniFixture.Replace("\r\n", "\n").Split('\n').Length;
            int renderedLines = rendered.Replace("\r\n", "\n").Split('\n').Length;
            runner.EqualInt("行数不变（没有删除或插入行）", originalLines, renderedLines);

            IniDocument add = IniDocument.Parse(IniFixture);
            add.Set("script-option", "enableAutoCaptcha", "False");
            string added = add.Render("\r\n");
            runner.True("缺失 section 被追加", added.IndexOf("[script-option]", StringComparison.Ordinal) >= 0, added);
            runner.True("追加的键值正确",
                added.IndexOf("enableAutoCaptcha = False", StringComparison.Ordinal) >= 0, added);
            runner.True("原有内容未被破坏", added.IndexOf("[future-section]", StringComparison.Ordinal) >= 0, added);

            IniDocument inline = IniDocument.Parse(IniFixture);
            inline.Set("browser-option", "driver", "Edge");
            string inlineRendered = inline.Render("\r\n");
            runner.True("插入缺失键不影响其它行",
                inlineRendered.IndexOf("unknownExtra = keepme ; inline note", StringComparison.Ordinal) >= 0, inlineRendered);
        }

        private static void IniEncoding(Runner runner)
        {
            runner.Section("配置编码保持（UTF-8 / GBK）");

            string directory = Temp.NewDirectory("encoding");
            string utf8File = Path.Combine(directory, "utf8.ini");
            File.WriteAllText(utf8File, IniFixture, new UTF8Encoding(false));
            TextFile.Snapshot utf8 = TextFile.Read(utf8File);
            runner.EqualInt("UTF-8 识别", 65001, utf8.Encoding.CodePage);
            runner.True("UTF-8 无 BOM", !utf8.HasBom, "HasBom=" + utf8.HasBom);

            string gbkFile = Path.Combine(directory, "gbk.ini");
            Encoding gbk = Encoding.GetEncoding(936);
            File.WriteAllBytes(gbkFile, gbk.GetBytes(IniFixture));
            TextFile.Snapshot gbkSnapshot = TextFile.Read(gbkFile);
            runner.EqualInt("GBK 识别", 936, gbkSnapshot.Encoding.CodePage);
            runner.True("GBK 中文解码正确",
                gbkSnapshot.Text.IndexOf("账号与密码注释", StringComparison.Ordinal) >= 0, gbkSnapshot.Text);

            IniDocument gbkDocument = IniDocument.Parse(gbkSnapshot.Text);
            gbkDocument.Set("user-account", "username", "新用户");
            TextFile.Write(gbkSnapshot, gbkDocument.Render(gbkSnapshot.NewLine));
            string reDecoded = gbk.GetString(File.ReadAllBytes(gbkFile));
            runner.True("GBK 写回后仍是 GBK", reDecoded.IndexOf("账号与密码注释", StringComparison.Ordinal) >= 0, reDecoded);
            runner.True("GBK 写回后值已更新", reDecoded.IndexOf("username = 新用户", StringComparison.Ordinal) >= 0, reDecoded);
        }

        private static void AutovisorSave(Runner runner)
        {
            runner.Section("Autovisor 设置保存（密码策略 + 备份）");

            string directory = Temp.NewDirectory("autovisor");
            string configPath = Path.Combine(directory, "config.ini");
            File.WriteAllText(configPath, IniFixture, new UTF8Encoding(false));
            BackupService backups = new BackupService(Path.Combine(directory, "backups"));

            AutovisorSettings settings = AutovisorSettings.Load(configPath);
            runner.True("加载时识别到已保存密码", settings.PasswordConfigured, "PasswordConfigured=false");
            runner.EqualInt("课程链接数量为 0", 0, settings.CourseUrls.Count);
            runner.True("加载产生 warn（无课程链接）", settings.Warnings.Count > 0, "Warnings=" + settings.Warnings.Count);

            settings.Username = "u2";
            settings.Driver = "Edge";
            settings.LimitSpeed = 1.25;
            settings.CourseUrls = new List<string>(new string[] { "https://a.example/course/1", "https://b.example/course/2" });
            string backup = settings.Save(configPath, backups, SecretChange.Keep, string.Empty);

            runner.True("保存前已生成备份", !string.IsNullOrEmpty(backup) && File.Exists(backup), "backup=" + backup);
            string afterKeep = File.ReadAllText(configPath, new UTF8Encoding(false));
            runner.True("空密码不覆盖原密码", afterKeep.IndexOf("password = secret123", StringComparison.Ordinal) >= 0, afterKeep);
            runner.True("未知键仍保留", afterKeep.IndexOf("unknownExtra = keepme", StringComparison.Ordinal) >= 0, afterKeep);
            runner.True("课程链接写入 URL1..URL2",
                afterKeep.IndexOf("URL1 = https://a.example/course/1", StringComparison.Ordinal) >= 0 &&
                afterKeep.IndexOf("URL2 = https://b.example/course/2", StringComparison.Ordinal) >= 0, afterKeep);
            runner.True("倍速写入", afterKeep.IndexOf("limitSpeed = 1.25", StringComparison.Ordinal) >= 0, afterKeep);

            AutovisorSettings second = AutovisorSettings.Load(configPath);
            second.Save(configPath, backups, SecretChange.Clear, string.Empty);
            string afterClear = File.ReadAllText(configPath, new UTF8Encoding(false));
            runner.True("显式清除后密码为空", afterClear.IndexOf("password = ", StringComparison.Ordinal) >= 0 &&
                                             afterClear.IndexOf("secret123", StringComparison.Ordinal) < 0, afterClear);

            AutovisorSettings third = AutovisorSettings.Load(configPath);
            third.Save(configPath, backups, SecretChange.Replace, "brand-new");
            string afterReplace = File.ReadAllText(configPath, new UTF8Encoding(false));
            runner.True("替换写入新密码", afterReplace.IndexOf("password = brand-new", StringComparison.Ordinal) >= 0, afterReplace);
        }

        private const string JsonFixture =
            "{\r\n" +
            "  \"timeout\": 60,\r\n" +
            "  \"unknown_field\": {\r\n" +
            "    \"nested\": [\r\n" +
            "      1,\r\n" +
            "      2\r\n" +
            "    ],\r\n" +
            "    \"note\": \"保留我\"\r\n" +
            "  },\r\n" +
            "  \"api_key\": \"sk-existing-key\",\r\n" +
            "  \"base_url\": \"https://api.deepseek.com\",\r\n" +
            "  \"model\": \"deepseek-flash\",\r\n" +
            "  \"learning_strategy\": \"learn_all_compusory_course\",\r\n" +
            "  \"max_tokens\": 2000,\r\n" +
            "  \"temperature\": 0.3,\r\n" +
            "  \"interval_ms\": 3000,\r\n" +
            "  \"whisper_enabled\": true,\r\n" +
            "  \"whisper_model\": \"base\",\r\n" +
            "  \"whisper_language\": \"auto\"\r\n" +
            "}\r\n";

        private static void JsonLossless(Runner runner)
        {
            runner.Section("JSON 无损编辑（未知键/顺序/数字写法）");

            string directory = Temp.NewDirectory("json");
            string configPath = Path.Combine(directory, "config.json");
            File.WriteAllText(configPath, JsonFixture, new UTF8Encoding(false));
            BackupService backups = new BackupService(Path.Combine(directory, "backups"));

            UnipusSettings settings = UnipusSettings.Load(configPath);
            runner.Equal("读取 base_url", "https://api.deepseek.com", settings.BaseUrl);
            runner.Equal("读取 model", "deepseek-flash", settings.Model);
            runner.EqualInt("读取 max_tokens", 2000, settings.MaxTokens);
            runner.EqualDouble("读取 temperature", 0.3, settings.Temperature, 0.0001);
            runner.True("识别到已保存 api_key", settings.ApiKeyConfigured, "ApiKeyConfigured=false");

            settings.Model = "deepseek-chat";
            settings.MaxTokens = 4096;
            settings.Temperature = 0.7;
            settings.IntervalMs = 1500;
            settings.WhisperEnabled = false;

            string backup = settings.Save(backups, SecretChange.Keep, string.Empty);
            runner.True("保存前已生成备份", !string.IsNullOrEmpty(backup) && File.Exists(backup), "backup=" + backup);

            string after = File.ReadAllText(configPath, new UTF8Encoding(false));
            JsonDocument document = JsonDocument.Parse(after);
            JsonValue root = document.Root;

            runner.True("未知嵌套键保留", root.Get("unknown_field") != null, "unknown_field 丢失");
            JsonValue nested = root.Get("unknown_field") == null ? null : root.Get("unknown_field").Get("nested");
            runner.True("未知数组保留", nested != null && nested.Kind == JsonKind.Array && nested.Items.Count == 2,
                nested == null ? "nested 丢失" : "count=" + nested.Items.Count);
            runner.Equal("未知中文字符串保留",
                "保留我", root.Get("unknown_field") == null ? "" : root.Get("unknown_field").Get("note").AsString(""));

            runner.Equal("键顺序保持（第一个键仍是 timeout）", "timeout", root.Members[0].Key);
            runner.True("api_key 留空时保留原值",
                after.IndexOf("\"sk-existing-key\"", StringComparison.Ordinal) >= 0, after);
            runner.True("新 model 已写入", after.IndexOf("\"model\": \"deepseek-chat\"", StringComparison.Ordinal) >= 0, after);
            runner.True("数字写法保留为整数 4096", after.IndexOf("\"max_tokens\": 4096", StringComparison.Ordinal) >= 0, after);
            runner.True("temperature 写成 0.7", after.IndexOf("\"temperature\": 0.7", StringComparison.Ordinal) >= 0, after);
            runner.True("布尔值写成 false", after.IndexOf("\"whisper_enabled\": false", StringComparison.Ordinal) >= 0, after);
            runner.True("结尾换行保留", after.EndsWith("\r\n", StringComparison.Ordinal), "没有结尾换行");

            JsonDocument roundTrip = JsonDocument.Parse(after);
            runner.EqualInt("再次解析键数量一致", root.Members.Count, roundTrip.Root.Members.Count);
        }

        private static void UnipusSecretPolicy(Runner runner)
        {
            runner.Section("空密钥保留 / 显式清除 / 类型检查");

            string directory = Temp.NewDirectory("secret");
            string configPath = Path.Combine(directory, "config.json");
            File.WriteAllText(configPath, JsonFixture, new UTF8Encoding(false));
            BackupService backups = new BackupService(Path.Combine(directory, "backups"));

            UnipusSettings keep = UnipusSettings.Load(configPath);
            keep.Save(backups, SecretChange.Keep, "ignored-because-keep");
            string afterKeep = File.ReadAllText(configPath, new UTF8Encoding(false));
            runner.True("Keep 时忽略新值、保留旧密钥",
                afterKeep.IndexOf("\"sk-existing-key\"", StringComparison.Ordinal) >= 0, afterKeep);

            UnipusSettings replace = UnipusSettings.Load(configPath);
            replace.Save(backups, SecretChange.Replace, "sk-new-value");
            string afterReplace = File.ReadAllText(configPath, new UTF8Encoding(false));
            runner.True("Replace 写入新密钥",
                afterReplace.IndexOf("\"sk-new-value\"", StringComparison.Ordinal) >= 0, afterReplace);

            UnipusSettings clear = UnipusSettings.Load(configPath);
            clear.Save(backups, SecretChange.Clear, string.Empty);
            string afterClear = File.ReadAllText(configPath, new UTF8Encoding(false));
            runner.True("Clear 后 api_key 为空串",
                afterClear.IndexOf("\"api_key\": \"\"", StringComparison.Ordinal) >= 0, afterClear);
            runner.True("Clear 后旧密钥不再出现",
                afterClear.IndexOf("sk-new-value", StringComparison.Ordinal) < 0, afterClear);

            UnipusSettings reloaded = UnipusSettings.Load(configPath);
            runner.True("清空后 ApiKeyConfigured=false", !reloaded.ApiKeyConfigured, "ApiKeyConfigured=true");

            string typedPath = Path.Combine(directory, "typed.json");
            File.WriteAllText(typedPath,
                "{ \"max_tokens\": \"2000\", \"temperature\": \"0.5\", \"whisper_enabled\": \"true\" }\r\n",
                new UTF8Encoding(false));
            UnipusSettings typed = UnipusSettings.Load(typedPath);
            runner.True("字符串数字触发类型告警", typed.Warnings.Count >= 2, "Warnings=" + typed.Warnings.Count);
            runner.EqualInt("字符串数字仍被读取", 2000, typed.MaxTokens);
            runner.True("字符串布尔仍被读取", typed.WhisperEnabled, "WhisperEnabled=false");

            string brokenPath = Path.Combine(directory, "broken.json");
            File.WriteAllText(brokenPath, "{ \"a\": }", new UTF8Encoding(false));
            try
            {
                UnipusSettings.Load(brokenPath);
                runner.True("损坏 JSON 会抛出异常", false, "没有抛出");
            }
            catch (Exception error)
            {
                runner.True("损坏 JSON 会抛出异常", error is JsonParseException || error is InvalidDataException,
                    error.GetType().Name);
            }
        }

        private static void BackupRestore(Runner runner)
        {
            runner.Section("备份 / 恢复 / 保留策略");

            string directory = Temp.NewDirectory("backup");
            string target = Path.Combine(directory, "config.ini");
            File.WriteAllText(target, "v1", new UTF8Encoding(false));
            BackupService backups = new BackupService(Path.Combine(directory, "backups"));

            BackupEntry first = backups.Create("autovisor", target);
            runner.True("创建备份成功", first != null && File.Exists(first.FilePath), "first=null");
            runner.Equal("备份内容是当时的快照", "v1", File.ReadAllText(first.FilePath, new UTF8Encoding(false)));

            File.WriteAllText(target, "v2", new UTF8Encoding(false));
            string restoredFrom = backups.Restore("autovisor", first.FilePath, target);
            runner.Equal("恢复后内容回到 v1", "v1", File.ReadAllText(target, new UTF8Encoding(false)));
            runner.True("恢复前先备份了当前文件", !string.IsNullOrEmpty(restoredFrom) && File.Exists(restoredFrom),
                "safety=" + restoredFrom);
            runner.Equal("恢复前的快照是 v2", "v2", File.ReadAllText(restoredFrom, new UTF8Encoding(false)));

            IList<BackupEntry> list = backups.List("autovisor");
            runner.True("列表包含至少两个备份", list.Count >= 2, "count=" + list.Count);
            runner.True("列表按时间倒序", list.Count < 2 || list[0].CreatedUtc >= list[1].CreatedUtc, "顺序不对");

            System.Threading.Thread.Sleep(15);
            backups.Create("autovisor", target);
            System.Threading.Thread.Sleep(15);
            backups.Create("autovisor", target);
            int removed = backups.Prune("autovisor", 2);
            runner.True("保留策略删除了多余备份", removed >= 1, "removed=" + removed);
            runner.EqualInt("保留后只剩 2 份", 2, backups.List("autovisor").Count);

            runner.True("不存在的文件返回 null", backups.Create("autovisor", Path.Combine(directory, "nope.ini")) == null,
                "应当返回 null");
        }

        private static void LogMasking(Runner runner)
        {
            runner.Section("日志脱敏");

            string masked = TimeSaver.Diagnostics.LogService.Mask(
                "api_key=sk-abcdef123456 password: hunter2 Authorization: Bearer abc.def.ghi " +
                "token=0123456789abcdef0123456789abcdef cookie=sessionid123 " +
                "hex=0123456789abcdef0123456789abcdef0123");

            runner.True("api_key 被遮蔽", masked.IndexOf("sk-abcdef123456", StringComparison.Ordinal) < 0, masked);
            runner.True("password 被遮蔽", masked.IndexOf("hunter2", StringComparison.Ordinal) < 0, masked);
            runner.True("Bearer 被遮蔽", masked.IndexOf("abc.def.ghi", StringComparison.Ordinal) < 0, masked);
            runner.True("token 被遮蔽", masked.IndexOf("0123456789abcdef", StringComparison.Ordinal) < 0, masked);
            runner.True("cookie 被遮蔽", masked.IndexOf("sessionid123", StringComparison.Ordinal) < 0, masked);
            runner.True("保留字段名便于排查", masked.IndexOf("api_key", StringComparison.Ordinal) >= 0, masked);
            runner.True("普通文本不受影响",
                TimeSaver.Diagnostics.LogService.Mask("已启动 PID 1234").IndexOf("已启动 PID 1234", StringComparison.Ordinal) >= 0,
                "普通文本被误改");

            string directory = Temp.NewDirectory("log");
            TimeSaver.Diagnostics.LogService log = new TimeSaver.Diagnostics.LogService(directory);
            log.Info("写入 api_key=sk-should-not-appear");
            string content = File.ReadAllText(log.CurrentFile, new UTF8Encoding(false));
            runner.True("日志文件里没有原始密钥",
                content.IndexOf("sk-should-not-appear", StringComparison.Ordinal) < 0, content);
            runner.True("日志文件含时间戳", content.IndexOf("[INFO]", StringComparison.Ordinal) >= 0, content);
        }
    }
}

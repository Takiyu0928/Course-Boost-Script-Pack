using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TimeSaver.Config;
using TimeSaver.Core;
using TimeSaver.Model;

namespace TimeSaver.Tests
{
    /// <summary>
    /// Regression tests for the four P2 findings of the independent final review:
    ///   1. course URL list shrink/clear must remove stale URL2..n lines
    ///   2. NaN / Infinity must never reach config.json (UI, save layer, serializer)
    ///   3. unsaved form drafts survive rebuilds (UI smoke test covers this)
    ///   4. launcher\build\TimeSaver.exe must resolve the install root
    /// </summary>
    public static class RegressionTests
    {
        public static void Run(Runner runner)
        {
            CourseUrlReconciliation(runner);
            NonFiniteNumbers(runner);
            InstallRootResolution(runner);
        }

        /* ---------------------------------------------------------------- 1 */

        private const string UrlFixture =
            "[user-account]\r\n" +
            "; 账号注释必须保留\r\n" +
            "username = u\r\n" +
            "password =\r\n" +
            "\r\n" +
            "[course-url]\r\n" +
            "; 配置要学习的课程链接，可继续添加多个\r\n" +
            "URL1 = https://a.example/course/1\r\n" +
            "URL2 = https://b.example/course/2\r\n" +
            "URL3 = https://c.example/course/3\r\n" +
            "URL4 =\r\n" +
            "URLn =\r\n" +
            "; 末尾注释也要保留\r\n" +
            "unknownExtra = keepme\r\n";

        private static void CourseUrlReconciliation(Runner runner)
        {
            runner.Section("P2-1 课程链接缩短/清空要清掉旧 URL 行");

            string directory = Temp.NewDirectory("url-reconcile");
            string configPath = Path.Combine(directory, "config.ini");
            File.WriteAllText(configPath, UrlFixture, new UTF8Encoding(false));
            BackupService backups = new BackupService(Path.Combine(directory, "backups"));

            AutovisorSettings loaded = AutovisorSettings.Load(configPath);
            runner.EqualInt("读到的课程链接数量（URL4 空值不计）", 3, loaded.CourseUrls.Count);

            // 3 -> 1
            loaded.CourseUrls = new List<string>(new string[] { "https://only.example/keep" });
            loaded.Save(configPath, backups, SecretChange.Keep, string.Empty);
            string afterShrink = File.ReadAllText(configPath, new UTF8Encoding(false));

            runner.True("2->1：URL1 已更新为新值",
                afterShrink.IndexOf("URL1 = https://only.example/keep", StringComparison.Ordinal) >= 0, afterShrink);
            runner.True("2->1：旧 URL2 行被删除",
                afterShrink.IndexOf("URL2", StringComparison.Ordinal) < 0, afterShrink);
            runner.True("2->1：旧 URL3 行被删除",
                afterShrink.IndexOf("URL3", StringComparison.Ordinal) < 0, afterShrink);
            runner.True("2->1：URLn 占位保留",
                afterShrink.IndexOf("URLn =", StringComparison.Ordinal) >= 0, afterShrink);
            runner.True("2->1：URL4 空占位也被清理",
                afterShrink.IndexOf("URL4", StringComparison.Ordinal) < 0, afterShrink);
            runner.True("2->1：未知字段保留",
                afterShrink.IndexOf("unknownExtra = keepme", StringComparison.Ordinal) >= 0, afterShrink);
            runner.True("2->1：section 前的注释保留",
                afterShrink.IndexOf("; 配置要学习的课程链接，可继续添加多个", StringComparison.Ordinal) >= 0, afterShrink);
            runner.True("2->1：section 后的注释保留",
                afterShrink.IndexOf("; 末尾注释也要保留", StringComparison.Ordinal) >= 0, afterShrink);
            runner.True("2->1：账号注释保留",
                afterShrink.IndexOf("; 账号注释必须保留", StringComparison.Ordinal) >= 0, afterShrink);

            AutovisorSettings reloaded = AutovisorSettings.Load(configPath);
            runner.EqualInt("2->1：重新加载后只剩 1 条", 1, reloaded.CourseUrls.Count);
            runner.Equal("2->1：重新加载的值正确", "https://only.example/keep", reloaded.CourseUrls[0]);

            // 1 -> 0
            reloaded.CourseUrls = new List<string>();
            reloaded.Save(configPath, backups, SecretChange.Keep, string.Empty);
            string afterClear = File.ReadAllText(configPath, new UTF8Encoding(false));

            runner.True("2->0：URL1 行被删除", afterClear.IndexOf("URL1", StringComparison.Ordinal) < 0, afterClear);
            runner.True("2->0：URLn 占位仍然保留",
                afterClear.IndexOf("URLn =", StringComparison.Ordinal) >= 0, afterClear);
            runner.True("2->0：[course-url] 段与注释仍然保留",
                afterClear.IndexOf("[course-url]", StringComparison.Ordinal) >= 0 &&
                afterClear.IndexOf("; 配置要学习的课程链接，可继续添加多个", StringComparison.Ordinal) >= 0, afterClear);
            runner.True("2->0：其它段未受影响",
                afterClear.IndexOf("[user-account]", StringComparison.Ordinal) >= 0 &&
                afterClear.IndexOf("username = u", StringComparison.Ordinal) >= 0, afterClear);

            AutovisorSettings emptied = AutovisorSettings.Load(configPath);
            runner.EqualInt("2->0：重新加载后 0 条", 0, emptied.CourseUrls.Count);
            runner.True("2->0：重新加载后仍有空链接告警", emptied.Warnings.Count > 0, "Warnings=0");

            // 0 -> 2（重新增长也必须正确）
            emptied.CourseUrls = new List<string>(new string[] { "https://x.example/1", "https://y.example/2" });
            emptied.Save(configPath, backups, SecretChange.Keep, string.Empty);
            AutovisorSettings grown = AutovisorSettings.Load(configPath);
            runner.EqualInt("0->2：重新增长后 2 条", 2, grown.CourseUrls.Count);
            runner.Equal("0->2：第一条正确", "https://x.example/1", grown.CourseUrls[0]);
            runner.Equal("0->2：第二条正确", "https://y.example/2", grown.CourseUrls[1]);
            string grownText = File.ReadAllText(configPath, new UTF8Encoding(false));
            runner.True("0->2：URLn 占位未被当成第 3 条",
                grownText.IndexOf("URLn =", StringComparison.Ordinal) >= 0, grownText);
            runner.True("0->2：没有产生 URL3",
                grownText.IndexOf("URL3", StringComparison.Ordinal) < 0, grownText);
        }

        /* ---------------------------------------------------------------- 2 */

        private static void NonFiniteNumbers(Runner runner)
        {
            runner.Section("P2-2 NaN / Infinity 不能写坏配置");

            double value;
            runner.True("拒绝 NaN", !NumberInput.TryParseFinite("NaN", out value), "NaN 被接受");
            runner.True("拒绝 Infinity", !NumberInput.TryParseFinite("Infinity", out value), "Infinity 被接受");
            runner.True("拒绝 -Infinity", !NumberInput.TryParseFinite("-Infinity", out value), "-Infinity 被接受");
            runner.True("拒绝溢出 1e400", !NumberInput.TryParseFinite("1e400", out value), "1e400 被接受");
            runner.True("接受 0.7", NumberInput.TryParseFinite("0.7", out value), "0.7 被拒绝");
            runner.EqualDouble("0.7 解析正确", 0.7, value, 0.0001);
            runner.True("接受带空格的 2", NumberInput.TryParseFinite("  2  ", out value), "带空格数字被拒绝");
            runner.True("区间校验拒绝 NaN", !NumberInput.TryParseFiniteRange("NaN", 0, 2, out value), "NaN 通过区间");
            runner.True("区间校验拒绝超界", !NumberInput.TryParseFiniteRange("3", 0, 2, out value), "3 通过区间");

            int intValue;
            runner.True("拒绝 int 溢出", !NumberInput.TryParseInt32("99999999999", out intValue), "溢出被接受");
            runner.True("拒绝空串", !NumberInput.TryParseInt32("", out intValue), "空串被接受");
            runner.True("接受 4096", NumberInput.TryParseInt32("4096", out intValue) && intValue == 4096, "4096 被拒绝");

            runner.True("JsonValue.NewNumber 拒绝 NaN", Throws(delegate { JsonValue.NewNumber(double.NaN); }), "没有抛异常");
            runner.True("JsonValue.NewNumber 拒绝 +Infinity", Throws(delegate { JsonValue.NewNumber(double.PositiveInfinity); }), "没有抛异常");
            runner.True("JsonValue.NewNumber 拒绝 -Infinity", Throws(delegate { JsonValue.NewNumber(double.NegativeInfinity); }), "没有抛异常");
            runner.True("JsonValue.NewNumberRaw 拒绝 NaN 文本", Throws(delegate { JsonValue.NewNumberRaw("NaN"); }), "没有抛异常");
            runner.True("JsonValue.NewNumberRaw 接受 2000", !Throws(delegate { JsonValue.NewNumberRaw("2000"); }), "抛异常了");
            JsonValue finite = JsonValue.NewNumber(0.7);
            runner.Equal("合法数值序列化", "0.7", finite.Text);

            // 保存层：NaN 必须抛错且不改动文件
            string directory = Temp.NewDirectory("nan-guard");
            string configPath = Path.Combine(directory, "config.json");
            File.WriteAllText(configPath,
                "{ \"base_url\": \"https://api.deepseek.com\", \"model\": \"m\", \"temperature\": 0.3, " +
                "\"api_key\": \"sk-keep\", \"max_tokens\": 2000, \"interval_ms\": 3000 }\r\n",
                new UTF8Encoding(false));
            byte[] beforeBytes = File.ReadAllBytes(configPath);
            BackupService backups = new BackupService(Path.Combine(directory, "backups"));

            UnipusSettings settings = UnipusSettings.Load(configPath);
            settings.Temperature = double.NaN;
            runner.True("保存层拒绝 NaN temperature",
                Throws(delegate { settings.Save(backups, SecretChange.Keep, string.Empty); }), "没有抛异常");
            byte[] afterBytes = File.ReadAllBytes(configPath);
            runner.True("被拒绝后配置文件字节不变", BytesEqual(beforeBytes, afterBytes), "文件被改动了");

            settings.Temperature = double.PositiveInfinity;
            runner.True("保存层拒绝 +Infinity temperature",
                Throws(delegate { settings.Save(backups, SecretChange.Keep, string.Empty); }), "没有抛异常");

            settings.MaxTokens = 0;
            runner.True("保存层拒绝非法 max_tokens",
                Throws(delegate { settings.Save(backups, SecretChange.Keep, string.Empty); }), "没有抛异常");
            settings.MaxTokens = 2000;

            settings.IntervalMs = -1;
            runner.True("保存层拒绝负 interval_ms",
                Throws(delegate { settings.Save(backups, SecretChange.Keep, string.Empty); }), "没有抛异常");

            // 合法值仍可往返
            settings.Temperature = 0.75;
            settings.IntervalMs = 2500;
            settings.Save(backups, SecretChange.Keep, string.Empty);
            string after = File.ReadAllText(configPath, new UTF8Encoding(false));
            runner.True("合法值写入后仍是有效 JSON", !Throws(delegate { JsonDocument.Parse(after); }), after);
            runner.True("temperature 写为 0.75", after.IndexOf("\"temperature\": 0.75", StringComparison.Ordinal) >= 0, after);
            runner.True("interval_ms 写为 2500", after.IndexOf("\"interval_ms\": 2500", StringComparison.Ordinal) >= 0, after);
            UnipusSettings roundTrip = UnipusSettings.Load(configPath);
            runner.EqualDouble("temperature 往返", 0.75, roundTrip.Temperature, 0.0001);
            runner.EqualInt("interval_ms 往返", 2500, roundTrip.IntervalMs);
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch
            {
                return true;
            }
        }

        /* ---------------------------------------------------------------- 4 */

        private static void InstallRootResolution(Runner runner)
        {
            runner.Section("P2-4 build 子目录入口要解析到安装根");

            // 路径里故意同时包含空格、中文、单引号
            string root = Path.Combine(Path.GetTempPath(),
                "TimeSaver 根 目录 " + Guid.NewGuid().ToString("N").Substring(0, 6) + " Example User's");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "launcher"));
            Directory.CreateDirectory(Path.Combine(root, "launcher", "build"));
            Directory.CreateDirectory(Path.Combine(root, "domooc_free"));
            string unrelated = Temp.NewDirectory("unrelated");

            PathResolver fromRoot = PathResolver.FromStartDirectory(root);
            runner.Equal("根目录入口解析到自身", Path.GetFullPath(root), fromRoot.InstallRoot);

            PathResolver fromLauncher = PathResolver.FromStartDirectory(Path.Combine(root, "launcher"));
            runner.Equal("launcher 目录向上解析到根", Path.GetFullPath(root), fromLauncher.InstallRoot);

            PathResolver fromBuild = PathResolver.FromStartDirectory(Path.Combine(root, "launcher", "build"));
            runner.Equal("launcher\\build 向上解析到根", Path.GetFullPath(root), fromBuild.InstallRoot);

            // 从工具子目录启动时同样解析到安装根（仍在已知布局内，不属于“盲目遍历”）
            PathResolver fromToolDir = PathResolver.FromStartDirectory(Path.Combine(root, "domooc_free"));
            runner.Equal("工具子目录向上解析到安装根", Path.GetFullPath(root), fromToolDir.InstallRoot);

            PathResolver fromUnrelated = PathResolver.FromStartDirectory(unrelated);
            runner.Equal("无关目录不会被误认成安装根", Path.GetFullPath(unrelated), fromUnrelated.InstallRoot);

            runner.True("识别安装根", PathResolver.LooksLikeInstallRoot(root), root);
            runner.True("不识别无关目录", !PathResolver.LooksLikeInstallRoot(unrelated), unrelated);
            runner.True("不存在的目录不识别", !PathResolver.LooksLikeInstallRoot(Path.Combine(root, "nope")), "被误判");

            // build 入口下的工具路径必须指向根目录，而不是 build
            string toolPath = fromBuild.Combine("domooc_free");
            runner.True("build 入口下的工具路径在根目录内",
                toolPath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) &&
                toolPath.IndexOf("build", StringComparison.OrdinalIgnoreCase) < 0, toolPath);

            // 端到端：测试进程本身就运行在 launcher\build\ 下，正好验证真实的 FromExecutableDirectory()
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (baseDir.EndsWith(Path.Combine("launcher", "build"), StringComparison.OrdinalIgnoreCase))
            {
                string expectedRoot = Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
                PathResolver actual = PathResolver.FromExecutableDirectory();
                runner.Equal("从 launcher\\build 实际运行时解析到安装根", expectedRoot, actual.InstallRoot);
                runner.Equal("实际解析后的工具路径指向安装根（无需安装第三方工具）",
                    Path.Combine(expectedRoot, "domooc_free"), actual.Combine("domooc_free"));
            }

            Directory.Delete(root, true);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TimeSaver.Config;
using TimeSaver.Core;

namespace TimeSaver.Model
{
    public sealed class ToolPathCandidate
    {
        public string Directory;
        public string MainRelativePath;
        public override string ToString() { return Directory; }
    }

    /// <summary>Bounded file-only discovery; never executes a candidate or follows directory links.</summary>
    public static class ToolPathProbe
    {
        private static string StorePath(PathResolver paths) { return Path.Combine(paths.LauncherDirectory, "tool-paths.json"); }

        public static ToolPathCandidate Inspect(ToolDefinition tool, string directory)
        {
            if (tool == null || string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory)) return null;
            directory = Path.GetFullPath(directory);
            ToolPathCandidate result = new ToolPathCandidate { Directory = directory };
            if (tool.Id == "xuexitong")
            {
                result.MainRelativePath = File.Exists(Path.Combine(directory, "main.exe")) ? "main.exe" : @"task\main.exe";
                if (!File.Exists(Path.Combine(directory, "start.exe")) ||
                    !File.Exists(Path.Combine(directory, result.MainRelativePath)) ||
                    !File.Exists(Path.Combine(directory, @"task\tool\account_info.json"))) return null;
            }
            else
            {
                foreach (ToolEntryPoint entry in tool.Entries)
                    if (!File.Exists(Path.Combine(directory, entry.ExeRelativePath))) return null;
                foreach (string required in tool.RequiredFiles)
                    if (!File.Exists(Path.Combine(directory, required)) && !System.IO.Directory.Exists(Path.Combine(directory, required))) return null;
                if (tool.Entries.Count == 0) return null;
            }
            return result;
        }

        public static List<ToolPathCandidate> Find(ToolDefinition tool, string searchRoot)
        {
            List<ToolPathCandidate> found = new List<ToolPathCandidate>();
            if (!System.IO.Directory.Exists(searchRoot)) return found;
            Queue<KeyValuePair<string, int>> pending = new Queue<KeyValuePair<string, int>>();
            pending.Enqueue(new KeyValuePair<string, int>(Path.GetFullPath(searchRoot), 0));
            int visited = 0;
            while (pending.Count > 0 && visited++ < 1000 && found.Count < 50)
            {
                KeyValuePair<string, int> item = pending.Dequeue();
                try
                {
                    if ((File.GetAttributes(item.Key) & FileAttributes.ReparsePoint) != 0) continue;
                    ToolPathCandidate candidate = Inspect(tool, item.Key);
                    if (candidate != null) { found.Add(candidate); continue; }
                    if (item.Value >= 4) continue;
                    foreach (string child in System.IO.Directory.EnumerateDirectories(item.Key))
                    {
                        string name = Path.GetFileName(child).ToLowerInvariant();
                        if (name == "profile" || name == "_internal" || name == "node_modules" || name == ".git" || name == "launcher" || name.StartsWith(".model")) continue;
                        if (pending.Count >= 1000) break;
                        pending.Enqueue(new KeyValuePair<string, int>(child, item.Value + 1));
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            return found;
        }

        public static void Use(ToolDefinition tool, ToolPathCandidate candidate)
        {
            tool.Directory = candidate.Directory;
            if (tool.Id == "xuexitong")
            {
                ToolEntryPoint main = tool.FindEntry("main");
                string previous = main.ExeRelativePath;
                main.ExeRelativePath = candidate.MainRelativePath;
                for (int i = 0; i < tool.RequiredFiles.Length; i++)
                    if (tool.RequiredFiles[i] == previous) tool.RequiredFiles[i] = candidate.MainRelativePath;
            }
        }

        public static void Save(PathResolver paths, ToolDefinition tool, ToolPathCandidate candidate, BackupService backups)
        {
            candidate = Inspect(tool, candidate.Directory);
            if (candidate == null) throw new InvalidDataException("所选路径的启动文件或配置已缺失，请重新探测。");
            string path = StorePath(paths);
            JsonDocument document = File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)) : JsonDocument.Parse("{}");
            if (document.Root == null || document.Root.Kind != JsonKind.Object) throw new InvalidDataException("路径配置必须是 JSON 对象。");
            if (File.Exists(path) && backups != null) backups.Create("tool-paths", path);
            document.Root.Set(tool.Id, JsonValue.NewString(candidate.Directory));
            string temp = path + ".timesaver-tmp";
            File.WriteAllText(temp, document.Render(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null, true);
            else File.Move(temp, path);
            Use(tool, candidate);
        }

        public static void ApplySaved(PathResolver paths, ToolCatalog catalog)
        {
            string path = StorePath(paths);
            if (!File.Exists(path)) return;
            try
            {
                JsonValue root = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)).Root;
                if (root == null || root.Kind != JsonKind.Object) throw new InvalidDataException();
                foreach (ToolDefinition tool in catalog.Tools)
                {
                    JsonValue stored = root.Get(tool.Id);
                    if (stored == null || stored.Kind != JsonKind.String || string.IsNullOrEmpty(stored.Text)) continue;
                    if (!Path.IsPathRooted(stored.Text)) { tool.Warnings.Add("已保存的启动路径不是绝对路径，请重新探测。"); continue; }
                    tool.Directory = stored.Text;
                    ToolPathCandidate candidate = Inspect(tool, stored.Text);
                    if (candidate != null) Use(tool, candidate);
                    else tool.Warnings.Add("已保存的启动路径缺少必需文件，请使用“探测启动路径”重新选择。");
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is JsonParseException) && !(error is ArgumentException)) throw;
                foreach (ToolDefinition tool in catalog.Tools) tool.Warnings.Add("无法读取 launcher/tool-paths.json，请检查格式或文件权限。");
            }
        }
    }
}

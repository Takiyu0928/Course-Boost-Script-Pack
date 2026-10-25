using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TimeSaver.Core
{
    public sealed class BackupEntry
    {
        public string FilePath;
        public string ToolId;
        public string OriginalName;
        public DateTime CreatedUtc;
        public long Size;

        public string DisplayName
        {
            get
            {
                DateTime local = CreatedUtc.ToLocalTime();
                return local.ToString("yyyy-MM-dd HH:mm:ss") + "  " + OriginalName + "  (" + Size + " B)";
            }
        }
    }

    /// <summary>
    /// Timestamped backups of the config files the user edits, living under launcher\backups.
    ///
    /// Rules: a backup is written *before* any save and before any restore, the original file is
    /// replaced atomically, and pruning keeps the newest N entries so the folder cannot grow
    /// without bound. Nothing outside launcher\backups is ever deleted.
    /// </summary>
    public sealed class BackupService
    {
        private readonly string _root;

        public BackupService(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory)) throw new ArgumentNullException("rootDirectory");
            _root = rootDirectory;
            if (!Directory.Exists(_root)) Directory.CreateDirectory(_root);
        }

        public string RootDirectory { get { return _root; } }

        public BackupEntry Create(string toolId, string sourcePath)
        {
            if (string.IsNullOrEmpty(toolId)) throw new ArgumentNullException("toolId");
            if (string.IsNullOrEmpty(sourcePath)) throw new ArgumentNullException("sourcePath");
            if (!File.Exists(sourcePath)) return null;

            string directory = Path.Combine(_root, Sanitize(toolId));
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            string originalName = Path.GetFileName(sourcePath);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            string target = Path.Combine(directory, originalName + "." + stamp + ".bak");

            int attempt = 0;
            while (File.Exists(target))
            {
                attempt++;
                target = Path.Combine(directory, originalName + "." + stamp + "-" + attempt + ".bak");
            }

            File.Copy(sourcePath, target, false);
            FileInfo info = new FileInfo(target);

            BackupEntry entry = new BackupEntry();
            entry.FilePath = target;
            entry.ToolId = toolId;
            entry.OriginalName = originalName;
            entry.CreatedUtc = info.LastWriteTimeUtc;
            entry.Size = info.Length;
            return entry;
        }

        public IList<BackupEntry> List(string toolId)
        {
            List<BackupEntry> entries = new List<BackupEntry>();
            string directory = Path.Combine(_root, Sanitize(toolId));
            if (!Directory.Exists(directory)) return entries;

            foreach (string file in Directory.GetFiles(directory, "*.bak"))
            {
                FileInfo info = new FileInfo(file);
                BackupEntry entry = new BackupEntry();
                entry.FilePath = file;
                entry.ToolId = toolId;
                entry.OriginalName = ExtractOriginalName(info.Name);
                entry.CreatedUtc = info.LastWriteTimeUtc;
                entry.Size = info.Length;
                entries.Add(entry);
            }
            entries.Sort(delegate(BackupEntry left, BackupEntry right)
            {
                return right.CreatedUtc.CompareTo(left.CreatedUtc);
            });
            return entries;
        }

        private static string ExtractOriginalName(string backupFileName)
        {
            // "<original>.<stamp>.bak" -> "<original>"
            string withoutExtension = backupFileName.Substring(0, backupFileName.Length - 4);
            int lastDot = withoutExtension.LastIndexOf('.');
            if (lastDot <= 0) return withoutExtension;
            int previousDot = withoutExtension.LastIndexOf('.', lastDot - 1);
            if (previousDot <= 0) return withoutExtension.Substring(0, lastDot);
            return withoutExtension.Substring(0, previousDot);
        }

        /// <summary>
        /// Restores a backup over the live file. The live file is backed up first (tagged
        /// "before-restore") so a wrong restore is itself undoable.
        /// </summary>
        public string Restore(string toolId, string backupFilePath, string targetPath)
        {
            if (!File.Exists(backupFilePath)) throw new FileNotFoundException("备份文件不存在", backupFilePath);
            if (string.IsNullOrEmpty(targetPath)) throw new ArgumentNullException("targetPath");

            string safety = string.Empty;
            if (File.Exists(targetPath))
            {
                BackupEntry current = Create(toolId, targetPath);
                if (current != null) safety = current.FilePath;
            }

            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                throw new DirectoryNotFoundException("目标目录不存在: " + directory);

            string temp = targetPath + ".timesaver-restore";
            File.Copy(backupFilePath, temp, true);
            if (File.Exists(targetPath)) File.Replace(temp, targetPath, null, true);
            else File.Move(temp, targetPath);
            return safety;
        }

        public int Prune(string toolId, int keep)
        {
            if (keep < 1) keep = 1;
            IList<BackupEntry> entries = List(toolId);
            int removed = 0;
            for (int i = keep; i < entries.Count; i++)
            {
                try
                {
                    File.Delete(entries[i].FilePath);
                    removed++;
                }
                catch
                {
                    // keep going: a locked backup is not fatal
                }
            }
            return removed;
        }

        private static string Sanitize(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') builder.Append(c);
                else builder.Append('_');
            }
            return builder.ToString();
        }
    }
}

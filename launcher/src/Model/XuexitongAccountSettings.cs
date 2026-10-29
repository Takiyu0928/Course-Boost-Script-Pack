using System;
using System.IO;
using System.Text;
using TimeSaver.Config;
using TimeSaver.Core;

namespace TimeSaver.Model
{
    public sealed class XuexitongAccountSettings
    {
        public string Username;
        public bool PasswordConfigured;
        private string _path;

        public static XuexitongAccountSettings Load(string path)
        {
            JsonValue root = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)).Root;
            if (root == null || root.Kind != JsonKind.Object) throw new InvalidDataException("账号配置必须是 JSON 对象。");
            JsonValue user = root.Get("phone_number");
            JsonValue password = root.Get("password");
            if (user != null && user.Kind != JsonKind.String) throw new InvalidDataException("phone_number 必须是字符串。");
            if (password != null && password.Kind != JsonKind.String) throw new InvalidDataException("password 必须是字符串。");
            return new XuexitongAccountSettings
            {
                _path = path,
                Username = user == null ? string.Empty : user.Text,
                PasswordConfigured = password != null && !string.IsNullOrEmpty(password.Text)
            };
        }

        public string Save(BackupService backups, string username, SecretChange change, string password)
        {
            username = (username ?? string.Empty).Trim();
            if (username.Length == 0) throw new ArgumentException("请填写学习通账号。");
            XuexitongAccountSettings latest = Load(_path);
            bool switching = latest.Username != username;
            if ((switching || change == SecretChange.Replace) && (change != SecretChange.Replace || string.IsNullOrEmpty(password)))
                throw new ArgumentException("更换账号时请同时填写新密码。");
            JsonDocument document = JsonDocument.Parse(File.ReadAllText(_path, Encoding.UTF8));
            JsonValue root = document.Root;
            string backup = string.Empty;
            if (backups != null)
            {
                BackupEntry entry = backups.Create("xuexitong", _path);
                if (entry != null) backup = entry.FilePath;
            }
            root.Set("phone_number", JsonValue.NewString(username));
            if (change == SecretChange.Replace) root.Set("password", JsonValue.NewString(password));
            else if (change == SecretChange.Clear) root.Set("password", JsonValue.NewString(string.Empty));
            if (switching)
            {
                // Course selections belong to the previous account; keep API and other preferences.
                foreach (string key in new string[] { "cour", "file_name" })
                    root.Set(key, JsonDocument.Parse("[]").Root);
            }
            string temp = _path + ".timesaver-tmp";
            File.WriteAllText(temp, document.Render(), new UTF8Encoding(false));
            File.Replace(temp, _path, null, true);
            return backup;
        }
    }
}

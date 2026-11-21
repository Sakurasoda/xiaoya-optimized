using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace XiaoYaOpt
{
    /// <summary>簡單的 key=value 設定檔（UTF-8），存在 exe 同目錄。</summary>
    internal sealed class Settings
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _path;

        public Settings(string path)
        {
            _path = path;
            if (!File.Exists(path)) return;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                _values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }

        public string Get(string key, string fallback)
        {
            string v;
            return _values.TryGetValue(key, out v) ? v : fallback;
        }

        public int GetInt(string key, int fallback)
        {
            int v;
            return int.TryParse(Get(key, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public bool GetBool(string key, bool fallback)
        {
            string v = Get(key, null);
            if (v == null) return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public void Set(string key, string value) { _values[key] = value ?? string.Empty; }
        public void Set(string key, int value) { _values[key] = value.ToString(CultureInfo.InvariantCulture); }
        public void Set(string key, bool value) { _values[key] = value ? "1" : "0"; }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 小雅優化版設定檔");
            var keys = new List<string>(_values.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string k in keys) sb.Append(k).Append('=').AppendLine(_values[k]);
            File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace XiaoYaOpt
{
    internal sealed class SkillInfo
    {
        public string ZhName;
        public string EnName;
        public int Level;
        /// <summary>秒；官網沒有資料時為 null。</summary>
        public double? CastSec;
        public double? CooldownSec;
        public double? DurationSec;
        /// <summary>官網的技能類型，例如 Active self-buff、Active skill、Passive。</summary>
        public string Type = "";

        public bool IsBuff
        {
            get { return Type.IndexOf("buff", StringComparison.OrdinalIgnoreCase) >= 0; }
        }
    }

    /// <summary>
    /// 讀取「技能表.csv」（由遊戲檔案與 Nekogame 官網整合而成，放在使用者本機）。
    /// 以英文技能名 + 等級查詢，對應遊戲快捷列裡的「/useskill 名稱 等級」。
    /// </summary>
    internal sealed class SkillDb
    {
        private readonly Dictionary<string, SkillInfo> _byKey = new Dictionary<string, SkillInfo>();

        public int Count { get { return _byKey.Count; } }
        public string Path { get; private set; }

        public static SkillDb Load(string path)
        {
            var db = new SkillDb { Path = path };
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                List<string> header = ReadRow(reader);
                if (header == null) throw new InvalidDataException("技能表是空的");
                int cId = header.IndexOf("技能ID"), cLv = header.IndexOf("等級"), cZh = header.IndexOf("中文名稱"),
                    cOrig = header.IndexOf("原文名稱"), cWikiEn = header.IndexOf("官網英文名"),
                    cCast = header.IndexOf("詠唱時間(秒)"), cCd = header.IndexOf("冷卻時間(秒)"), cDur = header.IndexOf("持續時間(秒)"),
                    cType = header.IndexOf("類型");
                if (cLv < 0 || cZh < 0 || cOrig < 0 || cWikiEn < 0 || cCast < 0 || cCd < 0 || cDur < 0)
                    throw new InvalidDataException("技能表缺少必要欄位（等級、中文名稱、原文名稱、官網英文名、詠唱/冷卻/持續時間）");

                List<string> row;
                while ((row = ReadRow(reader)) != null)
                {
                    if (row.Count < header.Count) continue;
                    int lv;
                    if (!int.TryParse(row[cLv], out lv)) continue;
                    var info = new SkillInfo
                    {
                        ZhName = row[cZh],
                        EnName = row[cWikiEn].Length > 0 ? row[cWikiEn] : row[cOrig],
                        Level = lv,
                        CastSec = ParseSec(row[cCast]),
                        CooldownSec = ParseSec(row[cCd]),
                        DurationSec = ParseSec(row[cDur]),
                        Type = cType >= 0 ? row[cType] : ""
                    };
                    // 官網英文名與遊戲原文名都建索引；有官網時間資料的列優先。
                    db.Add(row[cWikiEn], lv, info);
                    db.Add(row[cOrig], lv, info);
                }
            }
            return db;
        }

        private void Add(string name, int level, SkillInfo info)
        {
            string key = Key(name, level);
            if (key == null) return;
            SkillInfo existing;
            if (_byKey.TryGetValue(key, out existing) && HasTimes(existing) && !HasTimes(info)) return;
            if (existing != null && HasTimes(existing) && HasTimes(info)) return; // 保留第一筆
            _byKey[key] = info;
        }

        private static bool HasTimes(SkillInfo s)
        {
            return s.CastSec.HasValue || s.CooldownSec.HasValue || s.DurationSec.HasValue;
        }

        public SkillInfo Find(string enName, int level)
        {
            string key = Key(enName, level);
            SkillInfo info;
            return key != null && _byKey.TryGetValue(key, out info) ? info : null;
        }

        private static string Key(string name, int level)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var sb = new StringBuilder();
            foreach (char c in name.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.Length == 0 ? null : sb.ToString() + "#" + level.ToString(CultureInfo.InvariantCulture);
        }

        private static double? ParseSec(string s)
        {
            double v;
            return double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : (double?)null;
        }

        /// <summary>讀一列 CSV（支援雙引號、逗號與跳脫的雙引號）。</summary>
        private static List<string> ReadRow(TextReader r)
        {
            string line = r.ReadLine();
            if (line == null) return null;
            var cells = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            while (true)
            {
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (quoted)
                    {
                        if (c == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                            else quoted = false;
                        }
                        else cur.Append(c);
                    }
                    else if (c == '"') quoted = true;
                    else if (c == ',') { cells.Add(cur.ToString()); cur.Clear(); }
                    else cur.Append(c);
                }
                if (!quoted) break;
                string next = r.ReadLine(); // 引號內換行
                if (next == null) break;
                cur.Append('\n');
                line = next;
            }
            cells.Add(cur.ToString());
            return cells;
        }
    }

    /// <summary>依技能資料建議「技能時間」與「延遲時間」。</summary>
    internal static class TimingAdvisor
    {
        public const int DelayBufferMs = 500;     // 延遲 = 詠唱 + 緩衝
        public const int BuffMarginMs = 3000;     // 增益技在效果結束前多久重放
        public const int MinIntervalMs = 1000;
        public const int UnknownBuffIntervalMs = 60000; // 增益技能查不到持續時間時的保守預設

        public static int SuggestIntervalMs(SkillInfo s)
        {
            int cd = ToMs(s.CooldownSec), dur = ToMs(s.DurationSec), cast = ToMs(s.CastSec);
            int interval;
            if (dur > 0)
                interval = dur > BuffMarginMs * 2 ? dur - BuffMarginMs : dur; // 有持續時間：效果快結束時再放
            else
                interval = cd;
            interval = Math.Max(interval, cd);
            interval = Math.Max(interval, cast + DelayBufferMs);
            return Math.Max(interval, MinIntervalMs);
        }

        public static int SuggestDelayMs(SkillInfo s)
        {
            return ToMs(s.CastSec) + DelayBufferMs;
        }

        private static int ToMs(double? sec)
        {
            return sec.HasValue ? (int)Math.Round(sec.Value * 1000) : 0;
        }
    }
}

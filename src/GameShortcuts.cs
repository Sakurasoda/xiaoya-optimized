using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace XiaoYaOpt
{
    internal sealed class ShortcutEntry
    {
        public string Key;          // F1..F12
        public string Command;      // 原始指令，例如 /useskill Flame Heart 5
        public string SkillName;    // 解析出的英文技能名；不是 /useskill 時為 null
        public int SkillLevel;
    }

    internal sealed class MacroFile
    {
        public string Path;
        public string Name;         // <Name> 欄位，例如 Chara1
        public DateTime LastWrite;
        public List<ShortcutEntry> Shortcuts = new List<ShortcutEntry>();

        public override string ToString()
        {
            return System.IO.Path.GetFileName(Path) + "（" + Name + "）";
        }
    }

    /// <summary>
    /// 讀取遊戲 config\MACRO??.xml 的快捷列（ShortcutCode/NONE）。
    /// 遊戲在角色登出或關閉時才會寫入；同一組設定被多個角色共用時，內容是最後登出的那個角色。
    /// </summary>
    internal static class GameShortcuts
    {
        private static readonly Regex UseSkill = new Regex(@"^/useskill\s+(?<name>.+?)\s+(?<lv>\d+)\s*$", RegexOptions.IgnoreCase);

        public static List<MacroFile> List(string gameDir)
        {
            var result = new List<MacroFile>();
            string dir = Path.Combine(gameDir, "config");
            if (!Directory.Exists(dir)) return result;
            foreach (string f in Directory.GetFiles(dir, "MACRO*.xml").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try { result.Add(Read(f)); }
                catch (Exception) { /* 損壞或寫入中的檔案略過 */ }
            }
            return result;
        }

        public static MacroFile Read(string path)
        {
            var doc = new XmlDocument();
            doc.Load(path);
            var mf = new MacroFile { Path = path, LastWrite = File.GetLastWriteTime(path) };
            var nameNode = doc.SelectSingleNode("/Config/Name");
            mf.Name = nameNode != null ? nameNode.InnerText.Trim() : "";
            var codes = doc.SelectNodes("/Config/ShortcutCode/NONE/CODE");
            if (codes != null)
            {
                foreach (XmlNode n in codes)
                {
                    var keyAttr = n.Attributes != null ? n.Attributes["KEY"] : null;
                    if (keyAttr == null) continue;
                    var e = new ShortcutEntry { Key = keyAttr.Value.Trim().ToUpperInvariant(), Command = n.InnerText.Trim() };
                    var m = UseSkill.Match(e.Command);
                    if (m.Success)
                    {
                        e.SkillName = m.Groups["name"].Value.Trim();
                        e.SkillLevel = int.Parse(m.Groups["lv"].Value, CultureInfo.InvariantCulture);
                    }
                    mf.Shortcuts.Add(e);
                }
            }
            mf.Shortcuts.Sort((a, b) =>
            {
                int c = KeyOrder(a.Key).CompareTo(KeyOrder(b.Key));
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            });
            return mf;
        }

        private static int KeyOrder(string key)
        {
            int n;
            if (key.StartsWith("F") && int.TryParse(key.Substring(1), out n)) return n;
            return 1000;
        }

        /// <summary>把快捷列轉成角色設定的技能欄位（最多 Profile.SlotCount 個）。回傳未能放入的項目數。</summary>
        public static int ApplyTo(Profile p, MacroFile mf, SkillDb db)
        {
            for (int i = 0; i < Profile.SlotCount; i++) p.Slots[i] = new SlotData { KeyName = "F" + (i + 1) };
            int slot = 0, skipped = 0;
            foreach (var e in mf.Shortcuts)
            {
                if (slot >= Profile.SlotCount) { skipped++; continue; }
                var d = p.Slots[slot++];
                d.KeyName = e.Key;
                if (e.SkillName == null)
                {
                    d.Enabled = false;
                    d.SkillLabel = e.Command;
                    continue;
                }
                d.SkillName = e.SkillName;
                d.SkillLevel = e.SkillLevel;
                // 只有能可靠算出時間的技能才自動啟用；其他的請使用者確認後手動勾選。
                d.Enabled = ApplySuggestion(d, db);
            }
            return skipped;
        }

        /// <summary>
        /// 依技能表重新計算一個欄位的名稱與時間。
        /// 回傳 true 表示時間可信、可以直接啟用；false 表示查不到資料或需要人工確認（例如增益技能沒有持續時間）。
        /// </summary>
        public static bool ApplySuggestion(SlotData d, SkillDb db)
        {
            if (string.IsNullOrEmpty(d.SkillName)) return false;
            SkillInfo info = db != null ? db.Find(d.SkillName, d.SkillLevel) : null;
            if (info == null)
            {
                d.SkillLabel = d.SkillName + " Lv" + d.SkillLevel + "（技能表查無資料）";
                return false;
            }
            d.SkillLabel = (info.ZhName.Length > 0 ? info.ZhName : d.SkillName) + " Lv" + d.SkillLevel;
            if (!info.CastSec.HasValue && !info.CooldownSec.HasValue && !info.DurationSec.HasValue)
            {
                d.SkillLabel += "（無官網時間）";
                return false;
            }
            d.DelayMs = TimingAdvisor.SuggestDelayMs(info);
            if (info.IsBuff && !info.DurationSec.HasValue)
            {
                // 增益技能不知道效果多久：用冷卻時間會變成一直重放（開關型技能還會一直開開關關），改給保守值並要求確認。
                d.IntervalMs = TimingAdvisor.UnknownBuffIntervalMs;
                d.SkillLabel += "（持續時間不明，請確認）";
                return false;
            }
            d.IntervalMs = TimingAdvisor.SuggestIntervalMs(info);
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XiaoYaOpt
{
    /// <summary>
    /// 從使用者自己的遊戲資料夾讀技能名稱（data\effect\effect.ssp）。
    /// 裝了中文化技能檔的就是中文，沒裝的就是遊戲原文。發布的技能時間表不含遊戲文字，名稱一律在執行時從這裡取得。
    /// 記錄格式：從 0x30000 開始，每筆 728 bytes；+0 技能 ID（u32）、+4 名稱（UTF-16，最多 128 bytes）、+0x286 等級。
    /// </summary>
    internal static class GameSkillNames
    {
        private const int RecordBase = 0x30000;
        private const int RecordSize = 728;
        private const int NameOffset = 4;
        private const int NameBytes = 128;

        public static string PathIn(string gameDir)
        {
            return Path.Combine(gameDir ?? "", @"data\effect\effect.ssp");
        }

        /// <summary>技能 ID → 名稱（同一 ID 取第一個非空名稱）。讀不到時回傳空的字典。</summary>
        public static Dictionary<uint, string> Load(string gameDir)
        {
            var names = new Dictionary<uint, string>();
            string path = PathIn(gameDir);
            if (string.IsNullOrEmpty(gameDir) || !File.Exists(path)) return names;
            byte[] b;
            try { b = File.ReadAllBytes(path); }
            catch (IOException) { return names; }
            catch (UnauthorizedAccessException) { return names; }
            for (int o = RecordBase; o + RecordSize <= b.Length; o += RecordSize)
            {
                uint id = BitConverter.ToUInt32(b, o);
                if (id == 0 || names.ContainsKey(id)) continue;
                int n = 0;
                while (n + 1 < NameBytes && (b[o + NameOffset + n] | b[o + NameOffset + n + 1]) != 0) n += 2;
                string name = Encoding.Unicode.GetString(b, o + NameOffset, n).Trim();
                if (name.Length > 0) names[id] = name;
            }
            return names;
        }
    }
}

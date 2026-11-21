using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace XiaoYaOpt
{
    /// <summary>副職業（デュアルジョブ）可學的一個技能。</summary>
    internal sealed class DualJobSkill
    {
        public uint SkillId;
        public string JpName;
        public int JobId;
        /// <summary>技能 Lv1..Lv10 需要的副職業等級；0 表示該等級學不到。</summary>
        public int[] RequiredLevel = new int[10];

        /// <summary>在指定副職業等級下能學到的最高技能等級（djLevel ≤ 0 表示不限）；0 表示學不到。</summary>
        public int MaxSkillLevel(int djLevel)
        {
            int max = 0;
            for (int lv = 1; lv <= RequiredLevel.Length; lv++)
            {
                int req = RequiredLevel[lv - 1];
                if (req <= 0) continue;
                if (djLevel > 0 && req > djLevel) continue;
                max = lv;
            }
            return max;
        }
    }

    /// <summary>
    /// 從遊戲資料夾的 data\xls\table.hed/dat 讀取 dualjob_skill_learn.csv（Shift-JIS）。
    /// 欄位：デュアルジョブID, スキルID, スキル名, 対象ジョブID, スキルLV1..LV10 的必要副職業等級。
    /// </summary>
    internal sealed class DualJobDb
    {
        private readonly Dictionary<int, List<DualJobSkill>> _bySystem = new Dictionary<int, List<DualJobSkill>>();

        public int RowCount { get; private set; }
        public string GameDir { get; private set; }

        public IList<DualJobSkill> SkillsOf(int systemId)
        {
            List<DualJobSkill> list;
            return _bySystem.TryGetValue(systemId, out list) ? list : new List<DualJobSkill>();
        }

        public static bool LooksLikeGameDir(string dir)
        {
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, @"data\xls\table.hed"))
                && File.Exists(Path.Combine(dir, @"data\xls\table.dat"));
        }

        public static DualJobDb Load(string gameDir)
        {
            string hed = Path.Combine(gameDir, @"data\xls\table.hed"), dat = Path.Combine(gameDir, @"data\xls\table.dat");
            var entries = EcoArchive.Open(hed, dat);
            var entry = entries.FirstOrDefault(e => string.Equals(e.Name, "dualjob_skill_learn.csv", StringComparison.OrdinalIgnoreCase));
            if (entry == null) throw new InvalidDataException("遊戲資料裡找不到 dualjob_skill_learn.csv");
            byte[] data;
            using (var fs = File.OpenRead(dat)) data = EcoArchive.ReadEntry(fs, entry);

            var db = new DualJobDb { GameDir = gameDir };
            string text = Encoding.GetEncoding(932).GetString(data);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] c = line.Split(',');
                int system, job; uint skill;
                if (c.Length < 14 || !int.TryParse(c[0], out system) || !uint.TryParse(c[1], out skill) || !int.TryParse(c[3], out job)) continue;
                var s = new DualJobSkill { SkillId = skill, JpName = c[2], JobId = job };
                for (int i = 0; i < 10; i++)
                {
                    int req;
                    s.RequiredLevel[i] = int.TryParse(c[4 + i], NumberStyles.Integer, CultureInfo.InvariantCulture, out req) ? req : 0;
                }
                List<DualJobSkill> list;
                if (!db._bySystem.TryGetValue(system, out list)) db._bySystem[system] = list = new List<DualJobSkill>();
                list.Add(s);
                db.RowCount++;
            }
            if (db.RowCount == 0) throw new InvalidDataException("dualjob_skill_learn.csv 沒有資料");
            return db;
        }
    }
}

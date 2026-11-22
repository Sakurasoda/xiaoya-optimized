using System;
using System.Collections.Generic;

namespace XiaoYaOpt
{
    internal sealed class JobInfo
    {
        /// <summary>主職業：職業線的最終職業官網代號（例如 maestro）；副職業：體系 ID。</summary>
        public string Slug;
        public string Display;
        public override string ToString() { return Display; }
    }

    /// <summary>
    /// Nekogame 官網的職業清單（依官網選單順序）。
    /// 主職業以「職業線」為單位，用最終職業的縮寫表示；技能清單包含整條線的技能。
    /// </summary>
    internal static class Jobs
    {
        public const string AllSlug = "";

        /// <summary>
        /// 職業線：縮寫、顯示名稱、線上的職業（基本→進階→技術→最終職業）。
        /// 縮寫取自官網選單，鷹眼官網沒有標示，用玩家慣用的 HAW。
        /// </summary>
        private static readonly object[][] LineTable =
        {
            new object[] { "GL",  "劍鬥士（劍士系）",     new[] { "swordsman", "blade-master", "bounty-hunter", "gladiator" } },
            new object[] { "GU",  "守護者（騎士系）",     new[] { "fencer", "knight", "dark-stalker", "guardian" } },
            new object[] { "ER",  "抹消者（盜賊系）",     new[] { "scout", "assassin", "commando", "eraser" } },
            new object[] { "HAW", "鷹眼（弓箭手系）",     new[] { "archer", "striker", "gunner", "hawkeye" } },
            new object[] { "FO",  "力量大師（魔法師系）", new[] { "wizard", "sorcerer", "sage", "force-master" } },
            new object[] { "AS",  "星靈使（元素師系）",   new[] { "shaman", "elementalist", "enchanter", "astralist" } },
            new object[] { "CA",  "樞機主教（祭司系）",   new[] { "vates", "druid", "bard", "cardinal" } },
            new object[] { "SO",  "靈魂收割者（術士系）", new[] { "warlock", "cabalist", "necromancer", "soultaker" } },
            new object[] { "MA",  "大師（礦工系）",       new[] { "miner", "blacksmith", "engineer", "maestro" } },
            new object[] { "HA",  "收穫者（農夫系）",     new[] { "farmer", "alchemist", "marionettist", "harvester" } },
            new object[] { "ST",  "漫遊者（遊俠系）",     new[] { "ranger", "explorer", "treasure-hunter", "strider" } },
            new object[] { "RO",  "皇家商人（商人系）",   new[] { "merchant", "trader", "gambler", "royal-dealer" } },
            new object[] { "JO",  "小丑",                 new[] { "joker" } },
            new object[] { "BR",  "馴獸師",               new[] { "breeder" } },
            new object[] { "GA",  "園藝師",               new[] { "gardener" } },
        };

        /// <summary>
        /// 副職業體系（遊戲 dualjob_info.csv 的 デュアルジョブID）與所含職業。
        /// 副職業是整個體系，可學的技能與等級上限由 dualjob_skill_learn.csv 決定。
        /// </summary>
        private static readonly object[][] DualJobTable =
        {
            new object[] { 1,  "劍士系 Swordsman",  new[] { "swordsman", "blade-master", "bounty-hunter", "gladiator" } },
            new object[] { 2,  "騎士系 Fencer",     new[] { "fencer", "knight", "dark-stalker", "guardian" } },
            new object[] { 3,  "盜賊系 Scout",      new[] { "scout", "assassin", "commando", "eraser" } },
            new object[] { 4,  "弓箭手系 Archer",   new[] { "archer", "striker", "gunner", "hawkeye" } },
            new object[] { 5,  "魔法師系 Wizard",   new[] { "wizard", "sorcerer", "sage", "force-master" } },
            new object[] { 6,  "元素師系 Shaman",   new[] { "shaman", "elementalist", "enchanter", "astralist" } },
            new object[] { 7,  "祭司系 Vates",      new[] { "vates", "druid", "bard", "cardinal" } },
            new object[] { 8,  "術士系 Warlock",    new[] { "warlock", "cabalist", "necromancer", "soultaker" } },
            new object[] { 9,  "礦工系 Miner",      new[] { "miner", "blacksmith", "engineer", "maestro" } },
            new object[] { 10, "農夫系 Farmer",     new[] { "farmer", "alchemist", "marionettist", "harvester" } },
            new object[] { 11, "遊俠系 Ranger",     new[] { "ranger", "explorer", "treasure-hunter", "strider" } },
            new object[] { 12, "商人系 Merchant",   new[] { "merchant", "trader", "gambler", "royal-dealer" } },
        };

        /// <summary>副職業選單用：「（無）」＋12 個副職業體系。Slug 存的是體系 ID（字串）。</summary>
        public static List<JobInfo> SubJobChoices()
        {
            var list = new List<JobInfo> { new JobInfo { Slug = "", Display = "（無）" } };
            foreach (var row in DualJobTable)
                list.Add(new JobInfo { Slug = ((int)row[0]).ToString(), Display = (string)row[1] });
            return list;
        }

        /// <summary>找出某個職業所屬的副職業體系 ID；不屬於任何體系時回傳空字串。</summary>
        public static string SystemOfJob(string slug)
        {
            foreach (var row in DualJobTable)
                if (Array.IndexOf((string[])row[2], slug) >= 0) return ((int)row[0]).ToString();
            return "";
        }

        /// <summary>主職業選單：「（全部職業）」＋各職業線（例如「ER  抹消者（盜賊系）」）。</summary>
        public static List<JobInfo> All()
        {
            var list = new List<JobInfo> { new JobInfo { Slug = AllSlug, Display = "（全部職業）" } };
            foreach (var row in LineTable)
            {
                var m = (string[])row[2];
                list.Add(new JobInfo { Slug = m[m.Length - 1], Display = (string)row[0] + "  " + (string)row[1] });
            }
            return list;
        }

        /// <summary>職業線（Key = 最終職業代號）與線上的所有職業（基本職業在前）。</summary>
        public static IEnumerable<KeyValuePair<string, string[]>> Lines()
        {
            foreach (var row in LineTable)
            {
                var m = (string[])row[2];
                yield return new KeyValuePair<string, string[]>(m[m.Length - 1], m);
            }
        }

        /// <summary>任一職業代號 → 所屬職業線（最終職業代號）。舊版設定存的是單一職業，用這個轉換。</summary>
        public static string LineOf(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return AllSlug;
            foreach (var row in LineTable)
            {
                var m = (string[])row[2];
                if (Array.IndexOf(m, slug) >= 0) return m[m.Length - 1];
            }
            return slug;
        }
    }
}

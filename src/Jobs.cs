using System;
using System.Collections.Generic;

namespace XiaoYaOpt
{
    internal sealed class JobInfo
    {
        /// <summary>官網網址代號，與技能表「職業」欄相同，例如 blacksmith。</summary>
        public string Slug;
        public string Display;
        public override string ToString() { return Display; }
    }

    /// <summary>
    /// Nekogame 官網的職業清單（依官網選單順序）。
    /// 中文名稱是常見譯名，不一定與台版用語完全一致，所以同時顯示英文。
    /// </summary>
    internal static class Jobs
    {
        public const string AllSlug = "";

        private static readonly string[,] Table =
        {
            { "",                "（全部職業）" },
            { "swordsman",       "劍士 Swordsman" },
            { "blade-master",    "劍聖 Blade Master" },
            { "bounty-hunter",   "賞金獵人 Bounty Hunter" },
            { "gladiator",       "劍鬥士 Gladiator" },
            { "fencer",          "騎士 Fencer" },
            { "knight",          "聖騎士 Knight" },
            { "dark-stalker",    "暗黑騎士 Dark Stalker" },
            { "guardian",        "守護者 Guardian" },
            { "scout",           "盜賊 Scout" },
            { "assassin",        "刺客 Assassin" },
            { "commando",        "突擊兵 Commando" },
            { "eraser",          "抹消者 Eraser" },
            { "archer",          "弓箭手 Archer" },
            { "striker",         "狙擊手 Striker" },
            { "gunner",          "槍手 Gunner" },
            { "hawkeye",         "鷹眼 Hawkeye" },
            { "wizard",          "魔法師 Wizard" },
            { "sorcerer",        "巫師 Sorcerer" },
            { "sage",            "賢者 Sage" },
            { "force-master",    "力量大師 Force Master" },
            { "shaman",          "元素師 Shaman" },
            { "elementalist",    "精靈使 Elementalist" },
            { "enchanter",       "附魔師 Enchanter" },
            { "astralist",       "星靈使 Astralist" },
            { "vates",           "祭司 Vates" },
            { "druid",           "德魯伊 Druid" },
            { "bard",            "吟遊詩人 Bard" },
            { "cardinal",        "樞機主教 Cardinal" },
            { "warlock",         "術士 Warlock" },
            { "cabalist",        "秘術士 Cabalist" },
            { "necromancer",     "死靈法師 Necromancer" },
            { "soultaker",       "靈魂收割者 Soul Taker" },
            { "miner",           "礦工 Miner" },
            { "blacksmith",      "鐵匠 Blacksmith" },
            { "engineer",        "工程師 Engineer" },
            { "maestro",         "大師 Maestro" },
            { "farmer",          "農夫 Farmer" },
            { "alchemist",       "鍊金術士 Alchemist" },
            { "marionettist",    "傀儡師 Marionettist" },
            { "harvester",       "收穫者 Harvester" },
            { "ranger",          "遊俠 Ranger" },
            { "explorer",        "探險家 Explorer" },
            { "treasure-hunter", "寶藏獵人 Treasure Hunter" },
            { "strider",         "漫遊者 Strider" },
            { "merchant",        "商人 Merchant" },
            { "trader",          "貿易商 Trader" },
            { "gambler",         "賭徒 Gambler" },
            { "royal-dealer",    "皇家商人 Royal Dealer" },
            { "breeder",         "馴獸師 Breeder" },
            { "gardener",        "園藝師 Gardener" },
            { "joker",           "小丑 Joker" },
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

        public static List<JobInfo> All()
        {
            var list = new List<JobInfo>();
            for (int i = 0; i < Table.GetLength(0); i++) list.Add(new JobInfo { Slug = Table[i, 0], Display = Table[i, 1] });
            return list;
        }
    }
}

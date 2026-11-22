using System;
using System.Collections.Generic;

namespace XiaoYaOpt
{
    /// <summary>
    /// 「憑依時可否使用」的判斷。遊戲檔案與 Wiki 都沒有完整資料（很可能是伺服器端判斷），
    /// 所以先依官網技能類型推估，再加上 Wiki 明確提到的技能，使用者可以逐一手動修正。
    /// </summary>
    internal sealed class Possession
    {
        /// <summary>lycolia Wiki（?Hyoui、?Skill/Blacksmith）明確寫到憑依中可使用的技能（日文名）。</summary>
        private static readonly HashSet<string> WikiUsable = new HashSet<string>
        {
            "フレイムハート", "クリティカルアシスト", "アタックアシスト", "ディフェンスアシスト", "プロヴァイド",
            "エナジーバリア", "マジックバリア", "サルベイション", "ヒーリング", "ホーリーライト"
        };

        /// <summary>類型含這些字就推估為可用（治療、輔助類）。</summary>
        private static readonly string[] SupportWords = { "heal", "support", "recovery", "cleanse", "knowledge" };

        /// <summary>使用者手動修正：技能 ID → 可用／不可用。</summary>
        private readonly Dictionary<uint, bool> _overrides = new Dictionary<uint, bool>();

        public IDictionary<uint, bool> Overrides { get { return _overrides; } }

        public static bool Guess(SkillEntry e)
        {
            if (e == null) return false;
            if (!string.IsNullOrEmpty(e.JpName) && WikiUsable.Contains(e.JpName)) return true;
            string t = (e.Type ?? "").ToLowerInvariant();
            if (t.Length == 0 || t.StartsWith("passive")) return false;
            foreach (string w in SupportWords)
                if (t.Contains(w)) return true;                // 治療、輔助（含「area heal/attack」這類）
            // 增益類算可用；只有減益（debuff）的不算：去掉 "debuff" 後還剩 "buff" 才算增益
            return t.Replace("debuff", "").Contains("buff");
        }

        public bool IsUsable(SkillEntry e)
        {
            bool v;
            if (e != null && e.Id != 0 && _overrides.TryGetValue(e.Id, out v)) return v;
            return Guess(e);
        }

        /// <summary>0 = 自動判斷，1 = 手動標記可用，2 = 手動標記不可用。</summary>
        public int OverrideState(SkillEntry e)
        {
            bool v;
            if (e == null || e.Id == 0 || !_overrides.TryGetValue(e.Id, out v)) return 0;
            return v ? 1 : 2;
        }

        public void SetOverride(SkillEntry e, bool? usable)
        {
            if (e == null || e.Id == 0) return;
            if (usable.HasValue) _overrides[e.Id] = usable.Value; else _overrides.Remove(e.Id);
        }
    }
}

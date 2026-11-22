using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XiaoYaOpt
{
    internal sealed class SlotData
    {
        public bool Enabled;
        public string KeyName = "F1";
        public int IntervalMs = 1000;
        public bool Mouse;
        public int DelayMs = 200;
        /// <summary>遊戲快捷列裡的英文技能名（/useskill 後面那段），空字串表示手動設定。</summary>
        public string SkillName = "";
        public int SkillLevel;
        /// <summary>顯示用：中文名稱 + 等級。</summary>
        public string SkillLabel = "";

        public SlotData Clone() { return (SlotData)MemberwiseClone(); }
    }

    internal sealed class Profile
    {
        public const int DefaultSlots = 6;
        public const int MinSlots = 1;
        public const int MaxSlots = 15;
        public string Name;
        /// <summary>職業線（最終職業的官網代號，例如 maestro = 礦工系整條線）；空字串表示全部職業。</summary>
        public string Job = "";
        /// <summary>副職業體系 ID（例如 "9" = 礦工系）；空字串表示沒有副職業。</summary>
        public string SubJob = "";
        /// <summary>副職業等級；0 表示不限（列出體系內全部可學的技能與等級）。</summary>
        public int SubJobLevel;
        /// <summary>技能清單只顯示憑依時可用的技能。</summary>
        public bool PossessOnly;
        /// <summary>技能格（1～15 格，可自行增減）。</summary>
        public List<SlotData> Slots = new List<SlotData>();

        public Profile(string name) : this(name, DefaultSlots) { }

        public Profile(string name, int slotCount)
        {
            Name = name;
            for (int i = 0; i < slotCount; i++) Slots.Add(new SlotData { KeyName = "F" + (i + 1) });
        }

        public Profile Clone(string newName)
        {
            var p = new Profile(newName, 0) { Job = Job, SubJob = SubJob, SubJobLevel = SubJobLevel, PossessOnly = PossessOnly };
            foreach (var d in Slots) p.Slots.Add(d.Clone());
            return p;
        }

        /// <summary>新增一格；按鍵預設為還沒用到的第一個 F 鍵。超過上限時回傳 null。</summary>
        public SlotData AddSlot()
        {
            if (Slots.Count >= MaxSlots) return null;
            var used = new HashSet<string>(Slots.Select(s => s.KeyName), StringComparer.OrdinalIgnoreCase);
            // 依序找還沒用過的鍵：F1～F12，用完再用數字鍵 1～9、0
            var order = Enumerable.Range(1, 12).Select(f => "F" + f)
                .Concat(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" });
            string key = order.FirstOrDefault(k => !used.Contains(k)) ?? "F1";
            var d = new SlotData { KeyName = key };
            Slots.Add(d);
            return d;
        }
    }

    /// <summary>角色設定存在 exe 旁的「角色設定」資料夾，每個設定一個 UTF-8 檔案。</summary>
    internal sealed class ProfileStore
    {
        public const string DefaultName = "預設";
        private readonly string _dir;
        private readonly List<Profile> _profiles = new List<Profile>();

        public ProfileStore(string dir)
        {
            _dir = dir;
        }

        public IList<Profile> All { get { return _profiles; } }

        public Profile Find(string name)
        {
            return _profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public void Load()
        {
            _profiles.Clear();
            if (Directory.Exists(_dir))
            {
                foreach (string f in Directory.GetFiles(_dir, "*.cfg").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var s = new Settings(f);
                    var p = new Profile(s.Get("name", Path.GetFileNameWithoutExtension(f)));
                    p.Job = Jobs.LineOf(s.Get("job", "")); // 舊版存的是單一職業，轉成所屬職業線
                    p.SubJob = s.Get("subJob", "");
                    int sysId;
                    if (p.SubJob.Length > 0 && !int.TryParse(p.SubJob, out sysId))
                        p.SubJob = Jobs.SystemOfJob(p.SubJob); // 舊版存的是單一職業代號，轉成所屬體系
                    p.SubJobLevel = s.GetInt("subJobLevel", 0);
                    p.PossessOnly = s.GetBool("possessOnly", false);
                    // 舊版沒有 slotCount，固定 6 格
                    int count = Math.Max(Profile.MinSlots, Math.Min(Profile.MaxSlots, s.GetInt("slotCount", Profile.DefaultSlots)));
                    p.Slots.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        string n = "slot" + (i + 1) + ".";
                        var d = new SlotData();
                        p.Slots.Add(d);
                        d.Enabled = s.GetBool(n + "enabled", false);
                        d.KeyName = s.Get(n + "key", "F" + (i + 1));
                        d.IntervalMs = s.GetInt(n + "interval", 1000);
                        d.Mouse = s.GetBool(n + "mouse", false);
                        d.DelayMs = s.GetInt(n + "delay", 200);
                        d.SkillName = s.Get(n + "skill", "");
                        d.SkillLevel = s.GetInt(n + "skillLevel", 0);
                        d.SkillLabel = s.Get(n + "skillLabel", "");
                    }
                    _profiles.Add(p);
                }
            }
            if (Find(DefaultName) == null) _profiles.Insert(0, new Profile(DefaultName));
        }

        public void Save(Profile p)
        {
            Directory.CreateDirectory(_dir);
            var s = new Settings(FileFor(p.Name), false); // 整份重寫，刪掉的格子才不會殘留
            s.Set("name", p.Name);
            s.Set("job", p.Job ?? "");
            s.Set("subJob", p.SubJob ?? "");
            s.Set("subJobLevel", p.SubJobLevel);
            s.Set("possessOnly", p.PossessOnly);
            s.Set("slotCount", p.Slots.Count);
            for (int i = 0; i < p.Slots.Count; i++)
            {
                string n = "slot" + (i + 1) + ".";
                var d = p.Slots[i];
                s.Set(n + "enabled", d.Enabled);
                s.Set(n + "key", d.KeyName);
                s.Set(n + "interval", d.IntervalMs);
                s.Set(n + "mouse", d.Mouse);
                s.Set(n + "delay", d.DelayMs);
                s.Set(n + "skill", d.SkillName);
                s.Set(n + "skillLevel", d.SkillLevel);
                s.Set(n + "skillLabel", d.SkillLabel);
            }
            s.Save();
        }

        public void SaveAll()
        {
            foreach (var p in _profiles) Save(p);
        }

        public Profile Add(Profile p)
        {
            _profiles.Add(p);
            Save(p);
            return p;
        }

        public void Delete(Profile p)
        {
            _profiles.Remove(p);
            string f = FileFor(p.Name);
            if (File.Exists(f)) File.Delete(f);
        }

        public void Rename(Profile p, string newName)
        {
            string old = FileFor(p.Name);
            p.Name = newName;
            Save(p);
            if (!string.Equals(old, FileFor(newName), StringComparison.OrdinalIgnoreCase) && File.Exists(old)) File.Delete(old);
        }

        /// <summary>檢查名稱是否可用（非空、不重複、可當檔名）。回傳錯誤訊息，可用時回傳 null。</summary>
        public string Validate(string name, Profile except)
        {
            if (string.IsNullOrWhiteSpace(name)) return "名稱不能是空白";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "名稱不能包含 \\ / : * ? \" < > | 等字元";
            var other = Find(name.Trim());
            if (other != null && other != except) return "已經有同名的角色設定";
            return null;
        }

        private string FileFor(string name)
        {
            return Path.Combine(_dir, name + ".cfg");
        }
    }
}

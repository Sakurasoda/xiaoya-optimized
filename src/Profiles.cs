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
        public const int SlotCount = 6;
        public string Name;
        public SlotData[] Slots = new SlotData[SlotCount];

        public Profile(string name)
        {
            Name = name;
            for (int i = 0; i < SlotCount; i++) Slots[i] = new SlotData { KeyName = "F" + (i + 1) };
        }

        public Profile Clone(string newName)
        {
            var p = new Profile(newName);
            for (int i = 0; i < SlotCount; i++) p.Slots[i] = Slots[i].Clone();
            return p;
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
                    for (int i = 0; i < Profile.SlotCount; i++)
                    {
                        string n = "slot" + (i + 1) + ".";
                        var d = p.Slots[i];
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
            var s = new Settings(FileFor(p.Name));
            s.Set("name", p.Name);
            for (int i = 0; i < Profile.SlotCount; i++)
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

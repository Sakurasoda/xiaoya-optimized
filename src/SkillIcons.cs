using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace XiaoYaOpt
{
    /// <summary>
    /// 從遊戲資料夾的 data\sprite\skillicon\skillicon.hed/dat 讀取技能圖示（檔名 SI_&lt;技能ID&gt;.TGA），用到時才解碼並快取。
    /// </summary>
    internal sealed class SkillIcons : IDisposable
    {
        private readonly string _datPath;
        private readonly Dictionary<string, EcoArchive.Entry> _byName = new Dictionary<string, EcoArchive.Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, Image> _cache = new Dictionary<uint, Image>();
        private FileStream _dat;

        public int Count { get { return _byName.Count; } }

        private SkillIcons(string datPath)
        {
            _datPath = datPath;
        }

        public static SkillIcons Load(string gameDir)
        {
            string hed = Path.Combine(gameDir, @"data\sprite\skillicon\skillicon.hed");
            string dat = Path.ChangeExtension(hed, ".dat");
            if (!File.Exists(hed) || !File.Exists(dat)) return null;
            var icons = new SkillIcons(dat);
            foreach (var e in EcoArchive.Open(hed, dat))
                if (!string.IsNullOrEmpty(e.Name) && e.Index > 0) icons._byName[e.Name] = e;
            return icons;
        }

        /// <summary>取得技能圖示；沒有對應圖示時回傳 null。</summary>
        public Image Get(uint skillId)
        {
            Image img;
            if (_cache.TryGetValue(skillId, out img)) return img;
            img = null;
            EcoArchive.Entry e;
            if (skillId != 0 && _byName.TryGetValue(string.Format("SI_{0:D4}.TGA", skillId), out e))
            {
                try
                {
                    if (_dat == null) _dat = File.OpenRead(_datPath);
                    img = EcoImage.Decode(EcoArchive.ReadEntry(_dat, e));
                }
                catch (Exception) { img = null; }
            }
            _cache[skillId] = img;
            return img;
        }

        public void Dispose()
        {
            if (_dat != null) _dat.Dispose();
            foreach (var img in _cache.Values) if (img != null) img.Dispose();
            _cache.Clear();
        }
    }
}

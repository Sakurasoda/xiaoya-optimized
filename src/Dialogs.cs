using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XiaoYaOpt
{
    /// <summary>輸入名稱的小對話框。</summary>
    internal sealed class PromptDialog : Form
    {
        private readonly TextBox _txt = new TextBox();
        private readonly Func<string, string> _validate;

        public string Value { get { return _txt.Text.Trim(); } }

        public PromptDialog(string title, string label, string initial, Func<string, string> validate)
        {
            _validate = validate;
            Text = title;
            Font = new Font("Microsoft JhengHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 110);

            Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(12, 14) });
            _txt.SetBounds(12, 38, 316, 23);
            _txt.Text = initial ?? "";
            Controls.Add(_txt);
            var ok = new Button { Text = "確定", DialogResult = DialogResult.None };
            ok.SetBounds(172, 72, 75, 28);
            ok.Click += OnOk;
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(253, 72, 75, 28);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void OnOk(object sender, EventArgs e)
        {
            string err = _validate != null ? _validate(Value) : null;
            if (err != null)
            {
                MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>從遊戲 config\MACRO??.xml 匯入快捷列。</summary>
    internal sealed class ImportDialog : Form
    {
        private readonly TextBox _txtGameDir = new TextBox();
        private readonly ListView _lstFiles = new ListView();
        private readonly ListView _lstPreview = new ListView();
        private readonly RadioButton _rbNew = new RadioButton();
        private readonly RadioButton _rbOverwrite = new RadioButton();
        private readonly TextBox _txtNewName = new TextBox();
        private readonly Label _lblHint = new Label();
        private readonly SkillDb _db;
        private readonly ProfileStore _store;
        private readonly Profile _current;
        private List<MacroFile> _files = new List<MacroFile>();

        public string GameDir { get { return _txtGameDir.Text.Trim(); } }
        public MacroFile Selected { get; private set; }
        public bool CreateNew { get { return _rbNew.Checked; } }
        public string NewName { get { return _txtNewName.Text.Trim(); } }

        public ImportDialog(string gameDir, SkillDb db, ProfileStore store, Profile current)
        {
            _db = db;
            _store = store;
            _current = current;
            Text = "從遊戲匯入快捷列";
            Font = new Font("Microsoft JhengHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 520);

            Controls.Add(new Label { Text = "遊戲資料夾", AutoSize = true, Location = new Point(12, 15) });
            _txtGameDir.SetBounds(90, 12, 400, 23);
            _txtGameDir.Text = gameDir ?? "";
            Controls.Add(_txtGameDir);
            var browse = new Button { Text = "選擇…" };
            browse.SetBounds(496, 10, 52, 27);
            browse.Click += OnBrowse;
            Controls.Add(browse);
            var reload = new Button { Text = "讀取" };
            reload.SetBounds(552, 10, 56, 27);
            reload.Click += delegate { LoadFiles(); };
            Controls.Add(reload);

            Controls.Add(new Label
            {
                Text = "遊戲在角色登出或關閉時才會寫入快捷列；多個角色共用同一組設定時，內容是最後登出的那個角色。",
                Location = new Point(12, 44), Size = new Size(596, 20)
            });

            _lstFiles.SetBounds(12, 68, 596, 120);
            _lstFiles.View = View.Details;
            _lstFiles.FullRowSelect = true;
            _lstFiles.HideSelection = false;
            _lstFiles.MultiSelect = false;
            _lstFiles.Columns.Add("檔案", 110);
            _lstFiles.Columns.Add("設定組", 80);
            _lstFiles.Columns.Add("最後寫入", 130);
            _lstFiles.Columns.Add("快捷列技能數", 90);
            _lstFiles.SelectedIndexChanged += delegate { ShowPreview(); };
            Controls.Add(_lstFiles);

            Controls.Add(new Label { Text = "內容預覽（依技能表算出的建議時間，匯入後仍可修改）", AutoSize = true, Location = new Point(12, 196) });
            _lstPreview.SetBounds(12, 218, 596, 180);
            _lstPreview.View = View.Details;
            _lstPreview.FullRowSelect = true;
            _lstPreview.Columns.Add("按鍵", 50);
            _lstPreview.Columns.Add("技能", 200);
            _lstPreview.Columns.Add("詠唱/冷卻/持續(秒)", 130);
            _lstPreview.Columns.Add("技能時間(毫秒)", 100);
            _lstPreview.Columns.Add("延遲時間(毫秒)", 100);
            Controls.Add(_lstPreview);

            _rbNew.Text = "匯入為新的角色設定，名稱：";
            _rbNew.SetBounds(12, 408, 200, 22);
            _rbNew.Checked = true;
            _txtNewName.SetBounds(214, 407, 180, 23);
            _rbOverwrite.Text = "覆蓋目前選取的角色設定「" + current.Name + "」";
            _rbOverwrite.SetBounds(12, 434, 400, 22);
            Controls.AddRange(new Control[] { _rbNew, _txtNewName, _rbOverwrite });

            _lblHint.SetBounds(12, 462, 400, 40);
            _lblHint.ForeColor = Color.DimGray;
            Controls.Add(_lblHint);

            var ok = new Button { Text = "匯入" };
            ok.SetBounds(452, 480, 75, 30);
            ok.Click += OnOk;
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(533, 480, 75, 30);
            Controls.Add(ok);
            Controls.Add(cancel);
            CancelButton = cancel;

            if (_db == null) _lblHint.Text = "尚未載入技能表：只會匯入按鍵與技能名稱，時間需要手動設定。";
            Shown += delegate { if (GameDir.Length > 0) LoadFiles(); };
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog { Description = "選擇遊戲資料夾（裡面有 eco.exe 與 config 資料夾）" })
            {
                if (Directory.Exists(GameDir)) dlg.SelectedPath = GameDir;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _txtGameDir.Text = dlg.SelectedPath;
                    LoadFiles();
                }
            }
        }

        private void LoadFiles()
        {
            _lstFiles.Items.Clear();
            _lstPreview.Items.Clear();
            if (!Directory.Exists(Path.Combine(GameDir, "config")))
            {
                _lblHint.Text = "這個資料夾裡找不到 config 子資料夾，請確認是遊戲安裝資料夾。";
                return;
            }
            _files = GameShortcuts.List(GameDir);
            foreach (var f in _files)
            {
                var item = new ListViewItem(new[]
                {
                    Path.GetFileName(f.Path), f.Name, f.LastWrite.ToString("yyyy/MM/dd HH:mm:ss"),
                    f.Shortcuts.Count(s => s.SkillName != null).ToString()
                }) { Tag = f };
                _lstFiles.Items.Add(item);
            }
            if (_files.Count == 0) { _lblHint.Text = "沒有找到 MACRO??.xml。"; return; }
            // 預設選最近寫入的那個（通常是剛登出的角色）
            var latest = _files.OrderByDescending(f => f.LastWrite).First();
            foreach (ListViewItem it in _lstFiles.Items)
                if (it.Tag == latest) { it.Selected = true; it.EnsureVisible(); }
        }

        private void ShowPreview()
        {
            _lstPreview.Items.Clear();
            if (_lstFiles.SelectedItems.Count == 0) return;
            var mf = (MacroFile)_lstFiles.SelectedItems[0].Tag;
            var temp = new Profile("preview");
            int skipped = GameShortcuts.ApplyTo(temp, mf, _db);
            for (int i = 0; i < Profile.SlotCount; i++)
            {
                var d = temp.Slots[i];
                if (string.IsNullOrEmpty(d.SkillName) && string.IsNullOrEmpty(d.SkillLabel)) continue;
                SkillInfo info = _db != null && !string.IsNullOrEmpty(d.SkillName) ? _db.Find(d.SkillName, d.SkillLevel) : null;
                string times = info == null ? "" : string.Format("{0} / {1} / {2}", Fmt(info.CastSec), Fmt(info.CooldownSec), Fmt(info.DurationSec));
                _lstPreview.Items.Add(new ListViewItem(new[]
                {
                    d.KeyName, d.SkillLabel, times,
                    d.Enabled ? d.IntervalMs.ToString() : "（不啟用）", d.Enabled ? d.DelayMs.ToString() : ""
                }));
            }
            _lblHint.Text = skipped > 0
                ? string.Format("快捷列有 {0} 個項目超過 {1} 格，只會匯入前 {1} 個。", skipped, Profile.SlotCount)
                : (_db == null ? _lblHint.Text : "");
            if (_txtNewName.Text.Length == 0) _txtNewName.Text = SuggestName(mf);
        }

        private string SuggestName(MacroFile mf)
        {
            string baseName = string.IsNullOrEmpty(mf.Name) ? Path.GetFileNameWithoutExtension(mf.Path) : mf.Name;
            string name = baseName;
            for (int n = 2; _store.Find(name) != null; n++) name = baseName + "-" + n;
            return name;
        }

        private static string Fmt(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.##") : "-";
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (_lstFiles.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "請先選擇要匯入的設定檔。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (CreateNew)
            {
                string err = _store.Validate(NewName, null);
                if (err != null) { MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            Selected = (MacroFile)_lstFiles.SelectedItems[0].Tag;
            DialogResult = DialogResult.OK;
        }
    }
}

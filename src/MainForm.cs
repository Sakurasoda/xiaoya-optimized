using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XiaoYaOpt
{
    internal sealed class MainForm : Form
    {
        private const int SlotCount = Profile.SlotCount;
        private const int HotkeyId = 1;
        private const int VK_F10 = 0x79;

        private sealed class KeyItem
        {
            public string Name;
            public int Vk;
            public override string ToString() { return Name; }
        }

        private sealed class WindowItem
        {
            public IntPtr Hwnd;
            public string Title;
            public uint Pid;
        }

        // 視窗清單
        private readonly TextBox _txtKeyword = new TextBox();
        private readonly Button _btnRefresh = new Button();
        private readonly DataGridView _grid = new DataGridView();
        private readonly DataGridViewCheckBoxColumn _colUse = new DataGridViewCheckBoxColumn();
        private readonly DataGridViewTextBoxColumn _colTitle = new DataGridViewTextBoxColumn();
        private readonly DataGridViewTextBoxColumn _colPid = new DataGridViewTextBoxColumn();
        private readonly DataGridViewComboBoxColumn _colProfile = new DataGridViewComboBoxColumn();

        // 角色設定編輯器
        private readonly GroupBox _grpProfile = new GroupBox();
        private readonly ComboBox _cboProfile = new ComboBox();
        private readonly Button _btnNew = new Button();
        private readonly Button _btnCopy = new Button();
        private readonly Button _btnRename = new Button();
        private readonly Button _btnDelete = new Button();
        private readonly ComboBox _cboJob = new ComboBox();
        private readonly ComboBox _cboSubJob = new ComboBox();
        private readonly NumericUpDown _numSubLv = new NumericUpDown();
        private DualJobDb _dj;
        private SkillIcons _icons;
        private readonly ToolTip _tip = new ToolTip();

        /// <summary>技能下拉的項目：主職業技能直接顯示名稱，副職業技能前面加「［副］」。</summary>
        private sealed class SkillChoice
        {
            public SkillEntry Entry;
            public bool Sub;
            /// <summary>可選的最高等級；0 表示不限（主職業技能）。副職業技能依副職業表限制。</summary>
            public int MaxLevel;
            public override string ToString() { return (Sub ? "［副］" : "") + Entry.Display; }
        }
        private readonly Button _btnImport = new Button();
        private readonly Button _btnDb = new Button();
        private readonly Label _lblDb = new Label();
        private readonly Label _lblHint = new Label();
        private readonly CheckBox[] _chkEnabled = new CheckBox[SlotCount];
        private readonly ComboBox[] _cboKey = new ComboBox[SlotCount];
        private readonly ComboBox[] _cboSkill = new ComboBox[SlotCount];
        private readonly ComboBox[] _cboLv = new ComboBox[SlotCount];
        private const string ManualItem = "（手動設定）";
        private readonly NumericUpDown[] _numInterval = new NumericUpDown[SlotCount];
        private readonly CheckBox[] _chkMouse = new CheckBox[SlotCount];
        private readonly NumericUpDown[] _numDelay = new NumericUpDown[SlotCount];

        // 選項
        private readonly GroupBox _grpOptions = new GroupBox();
        private readonly RadioButton _rbBackground = new RadioButton();
        private readonly RadioButton _rbForeground = new RadioButton();
        private readonly ComboBox _cboButton = new ComboBox();
        private readonly RadioButton _rbCenter = new RadioButton();
        private readonly RadioButton _rbCustom = new RadioButton();
        private readonly CheckBox _chkCtrlT = new CheckBox();
        private readonly NumericUpDown _numX = new NumericUpDown();
        private readonly NumericUpDown _numY = new NumericUpDown();
        private readonly Button _btnCapture = new Button();
        private readonly NumericUpDown _numStagger = new NumericUpDown();

        private readonly Button _btnStart = new Button();
        private readonly Button _btnStop = new Button();
        private readonly Button _btnSave = new Button();
        private readonly Label _lblStatus = new Label();
        private readonly Timer _uiTimer = new Timer();
        private readonly Timer _captureTimer = new Timer();

        private readonly string _baseDir;
        private readonly string _settingsPath;
        private readonly ProfileStore _store;
        private readonly Dictionary<string, int> _vkByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _assignByTitle = new Dictionary<string, string>();
        private Profile _current;
        private SkillDb _db;
        private string _skillDbPath = "";
        private string _gameDir = "";
        private bool _loadingEditor;
        private Scheduler _scheduler;
        private int _activeWindows;

        public MainForm()
        {
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _settingsPath = Path.Combine(_baseDir, "小雅優化版.cfg");
            _store = new ProfileStore(Path.Combine(_baseDir, "角色設定"));

            Text = "小雅 優化版";
            Font = new Font("Microsoft JhengHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(640, 790);
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi();
            LoadSettings();
            RefreshWindows();
            SetRunning(false);

            _uiTimer.Interval = 1000;
            _uiTimer.Tick += delegate { UpdateStatus(); };
            _captureTimer.Interval = 3000;
            _captureTimer.Tick += OnCaptureTick;
        }

        // =====================================================================
        // 介面配置
        // =====================================================================

        private void BuildUi()
        {
            // ---- 視窗清單 ----
            AddLabel(this, "視窗標題關鍵字", 12, 15);
            _txtKeyword.SetBounds(110, 12, 120, 23);
            Controls.Add(_txtKeyword);
            _btnRefresh.Text = "重新整理視窗";
            _btnRefresh.SetBounds(240, 10, 110, 27);
            _btnRefresh.Click += delegate { RefreshWindows(); };
            Controls.Add(_btnRefresh);
            AddLabel(this, "勾選要自動按鍵的視窗，並替每個視窗選擇角色設定", 360, 15);

            _grid.SetBounds(12, 44, 616, 130);
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _colUse.HeaderText = "啟用";
            _colUse.Width = 45;
            _colTitle.HeaderText = "視窗標題";
            _colTitle.ReadOnly = true;
            _colTitle.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _colPid.HeaderText = "PID";
            _colPid.ReadOnly = true;
            _colPid.Width = 70;
            _colProfile.HeaderText = "角色設定";
            _colProfile.Width = 150;
            _colProfile.FlatStyle = FlatStyle.Flat;
            _grid.Columns.AddRange(new DataGridViewColumn[] { _colUse, _colTitle, _colPid, _colProfile });
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += OnGridValueChanged;
            _grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            Controls.Add(_grid);

            // ---- 角色設定 ----
            _grpProfile.Text = "角色設定（每個角色的技能與時間）";
            _grpProfile.SetBounds(12, 182, 616, 330);
            Controls.Add(_grpProfile);

            AddLabel(_grpProfile, "編輯", 12, 25);
            _cboProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboProfile.SetBounds(50, 22, 170, 23);
            _cboProfile.SelectedIndexChanged += OnProfileSelected;
            _btnNew.Text = "新增";
            _btnNew.SetBounds(228, 20, 56, 27);
            _btnNew.Click += delegate { NewProfile(false); };
            _btnCopy.Text = "複製";
            _btnCopy.SetBounds(288, 20, 56, 27);
            _btnCopy.Click += delegate { NewProfile(true); };
            _btnRename.Text = "重新命名";
            _btnRename.SetBounds(348, 20, 76, 27);
            _btnRename.Click += delegate { RenameProfile(); };
            _btnDelete.Text = "刪除";
            _btnDelete.SetBounds(428, 20, 56, 27);
            _btnDelete.Click += delegate { DeleteProfile(); };
            _grpProfile.Controls.AddRange(new Control[] { _cboProfile, _btnNew, _btnCopy, _btnRename, _btnDelete });

            _btnDb.Text = "技能表…";
            _btnDb.SetBounds(538, 20, 70, 27);
            _btnDb.Click += delegate { ChooseSkillDb(); };
            _grpProfile.Controls.Add(_btnDb);

            AddLabel(_grpProfile, "職業", 12, 59);
            _cboJob.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboJob.SetBounds(50, 56, 170, 23);
            _cboJob.DropDownWidth = 220;
            _cboJob.MaxDropDownItems = 20;
            foreach (var j in Jobs.All()) _cboJob.Items.Add(j);
            _cboJob.SelectedIndexChanged += OnJobChanged;
            AddLabel(_grpProfile, "副職業", 226, 59);
            _cboSubJob.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboSubJob.SetBounds(274, 56, 150, 23);
            _cboSubJob.DropDownWidth = 200;
            _cboSubJob.MaxDropDownItems = 20;
            foreach (var j in Jobs.SubJobChoices()) _cboSubJob.Items.Add(j);
            _cboSubJob.SelectedIndexChanged += OnJobChanged;
            AddLabel(_grpProfile, "Lv", 428, 59);
            _numSubLv.SetBounds(448, 56, 52, 23);
            _numSubLv.Maximum = 999;
            _numSubLv.ValueChanged += OnJobChanged;
            _tip.SetToolTip(_numSubLv, "副職業等級：只列出這個等級已經學得到的技能與技能等級。0 = 不限");
            _btnImport.Text = "從遊戲匯入…";
            _btnImport.SetBounds(506, 54, 102, 27);
            _btnImport.Click += delegate { ImportFromGame(); };
            _grpProfile.Controls.AddRange(new Control[] { _cboJob, _cboSubJob, _numSubLv, _btnImport });

            AddLabel(_grpProfile, "啟用", 14, 90);
            AddLabel(_grpProfile, "按鍵", 60, 90);
            AddLabel(_grpProfile, "技能", 130, 90);
            AddLabel(_grpProfile, "等級", 294, 90);
            AddLabel(_grpProfile, "技能時間(毫秒)", 346, 90);
            AddLabel(_grpProfile, "點滑鼠", 446, 90);
            AddLabel(_grpProfile, "延遲時間(毫秒)", 512, 90);

            KeyItem[] keys = BuildKeyList();
            foreach (var k in keys) _vkByName[k.Name] = k.Vk;
            for (int i = 0; i < SlotCount; i++)
            {
                int y = 112 + i * 29, slot = i;
                _chkEnabled[i] = new CheckBox { Text = (i + 1).ToString() };
                _chkEnabled[i].SetBounds(14, y, 44, 22);
                _cboKey[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                _cboKey[i].Items.AddRange(keys);
                _cboKey[i].SetBounds(60, y, 64, 23);
                _cboSkill[i] = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 260, MaxDropDownItems = 16,
                    DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20
                };
                _cboSkill[i].DrawItem += DrawSkillItem;
                _cboSkill[i].SetBounds(130, y - 1, 158, 26);
                _cboSkill[i].SelectedIndexChanged += delegate { OnSkillChanged(slot); };
                _cboLv[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                _cboLv[i].SetBounds(294, y, 46, 23);
                _cboLv[i].SelectedIndexChanged += delegate { OnLevelChanged(slot); };
                _numInterval[i] = MakeNumber(50, 3600000, 1000);
                _numInterval[i].SetBounds(346, y, 94, 23);
                _chkMouse[i] = new CheckBox { Text = "點一下" };
                _chkMouse[i].SetBounds(446, y, 62, 22);
                _numDelay[i] = MakeNumber(0, 60000, 200);
                _numDelay[i].SetBounds(512, y, 94, 23);
                _grpProfile.Controls.AddRange(new Control[] { _chkEnabled[i], _cboKey[i], _cboSkill[i], _cboLv[i], _numInterval[i], _chkMouse[i], _numDelay[i] });
            }
            _lblHint.SetBounds(12, 290, 594, 34);
            _lblHint.ForeColor = Color.DimGray;
            _grpProfile.Controls.Add(_lblHint);

            // ---- 選項 ----
            _grpOptions.Text = "選項（所有視窗共用）";
            _grpOptions.SetBounds(12, 520, 616, 200);
            Controls.Add(_grpOptions);
            var modePanel = new Panel();
            modePanel.SetBounds(8, 18, 600, 50);
            _rbBackground.Text = "背景送出（不搶焦點，同原版，多開建議）";
            _rbBackground.SetBounds(6, 2, 560, 22);
            _rbForeground.Text = "前景送出（切到視窗 + 模擬實體按鍵，相容性較高但會搶焦點）";
            _rbForeground.SetBounds(6, 26, 560, 22);
            modePanel.Controls.Add(_rbBackground);
            modePanel.Controls.Add(_rbForeground);
            _grpOptions.Controls.Add(modePanel);

            AddLabel(_grpOptions, "滑鼠按鍵", 14, 78);
            _cboButton.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboButton.Items.AddRange(new object[] { "左鍵", "右鍵" });
            _cboButton.SetBounds(80, 75, 60, 23);

            var clickPanel = new Panel();
            clickPanel.SetBounds(8, 102, 600, 52);
            _rbCenter.Text = "點擊位置：遊戲視窗正中央（同原版）";
            _rbCenter.SetBounds(6, 2, 400, 22);
            _rbCustom.Text = "自訂";
            _rbCustom.SetBounds(6, 27, 56, 22);
            _rbCustom.CheckedChanged += delegate { UpdateClickControls(); };
            clickPanel.Controls.Add(_rbCenter);
            clickPanel.Controls.Add(_rbCustom);
            var lblX = new Label { Text = "X", AutoSize = true, Location = new Point(66, 30) };
            _numX.Maximum = 10000;
            _numX.SetBounds(82, 27, 65, 23);
            var lblY = new Label { Text = "Y", AutoSize = true, Location = new Point(154, 30) };
            _numY.Maximum = 10000;
            _numY.SetBounds(170, 27, 65, 23);
            _btnCapture.Text = "擷取座標（3 秒後）";
            _btnCapture.SetBounds(245, 25, 150, 27);
            _btnCapture.Click += OnCaptureClick;
            clickPanel.Controls.AddRange(new Control[] { lblX, _numX, lblY, _numY, _btnCapture });
            _grpOptions.Controls.Add(clickPanel);

            _chkCtrlT.Text = "執行時切到拍照模式（Ctrl+T 隱藏 UI）";
            _chkCtrlT.SetBounds(14, 164, 260, 22);
            AddLabel(_grpOptions, "多開時每個視窗錯開 (毫秒)", 300, 167);
            _numStagger.Maximum = 10000;
            _numStagger.SetBounds(476, 164, 80, 23);
            _grpOptions.Controls.AddRange(new Control[] { _cboButton, _chkCtrlT, _numStagger });

            // ---- 下方按鈕 ----
            _btnStart.Text = "開始（Ctrl+F10）";
            _btnStart.SetBounds(12, 732, 130, 32);
            _btnStart.Click += delegate { StartRun(); };
            _btnStop.Text = "停止";
            _btnStop.SetBounds(150, 732, 80, 32);
            _btnStop.Click += delegate { StopRun("已停止"); };
            _btnSave.Text = "儲存設定";
            _btnSave.SetBounds(238, 732, 90, 32);
            _btnSave.Click += delegate { SaveSettings(); _lblStatus.Text = "設定已儲存"; };
            _lblStatus.SetBounds(338, 730, 290, 44);
            Controls.AddRange(new Control[] { _btnStart, _btnStop, _btnSave, _lblStatus });
        }

        private static KeyItem[] BuildKeyList()
        {
            var list = new List<KeyItem>();
            for (int i = 1; i <= 12; i++) list.Add(new KeyItem { Name = "F" + i, Vk = 0x6F + i });
            for (int i = 1; i <= 9; i++) list.Add(new KeyItem { Name = i.ToString(), Vk = 0x30 + i });
            list.Add(new KeyItem { Name = "0", Vk = 0x30 });
            for (char c = 'A'; c <= 'Z'; c++) list.Add(new KeyItem { Name = c.ToString(), Vk = c });
            list.Add(new KeyItem { Name = "Space", Vk = 0x20 });
            list.Add(new KeyItem { Name = "Tab", Vk = 0x09 });
            return list.ToArray();
        }

        private static NumericUpDown MakeNumber(int min, int max, int value)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Increment = 100, ThousandsSeparator = true };
        }

        private static void AddLabel(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) });
        }

        // =====================================================================
        // 視窗清單
        // =====================================================================

        private void RefreshWindows()
        {
            var keepChecked = new HashSet<IntPtr>();
            var keepProfile = new Dictionary<IntPtr, string>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var w = (WindowItem)row.Tag;
                if (Convert.ToBoolean(row.Cells[_colUse.Index].Value)) keepChecked.Add(w.Hwnd);
                keepProfile[w.Hwnd] = Convert.ToString(row.Cells[_colProfile.Index].Value);
            }

            string keyword = _txtKeyword.Text.Trim();
            var found = new List<WindowItem>();
            IntPtr self = Handle;
            // 用 EnumWindows 一次取得所有頂層視窗，取代原版逐一檢查 600 萬個視窗代碼的做法。
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (h == self || !Native.IsWindowVisible(h)) return true;
                string title = Native.GetTitle(h);
                if (title.Length == 0) return true;
                if (keyword.Length > 0 && title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) return true;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                found.Add(new WindowItem { Hwnd = h, Title = title, Pid = pid });
                return true;
            }, IntPtr.Zero);

            // 遊戲視窗的標題通常都是「eco」，多開時會重複；標題重複時不能用標題記憶，否則每個視窗都會變成同一個角色設定。
            var titleCount = found.GroupBy(x => x.Title).ToDictionary(x => x.Key, x => x.Count());

            RefreshProfileColumn();
            _grid.Rows.Clear();
            foreach (var w in found)
            {
                string profile;
                if (!keepProfile.TryGetValue(w.Hwnd, out profile) || _store.Find(profile) == null)
                {
                    if (titleCount[w.Title] > 1 || !_assignByTitle.TryGetValue(w.Title, out profile) || _store.Find(profile) == null)
                        profile = ProfileStore.DefaultName;
                }
                int idx = _grid.Rows.Add(keepChecked.Contains(w.Hwnd), w.Title, w.Pid.ToString(), _store.Find(profile).Name);
                _grid.Rows[idx].Tag = w;
            }
            _lblStatus.Text = string.Format("找到 {0} 個視窗", found.Count);
        }

        private void RefreshProfileColumn()
        {
            var names = _store.All.Select(p => p.Name).ToList();
            _colProfile.Items.Clear();
            foreach (string n in names) _colProfile.Items.Add(n);
            foreach (DataGridViewRow row in _grid.Rows)
            {
                string v = Convert.ToString(row.Cells[_colProfile.Index].Value);
                if (!names.Contains(v)) row.Cells[_colProfile.Index].Value = ProfileStore.DefaultName;
            }
        }

        private void OnGridValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _colProfile.Index) return;
            var row = _grid.Rows[e.RowIndex];
            var w = row.Tag as WindowItem;
            if (w == null) return;
            // 只有標題唯一時才記住「視窗標題 → 角色設定」，供重開小雅時自動套用。
            int same = _grid.Rows.Cast<DataGridViewRow>().Count(r => r.Tag is WindowItem && ((WindowItem)r.Tag).Title == w.Title);
            if (same == 1) _assignByTitle[w.Title] = Convert.ToString(row.Cells[_colProfile.Index].Value);
        }

        // =====================================================================
        // 角色設定
        // =====================================================================

        private void RefreshProfileCombo(Profile select)
        {
            _loadingEditor = true;
            _cboProfile.Items.Clear();
            foreach (var p in _store.All) _cboProfile.Items.Add(p.Name);
            _loadingEditor = false;
            _cboProfile.SelectedItem = select.Name;
            RefreshProfileColumn();
            _btnDelete.Enabled = _btnRename.Enabled = _current != null && _current.Name != ProfileStore.DefaultName;
        }

        private void OnProfileSelected(object sender, EventArgs e)
        {
            if (_loadingEditor) return;
            var p = _store.Find(Convert.ToString(_cboProfile.SelectedItem));
            if (p == null || p == _current) return;
            if (_current != null) EditorToProfile(_current);
            _current = p;
            ProfileToEditor(_current);
            _btnDelete.Enabled = _btnRename.Enabled = p.Name != ProfileStore.DefaultName;
        }

        private void ProfileToEditor(Profile p)
        {
            _loadingEditor = true;
            SelectJob(_cboJob, p.Job);
            SelectJob(_cboSubJob, p.SubJob);
            _numSubLv.Value = Clamp(_numSubLv, p.SubJobLevel);
            _cboJob.Enabled = _cboSubJob.Enabled = _db != null;
            _numSubLv.Enabled = _db != null && !string.IsNullOrEmpty(p.SubJob);
            for (int i = 0; i < SlotCount; i++)
            {
                var d = p.Slots[i];
                _chkEnabled[i].Checked = d.Enabled;
                SelectKey(_cboKey[i], d.KeyName);
                PopulateSkillCombo(i, p, d);
                _numInterval[i].Value = Clamp(_numInterval[i], d.IntervalMs);
                _numInterval[i].BackColor = SystemColors.Window;
                _chkMouse[i].Checked = d.Mouse;
                _numDelay[i].Value = Clamp(_numDelay[i], d.DelayMs);
            }
            _lblHint.Text = _db == null
                ? "尚未載入技能表：按「技能表…」選擇 技能表.csv 後，就能依職業選擇技能並自動帶入時間。"
                : "選好職業後，在「技能」欄挑選技能與等級，技能時間和延遲時間會自動帶入（仍可手動修改）。";
            _loadingEditor = false;
        }

        private void EditorToProfile(Profile p)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                var d = p.Slots[i];
                d.Enabled = _chkEnabled[i].Checked;
                d.KeyName = _cboKey[i].SelectedItem != null ? _cboKey[i].SelectedItem.ToString() : "F" + (i + 1);
                d.IntervalMs = (int)_numInterval[i].Value;
                d.Mouse = _chkMouse[i].Checked;
                d.DelayMs = (int)_numDelay[i].Value;
                var entry = ChosenEntry(i);
                if (entry == null)
                {
                    d.SkillName = ""; d.SkillLevel = 0; d.SkillLabel = "";
                }
                else
                {
                    int lv = _cboLv[i].SelectedItem is int ? (int)_cboLv[i].SelectedItem : d.SkillLevel;
                    d.SkillName = entry.EnName; d.SkillLevel = lv; d.SkillLabel = entry.Display + " Lv" + lv;
                }
            }
        }

        private static void SelectJob(ComboBox combo, string slug)
        {
            foreach (JobInfo j in combo.Items)
                if (j.Slug == (slug ?? "")) { combo.SelectedItem = j; return; }
            combo.SelectedIndex = 0;
        }

        private SkillEntry ChosenEntry(int i)
        {
            var c = _cboSkill[i].SelectedItem as SkillChoice;
            return c != null ? c.Entry : null;
        }

        /// <summary>
        /// 主職業與副職業可選的技能（主職業在前；兩邊都有的技能只列一次，算主職業）。
        /// 副職業技能來自遊戲的 dualjob_skill_learn.csv：只列體系內學得到的技能，等級上限也依表格（與副職業等級）限制。
        /// </summary>
        private List<SkillChoice> ChoicesFor(Profile p)
        {
            var list = new List<SkillChoice>();
            if (_db == null) return list;
            var seen = new HashSet<string>();
            foreach (var e in _db.SkillsOf(p.Job))
                if (seen.Add(Normalize(e.EnName))) list.Add(new SkillChoice { Entry = e });
            int system;
            if (_dj != null && int.TryParse(p.SubJob, out system))
            {
                foreach (var s in _dj.SkillsOf(system))
                {
                    int max = s.MaxSkillLevel(p.SubJobLevel);
                    if (max == 0) continue;
                    var e = _db.FindById(s.SkillId);
                    if (e == null || e.IsPassive) continue;
                    if (seen.Add(Normalize(e.EnName))) list.Add(new SkillChoice { Entry = e, Sub = true, MaxLevel = max });
                }
            }
            return list;
        }

        /// <summary>技能下拉：「手動設定」＋主／副職業的技能。角色設定裡已有、但不在清單中的技能也會保留。</summary>
        private void PopulateSkillCombo(int i, Profile p, SlotData d)
        {
            var combo = _cboSkill[i];
            combo.BeginUpdate();
            combo.Items.Clear();
            combo.Items.Add(ManualItem);
            SkillChoice selected = null;
            foreach (var c in ChoicesFor(p))
            {
                combo.Items.Add(c);
                if (selected == null && !string.IsNullOrEmpty(d.SkillName) && SameSkill(c.Entry.EnName, d.SkillName)) selected = c;
            }
            if (selected == null && !string.IsNullOrEmpty(d.SkillName))
            {
                SkillEntry other = _db != null ? _db.SkillsOf(Jobs.AllSlug).FirstOrDefault(e => SameSkill(e.EnName, d.SkillName)) : null;
                // 技能表沒有這個技能（或未載入技能表）：保留成一個只有名稱的項目，避免資料遺失。
                if (other == null) other = new SkillEntry { EnName = d.SkillName, ZhName = d.SkillLabel.Length > 0 ? d.SkillLabel : d.SkillName };
                selected = new SkillChoice { Entry = other };
                combo.Items.Insert(1, selected);
            }
            combo.SelectedItem = selected != null ? (object)selected : ManualItem;
            combo.EndUpdate();
            PopulateLevelCombo(i, selected, d.SkillLevel);
        }

        private void PopulateLevelCombo(int i, SkillChoice choice, int level)
        {
            var combo = _cboLv[i];
            combo.Items.Clear();
            combo.Enabled = choice != null;
            if (choice == null) return;
            var entry = choice.Entry;
            foreach (int lv in entry.Levels.Keys)
                if (choice.MaxLevel == 0 || lv <= choice.MaxLevel) combo.Items.Add(lv);
            if (combo.Items.Count == 0 && choice.MaxLevel > 0)
                for (int lv = 1; lv <= choice.MaxLevel; lv++) combo.Items.Add(lv);
            if (combo.Items.Count == 0 && level > 0) combo.Items.Add(level);
            if (combo.Items.Count == 0) return;
            if (combo.Items.Contains(level)) combo.SelectedItem = level;
            else combo.SelectedIndex = combo.Items.Count - 1; // 預設最高等級
        }

        private static bool SameSkill(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
        }

        private static string Normalize(string s)
        {
            return new string((s ?? "").Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private void OnSkillChanged(int i)
        {
            if (_loadingEditor) return;
            _loadingEditor = true;
            var choice = _cboSkill[i].SelectedItem as SkillChoice;
            var entry = choice != null ? choice.Entry : null;
            PopulateLevelCombo(i, choice, int.MaxValue); // 預設可選的最高等級
            _loadingEditor = false;
            if (entry == null)
            {
                _numInterval[i].BackColor = SystemColors.Window;
                _lblHint.Text = string.Format("第 {0} 格改為手動設定：請自行填寫按鍵、技能時間與延遲時間。", i + 1);
                return;
            }
            ApplyChosenSkill(i);
        }

        private void OnLevelChanged(int i)
        {
            if (_loadingEditor) return;
            ApplyChosenSkill(i);
        }

        /// <summary>依選到的技能與等級，自動帶入技能時間與延遲時間。</summary>
        private void ApplyChosenSkill(int i)
        {
            var entry = ChosenEntry(i);
            if (entry == null || !(_cboLv[i].SelectedItem is int)) return;
            int lv = (int)_cboLv[i].SelectedItem;
            var temp = new SlotData { SkillName = entry.EnName, SkillLevel = lv, IntervalMs = (int)_numInterval[i].Value, DelayMs = (int)_numDelay[i].Value };
            bool ok = GameShortcuts.ApplySuggestion(temp, _db);
            _numInterval[i].Value = Clamp(_numInterval[i], temp.IntervalMs);
            _numDelay[i].Value = Clamp(_numDelay[i], temp.DelayMs);
            _chkEnabled[i].Checked = ok;
            _numInterval[i].BackColor = ok ? SystemColors.Window : Color.LightYellow;

            SkillInfo info;
            entry.Levels.TryGetValue(lv, out info);
            string times = info == null ? "技能表沒有這個等級的資料"
                : string.Format("詠唱 {0}、冷卻 {1}、持續 {2}", Sec(info.CastSec), Sec(info.CooldownSec), Sec(info.DurationSec));
            _lblHint.Text = string.Format("第 {0} 格 {1} Lv{2}：{3}。{4}", i + 1, entry.Display, lv, times,
                ok ? string.Format("已帶入技能時間 {0:N1} 秒、延遲 {1:N1} 秒。", temp.IntervalMs / 1000.0, temp.DelayMs / 1000.0)
                   : "時間無法可靠推算（黃色欄位），請確認技能時間後再勾選啟用。");
        }

        private static string Sec(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.##") + " 秒" : "—";
        }

        private void OnJobChanged(object sender, EventArgs e)
        {
            if (_loadingEditor || _current == null) return;
            EditorToProfile(_current);
            var job = (JobInfo)_cboJob.SelectedItem;
            var sub = (JobInfo)_cboSubJob.SelectedItem;
            _current.Job = job.Slug;
            _current.SubJob = sub != null ? sub.Slug : "";
            _current.SubJobLevel = (int)_numSubLv.Value;
            if (_current.SubJob.Length > 0 && _dj == null && !EnsureDualJobData(true))
            {
                // 讀不到副職業表：不套用副職業，避免列出錯誤的技能。
                _current.SubJob = "";
                ProfileToEditor(_current);
                _lblHint.Text = "沒有載入副職業表（需要遊戲資料夾），所以先不套用副職業。";
                return;
            }
            // 不屬於主職業或副職業的技能清掉並停用，避免誤送。
            var choices = ChoicesFor(_current);
            int cleared = 0;
            if (_db != null)
            {
                var names = new HashSet<string>(choices.Select(x => Normalize(x.Entry.EnName)));
                foreach (var d in _current.Slots)
                {
                    if (string.IsNullOrEmpty(d.SkillName) || names.Contains(Normalize(d.SkillName))) continue;
                    d.SkillName = ""; d.SkillLevel = 0; d.SkillLabel = ""; d.Enabled = false;
                    cleared++;
                }
            }
            var levelsBefore = _current.Slots.Select(d => d.SkillLevel).ToArray();
            ProfileToEditor(_current);
            // 副職業等級調低時，超過上限的技能等級會在下拉中降到可用的最高等級；重新計算這些格的時間。
            int lowered = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (ChosenEntry(i) == null || !(_cboLv[i].SelectedItem is int)) continue;
                if ((int)_cboLv[i].SelectedItem == levelsBefore[i]) continue;
                ApplyChosenSkill(i);
                lowered++;
            }
            if (lowered > 0) EditorToProfile(_current);
            int mainCount = choices.Count(c => !c.Sub), subCount = choices.Count(c => c.Sub);
            string what = _current.SubJob.Length > 0
                ? string.Format("{0}＋{1}{2}：可選 {3} 個技能（主 {4}、［副］{5}）",
                    job.Display, ((JobInfo)_cboSubJob.SelectedItem).Display,
                    _current.SubJobLevel > 0 ? " Lv" + _current.SubJobLevel : "", choices.Count, mainCount, subCount)
                : string.Format("{0}：可選 {1} 個技能", job.Display, mainCount);
            _lblHint.Text = what + "。"
                + (cleared > 0 ? string.Format("{0} 格技能已不可用，已清除並停用。", cleared) : "")
                + (lowered > 0 ? string.Format("{0} 格技能等級超過副職業上限，已降級並重算時間。", lowered) : "");
        }

        /// <summary>從遊戲資料夾載入技能圖示（只載入索引，圖示用到時才解碼）。</summary>
        private void LoadIcons()
        {
            if (_icons != null || !DualJobDb.LooksLikeGameDir(_gameDir)) return;
            try { _icons = SkillIcons.Load(_gameDir); }
            catch (Exception) { _icons = null; }
            if (_icons != null) foreach (var c in _cboSkill) c.Invalidate();
        }

        /// <summary>技能下拉：左邊畫技能圖示，右邊是名稱。</summary>
        private void DrawSkillItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            var combo = (ComboBox)sender;
            object item = combo.Items[e.Index];
            var choice = item as SkillChoice;
            int x = e.Bounds.Left + 2;
            Image icon = choice != null && _icons != null ? _icons.Get(choice.Entry.Id) : null;
            if (icon != null)
            {
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                int size = Math.Min(18, e.Bounds.Height - 2);
                e.Graphics.DrawImage(icon, new Rectangle(x, e.Bounds.Top + (e.Bounds.Height - size) / 2, size, size));
            }
            x += 22;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            Color fore = selected ? SystemColors.HighlightText : (choice == null ? Color.DimGray : SystemColors.ControlText);
            TextRenderer.DrawText(e.Graphics, item.ToString(), e.Font,
                new Rectangle(x, e.Bounds.Top, e.Bounds.Right - x, e.Bounds.Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        /// <summary>從正在執行的遊戲程序（視窗清單裡的視窗、或名為 eco 的程序）找出遊戲資料夾。</summary>
        private string DetectGameDir()
        {
            var pids = new List<int>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var w = row.Tag as WindowItem;
                if (w != null) pids.Add((int)w.Pid);
            }
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("eco")) pids.Add(p.Id);
            foreach (int pid in pids.Distinct())
            {
                try
                {
                    using (var p = System.Diagnostics.Process.GetProcessById(pid))
                    {
                        string dir = Path.GetDirectoryName(p.MainModule.FileName);
                        if (DualJobDb.LooksLikeGameDir(dir)) return dir;
                    }
                }
                catch (Exception) { /* 權限不足或程序已結束：試下一個 */ }
            }
            return null;
        }

        /// <summary>載入遊戲裡的副職業表；需要遊戲資料夾，必要時詢問使用者。</summary>
        private bool EnsureDualJobData(bool prompt)
        {
            if (_dj != null) return true;
            if (!DualJobDb.LooksLikeGameDir(_gameDir))
            {
                // 先從正在執行的遊戲找出安裝資料夾，找不到才詢問使用者。
                string detected = DetectGameDir();
                if (detected != null)
                {
                    _gameDir = detected;
                    if (_current != null) SaveSettings(); // 啟動載入中不存檔，避免寫入不完整的設定
                }
            }
            if (!DualJobDb.LooksLikeGameDir(_gameDir))
            {
                if (!prompt) return false;
                using (var dlg = new FolderBrowserDialog { Description = "副職業的可用技能要從遊戲資料讀取。請選擇遊戲資料夾（裡面有 eco.exe 與 data 資料夾）" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                    if (!DualJobDb.LooksLikeGameDir(dlg.SelectedPath))
                    {
                        MessageBox.Show(this, "這個資料夾裡找不到 data\\xls\\table.hed，請確認是遊戲安裝資料夾。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    _gameDir = dlg.SelectedPath;
                    if (_current != null) SaveSettings();
                }
            }
            LoadIcons();
            try
            {
                _dj = DualJobDb.Load(_gameDir);
                return true;
            }
            catch (Exception ex)
            {
                if (prompt) MessageBox.Show(this, "讀取副職業表失敗：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void NewProfile(bool copy)
        {
            EditorToProfile(_current);
            string suggestion = copy ? _current.Name + "-複製" : "新角色";
            using (var dlg = new PromptDialog(copy ? "複製角色設定" : "新增角色設定", "角色設定名稱：", suggestion, n => _store.Validate(n, null)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var p = copy ? _current.Clone(dlg.Value) : new Profile(dlg.Value);
                _store.Add(p);
                RefreshProfileCombo(p);
            }
        }

        private void RenameProfile()
        {
            if (_current.Name == ProfileStore.DefaultName) return;
            EditorToProfile(_current);
            string oldName = _current.Name;
            using (var dlg = new PromptDialog("重新命名角色設定", "新名稱：", oldName, n => _store.Validate(n, _current)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string newName = dlg.Value;
                // 先記下哪些視窗用的是舊名稱，改名後再指回新名稱。
                var rows = _grid.Rows.Cast<DataGridViewRow>()
                    .Where(r => Convert.ToString(r.Cells[_colProfile.Index].Value) == oldName).ToList();
                _store.Rename(_current, newName);
                foreach (string t in _assignByTitle.Keys.ToList())
                    if (_assignByTitle[t] == oldName) _assignByTitle[t] = newName;
                RefreshProfileCombo(_current);
                foreach (var r in rows) r.Cells[_colProfile.Index].Value = newName;
            }
        }

        private void DeleteProfile()
        {
            if (_current.Name == ProfileStore.DefaultName) return;
            if (MessageBox.Show(this, "確定要刪除角色設定「" + _current.Name + "」嗎？\n使用這個設定的視窗會改回「" + ProfileStore.DefaultName + "」。",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var victim = _current;
            _store.Delete(victim);
            foreach (string t in _assignByTitle.Keys.ToList())
                if (_assignByTitle[t] == victim.Name) _assignByTitle.Remove(t);
            _current = _store.Find(ProfileStore.DefaultName);
            ProfileToEditor(_current);
            RefreshProfileCombo(_current);
        }

        private void ImportFromGame()
        {
            EditorToProfile(_current);
            using (var dlg = new ImportDialog(_gameDir, _db, _store, _current))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _gameDir = dlg.GameDir;
                Profile target;
                if (dlg.CreateNew)
                {
                    target = new Profile(dlg.NewName);
                    _store.Add(target);
                }
                else target = _current;
                int skipped = GameShortcuts.ApplyTo(target, dlg.Selected, _db);
                if (_db != null)
                {
                    string main, sub;
                    _db.GuessJobs(dlg.Selected.Shortcuts.Where(s => s.SkillName != null).Select(s => s.SkillName), out main, out sub);
                    target.Job = main;
                    // 副職業是整個體系；和主職業同體系的技能不需要副職業。
                    string subSystem = Jobs.SystemOfJob(sub);
                    target.SubJob = subSystem.Length > 0 && subSystem != Jobs.SystemOfJob(main) && EnsureDualJobData(false) ? subSystem : "";
                }
                _store.Save(target);
                _current = target;
                ProfileToEditor(_current);
                RefreshProfileCombo(_current);
                _lblStatus.Text = string.Format("已從 {0} 匯入到「{1}」{2}", Path.GetFileName(dlg.Selected.Path), target.Name,
                    skipped > 0 ? string.Format("（{0} 個超過 {1} 格未匯入）", skipped, SlotCount) : "");
                SaveSettings();
            }
        }

        private void ChooseSkillDb()
        {
            using (var dlg = new OpenFileDialog { Filter = "技能表 (*.csv)|*.csv|所有檔案 (*.*)|*.*", Title = "選擇 技能表.csv" })
            {
                if (File.Exists(_skillDbPath)) dlg.FileName = _skillDbPath;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                EditorToProfile(_current);
                _skillDbPath = dlg.FileName;
                LoadSkillDb(true);
                ProfileToEditor(_current);
                SaveSettings();
            }
        }

        private void LoadSkillDb(bool showErrors)
        {
            _db = null;
            string path = _skillDbPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                // 預設位置：exe 旁邊，或與「優化版」同層的「技能資料」資料夾
                foreach (string cand in new[]
                {
                    Path.Combine(_baseDir, "技能表.csv"),
                    Path.GetFullPath(Path.Combine(_baseDir, @"..\..\技能資料\技能表.csv")),
                    Path.GetFullPath(Path.Combine(_baseDir, @"..\技能資料\技能表.csv"))
                })
                {
                    if (File.Exists(cand)) { path = cand; break; }
                }
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _lblDb.Text = "未載入技能表";
                return;
            }
            try
            {
                _db = SkillDb.Load(path);
                _skillDbPath = path;
                _lblDb.Text = "技能表：" + Path.GetFileName(path) + "（" + _db.Count + " 筆）";
                _tip.SetToolTip(_btnDb, "目前的技能表：" + path + "（" + _db.Count + " 筆）");
            }
            catch (Exception ex)
            {
                _lblDb.Text = "技能表讀取失敗";
                if (showErrors) MessageBox.Show(this, "技能表讀取失敗：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // =====================================================================
        // 開始 / 停止
        // =====================================================================

        private void StartRun()
        {
            if (_scheduler != null) return;
            _grid.EndEdit();
            EditorToProfile(_current);

            var targets = new List<KeyValuePair<IntPtr, SlotConfig[]>>();
            var noSkill = new List<string>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!Convert.ToBoolean(row.Cells[_colUse.Index].Value)) continue;
                var w = (WindowItem)row.Tag;
                var p = _store.Find(Convert.ToString(row.Cells[_colProfile.Index].Value)) ?? _store.Find(ProfileStore.DefaultName);
                SlotConfig[] slots = ToSlotConfigs(p);
                if (!slots.Any(s => s.Enabled)) { noSkill.Add(p.Name); continue; }
                targets.Add(new KeyValuePair<IntPtr, SlotConfig[]>(w.Hwnd, slots));
            }
            if (targets.Count == 0)
            {
                MessageBox.Show(this, noSkill.Count > 0
                    ? "勾選的視窗使用的角色設定都沒有啟用任何技能：" + string.Join("、", noSkill.Distinct())
                    : "請先勾選至少一個遊戲視窗。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (noSkill.Count > 0)
            {
                if (MessageBox.Show(this, "下列角色設定沒有啟用任何技能，使用它們的視窗會被略過：\n" + string.Join("、", noSkill.Distinct()) + "\n\n要繼續嗎？",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }

            _activeWindows = targets.Count;
            _scheduler = new Scheduler(ReadRunConfig(), targets);
            _scheduler.WindowLost += h => BeginInvoke((Action)delegate
            {
                _activeWindows--;
                _lblStatus.Text = string.Format("視窗 0x{0:X8} 已關閉，已移除", h.ToInt64());
            });
            _scheduler.Finished += () => BeginInvoke((Action)delegate { StopRun("所有視窗都已關閉，自動停止"); });
            _scheduler.Start();
            SaveSettings();
            SetRunning(true);
            UpdateStatus();
        }

        private SlotConfig[] ToSlotConfigs(Profile p)
        {
            var result = new SlotConfig[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                var d = p.Slots[i];
                int vk;
                bool known = _vkByName.TryGetValue(d.KeyName ?? "", out vk);
                result[i] = new SlotConfig
                {
                    Enabled = d.Enabled && known,
                    VirtualKey = vk,
                    IntervalMs = d.IntervalMs,
                    Mouse = d.Mouse,
                    DelayMs = d.DelayMs
                };
            }
            return result;
        }

        private RunConfig ReadRunConfig()
        {
            return new RunConfig
            {
                Mode = _rbForeground.Checked ? SendMode.Foreground : SendMode.Background,
                RightButton = _cboButton.SelectedIndex == 1,
                ClickCenter = !_rbCustom.Checked,
                ClickX = (int)_numX.Value,
                ClickY = (int)_numY.Value,
                StaggerMs = (int)_numStagger.Value,
                SendCtrlT = _chkCtrlT.Checked
            };
        }

        private void StopRun(string message)
        {
            if (_scheduler != null)
            {
                _scheduler.Stop();
                _scheduler = null;
            }
            SetRunning(false);
            _lblStatus.Text = message;
        }

        private void SetRunning(bool running)
        {
            _btnStart.Enabled = !running;
            _btnStop.Enabled = running;
            _btnRefresh.Enabled = !running;
            _grid.Enabled = !running;
            _grpProfile.Enabled = !running;
            _grpOptions.Enabled = !running;
            _txtKeyword.Enabled = !running;
            if (running) _uiTimer.Start(); else _uiTimer.Stop();
        }

        private void UpdateStatus()
        {
            if (_scheduler == null) return;
            _lblStatus.Text = string.Format("執行中：{0} 個視窗\n已送出 {1} 次按鍵", _activeWindows, _scheduler.SentCount);
        }

        // =====================================================================
        // 擷取座標
        // =====================================================================

        private void OnCaptureClick(object sender, EventArgs e)
        {
            _btnCapture.Enabled = false;
            _lblStatus.Text = "3 秒內把滑鼠移到遊戲視窗內的目標位置…";
            _captureTimer.Start();
        }

        private void OnCaptureTick(object sender, EventArgs e)
        {
            _captureTimer.Stop();
            _btnCapture.Enabled = true;
            Native.POINT p;
            Native.GetCursorPos(out p);
            IntPtr root = Native.GetAncestor(Native.WindowFromPoint(p), Native.GA_ROOT);
            if (root == IntPtr.Zero || root == Handle)
            {
                _lblStatus.Text = "游標不在遊戲視窗上，請再試一次";
                return;
            }
            Native.ScreenToClient(root, ref p);
            _numX.Value = Math.Max(0, Math.Min(_numX.Maximum, p.X));
            _numY.Value = Math.Max(0, Math.Min(_numY.Maximum, p.Y));
            _rbCustom.Checked = true;
            _lblStatus.Text = string.Format("已擷取「{0}」內座標 ({1}, {2})", Native.GetTitle(root), p.X, p.Y);
        }

        private void UpdateClickControls()
        {
            _numX.Enabled = _rbCustom.Checked;
            _numY.Enabled = _rbCustom.Checked;
        }

        // =====================================================================
        // 設定檔
        // =====================================================================

        private void LoadSettings()
        {
            var s = new Settings(_settingsPath);
            _txtKeyword.Text = s.Get("keyword", "eco");
            bool fg = s.GetInt("mode", 0) == 1;
            _rbForeground.Checked = fg;
            _rbBackground.Checked = !fg;
            _cboButton.SelectedIndex = s.GetBool("mouse.right", false) ? 1 : 0;
            bool center = s.GetBool("mouse.center", true);
            _rbCenter.Checked = center;
            _rbCustom.Checked = !center;
            _numX.Value = Clamp(_numX, s.GetInt("mouse.x", 0));
            _numY.Value = Clamp(_numY, s.GetInt("mouse.y", 0));
            _numStagger.Value = Clamp(_numStagger, s.GetInt("stagger", 50));
            _chkCtrlT.Checked = s.GetBool("ctrlT", true);
            _gameDir = s.Get("gameDir", "");
            _skillDbPath = s.Get("skillDb", "");
            UpdateClickControls();

            foreach (string k in s.Keys())
                if (k.StartsWith("assign.", StringComparison.Ordinal)) _assignByTitle[k.Substring(7)] = s.Get(k, "");

            LoadSkillDb(false);
            EnsureDualJobData(false);
            _store.Load();
            MigrateOldSlots(s);
            _current = _store.Find(s.Get("profile", ProfileStore.DefaultName)) ?? _store.Find(ProfileStore.DefaultName);
            ProfileToEditor(_current);
            RefreshProfileCombo(_current);
        }

        /// <summary>舊版把技能設定存在主設定檔；第一次執行新版時搬到「預設」角色設定。</summary>
        private void MigrateOldSlots(Settings s)
        {
            if (s.Get("slot1.key", null) == null) return;
            var def = _store.Find(ProfileStore.DefaultName);
            if (s.GetBool("migrated", false)) return;
            for (int i = 0; i < SlotCount; i++)
            {
                string n = "slot" + (i + 1) + ".";
                var d = def.Slots[i];
                d.Enabled = s.GetBool(n + "enabled", false);
                d.KeyName = s.Get(n + "key", "F" + (i + 1));
                d.IntervalMs = s.GetInt(n + "interval", 1000);
                d.Mouse = s.GetBool(n + "mouse", false);
                d.DelayMs = s.GetInt(n + "delay", 200);
            }
            _store.Save(def);
        }

        private void SaveSettings()
        {
            if (_current != null) EditorToProfile(_current);
            try { _store.SaveAll(); }
            catch (Exception ex) { _lblStatus.Text = "角色設定儲存失敗：" + ex.Message; }

            var s = new Settings(_settingsPath, false);
            s.Set("keyword", _txtKeyword.Text.Trim());
            s.Set("mode", _rbForeground.Checked ? 1 : 0);
            s.Set("mouse.right", _cboButton.SelectedIndex == 1);
            s.Set("mouse.center", !_rbCustom.Checked);
            s.Set("mouse.x", (int)_numX.Value);
            s.Set("mouse.y", (int)_numY.Value);
            s.Set("stagger", (int)_numStagger.Value);
            s.Set("ctrlT", _chkCtrlT.Checked);
            s.Set("gameDir", _gameDir);
            s.Set("skillDb", _skillDbPath);
            s.Set("profile", _current != null ? _current.Name : ProfileStore.DefaultName);
            s.Set("migrated", true);
            foreach (var kv in _assignByTitle)
                if (_store.Find(kv.Value) != null) s.Set("assign." + kv.Key, kv.Value);
            try { s.Save(); }
            catch (Exception ex) { _lblStatus.Text = "設定儲存失敗：" + ex.Message; }
        }

        private static void SelectKey(ComboBox combo, string name)
        {
            foreach (object o in combo.Items)
            {
                if (string.Equals(o.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = o;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        private static decimal Clamp(NumericUpDown n, int v)
        {
            return Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }

        // =====================================================================
        // 全域快捷鍵
        // =====================================================================

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RegisterHotKey(Handle, HotkeyId, Native.MOD_CONTROL, VK_F10);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (_scheduler == null) StartRun(); else StopRun("已停止（快捷鍵）");
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopRun("");
            SaveSettings();
            if (_icons != null) _icons.Dispose();
            Native.UnregisterHotKey(Handle, HotkeyId);
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}

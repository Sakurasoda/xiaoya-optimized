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
        private readonly Button _btnImport = new Button();
        private readonly Button _btnRecalc = new Button();
        private readonly Button _btnDb = new Button();
        private readonly Label _lblDb = new Label();
        private readonly CheckBox[] _chkEnabled = new CheckBox[SlotCount];
        private readonly ComboBox[] _cboKey = new ComboBox[SlotCount];
        private readonly Label[] _lblSkill = new Label[SlotCount];
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
            ClientSize = new Size(640, 760);
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
            _grpProfile.SetBounds(12, 182, 616, 300);
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

            _btnImport.Text = "從遊戲匯入快捷列…";
            _btnImport.SetBounds(12, 54, 150, 27);
            _btnImport.Click += delegate { ImportFromGame(); };
            _btnRecalc.Text = "依技能表重算時間";
            _btnRecalc.SetBounds(166, 54, 140, 27);
            _btnRecalc.Click += delegate { RecalcTimes(); };
            _btnDb.Text = "技能表…";
            _btnDb.SetBounds(310, 54, 70, 27);
            _btnDb.Click += delegate { ChooseSkillDb(); };
            _lblDb.SetBounds(386, 59, 222, 20);
            _lblDb.AutoEllipsis = true;
            _lblDb.ForeColor = Color.DimGray;
            _grpProfile.Controls.AddRange(new Control[] { _btnImport, _btnRecalc, _btnDb, _lblDb });

            AddLabel(_grpProfile, "啟用", 14, 90);
            AddLabel(_grpProfile, "按鍵", 70, 90);
            AddLabel(_grpProfile, "技能", 150, 90);
            AddLabel(_grpProfile, "技能時間(毫秒)", 330, 90);
            AddLabel(_grpProfile, "點滑鼠", 440, 90);
            AddLabel(_grpProfile, "延遲時間(毫秒)", 508, 90);

            KeyItem[] keys = BuildKeyList();
            foreach (var k in keys) _vkByName[k.Name] = k.Vk;
            for (int i = 0; i < SlotCount; i++)
            {
                int y = 112 + i * 29;
                _chkEnabled[i] = new CheckBox { Text = (i + 1).ToString() };
                _chkEnabled[i].SetBounds(14, y, 50, 22);
                _cboKey[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                _cboKey[i].Items.AddRange(keys);
                _cboKey[i].SetBounds(70, y, 72, 23);
                _lblSkill[i] = new Label { AutoEllipsis = true, BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleLeft };
                _lblSkill[i].SetBounds(150, y, 172, 23);
                _numInterval[i] = MakeNumber(50, 3600000, 1000);
                _numInterval[i].SetBounds(330, y, 100, 23);
                _chkMouse[i] = new CheckBox { Text = "點一下" };
                _chkMouse[i].SetBounds(440, y, 64, 22);
                _numDelay[i] = MakeNumber(0, 60000, 200);
                _numDelay[i].SetBounds(508, y, 96, 23);
                _grpProfile.Controls.AddRange(new Control[] { _chkEnabled[i], _cboKey[i], _lblSkill[i], _numInterval[i], _chkMouse[i], _numDelay[i] });
            }

            // ---- 選項 ----
            _grpOptions.Text = "選項（所有視窗共用）";
            _grpOptions.SetBounds(12, 490, 616, 200);
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
            _btnStart.SetBounds(12, 702, 130, 32);
            _btnStart.Click += delegate { StartRun(); };
            _btnStop.Text = "停止";
            _btnStop.SetBounds(150, 702, 80, 32);
            _btnStop.Click += delegate { StopRun("已停止"); };
            _btnSave.Text = "儲存設定";
            _btnSave.SetBounds(238, 702, 90, 32);
            _btnSave.Click += delegate { SaveSettings(); _lblStatus.Text = "設定已儲存"; };
            _lblStatus.SetBounds(338, 700, 290, 44);
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
            for (int i = 0; i < SlotCount; i++)
            {
                var d = p.Slots[i];
                _chkEnabled[i].Checked = d.Enabled;
                SelectKey(_cboKey[i], d.KeyName);
                _numInterval[i].Value = Clamp(_numInterval[i], d.IntervalMs);
                _chkMouse[i].Checked = d.Mouse;
                _numDelay[i].Value = Clamp(_numDelay[i], d.DelayMs);
                _lblSkill[i].Text = string.IsNullOrEmpty(d.SkillLabel) ? "（手動設定）" : d.SkillLabel;
                _lblSkill[i].ForeColor = string.IsNullOrEmpty(d.SkillName) ? Color.DimGray : SystemColors.ControlText;
            }
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
                _store.Save(target);
                _current = target;
                ProfileToEditor(_current);
                RefreshProfileCombo(_current);
                _lblStatus.Text = string.Format("已從 {0} 匯入到「{1}」{2}", Path.GetFileName(dlg.Selected.Path), target.Name,
                    skipped > 0 ? string.Format("（{0} 個超過 {1} 格未匯入）", skipped, SlotCount) : "");
                SaveSettings();
            }
        }

        private void RecalcTimes()
        {
            if (_db == null)
            {
                MessageBox.Show(this, "尚未載入技能表，請先按「技能表…」選擇 技能表.csv。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            EditorToProfile(_current);
            int ok = 0, total = 0;
            foreach (var d in _current.Slots)
            {
                if (string.IsNullOrEmpty(d.SkillName)) continue;
                total++;
                if (GameShortcuts.ApplySuggestion(d, _db)) ok++;
            }
            ProfileToEditor(_current);
            _store.Save(_current);
            _lblStatus.Text = total == 0
                ? "這個角色設定沒有從遊戲匯入的技能，無法自動計算"
                : string.Format("已重算 {0} 個技能的時間{1}", ok, total > ok ? string.Format("，{0} 個需要手動確認", total - ok) : "");
        }

        private void ChooseSkillDb()
        {
            using (var dlg = new OpenFileDialog { Filter = "技能表 (*.csv)|*.csv|所有檔案 (*.*)|*.*", Title = "選擇 技能表.csv" })
            {
                if (File.Exists(_skillDbPath)) dlg.FileName = _skillDbPath;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _skillDbPath = dlg.FileName;
                LoadSkillDb(true);
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

            _store.Load();
            MigrateOldSlots(s);
            _current = _store.Find(s.Get("profile", ProfileStore.DefaultName)) ?? _store.Find(ProfileStore.DefaultName);
            ProfileToEditor(_current);
            RefreshProfileCombo(_current);
            _btnDelete.Enabled = _btnRename.Enabled = _current.Name != ProfileStore.DefaultName;
            LoadSkillDb(false);
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

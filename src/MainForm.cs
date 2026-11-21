using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace XiaoYaOpt
{
    internal sealed class MainForm : Form
    {
        private const int SlotCount = 6;
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
            public override string ToString()
            {
                return string.Format("{0}    [PID {1}, 0x{2:X8}]", Title, Pid, Hwnd.ToInt64());
            }
        }

        private readonly TextBox _txtKeyword = new TextBox();
        private readonly Button _btnRefresh = new Button();
        private readonly CheckedListBox _lstWindows = new CheckedListBox();
        private readonly CheckBox[] _chkEnabled = new CheckBox[SlotCount];
        private readonly ComboBox[] _cboKey = new ComboBox[SlotCount];
        private readonly NumericUpDown[] _numInterval = new NumericUpDown[SlotCount];
        private readonly CheckBox[] _chkMouse = new CheckBox[SlotCount];
        private readonly NumericUpDown[] _numDelay = new NumericUpDown[SlotCount];
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
        private readonly GroupBox _grpSlots = new GroupBox();
        private readonly GroupBox _grpOptions = new GroupBox();
        private readonly Timer _uiTimer = new Timer();
        private readonly Timer _captureTimer = new Timer();

        private readonly string _settingsPath;
        private Scheduler _scheduler;
        private int _activeWindows;

        public MainForm()
        {
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "小雅優化版.cfg");

            Text = "小雅 優化版";
            Font = new Font("Microsoft JhengHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(560, 650);
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

        private void BuildUi()
        {
            AddLabel(this, "視窗標題關鍵字", 12, 15);
            _txtKeyword.SetBounds(110, 12, 120, 23);
            Controls.Add(_txtKeyword);
            _btnRefresh.Text = "重新整理視窗";
            _btnRefresh.SetBounds(240, 10, 110, 27);
            _btnRefresh.Click += delegate { RefreshWindows(); };
            Controls.Add(_btnRefresh);
            AddLabel(this, "勾選要自動按鍵的遊戲視窗", 360, 15);

            _lstWindows.SetBounds(12, 44, 536, 112);
            _lstWindows.CheckOnClick = true;
            _lstWindows.IntegralHeight = false;
            Controls.Add(_lstWindows);

            _grpSlots.Text = "技能設定";
            _grpSlots.SetBounds(12, 164, 536, 222);
            Controls.Add(_grpSlots);
            AddLabel(_grpSlots, "啟用", 14, 24);
            AddLabel(_grpSlots, "按鍵", 70, 24);
            AddLabel(_grpSlots, "技能時間 (毫秒)", 160, 24);
            AddLabel(_grpSlots, "按後點滑鼠", 290, 24);
            AddLabel(_grpSlots, "延遲時間 (毫秒)", 390, 24);

            KeyItem[] keys = BuildKeyList();
            for (int i = 0; i < SlotCount; i++)
            {
                int y = 48 + i * 28;
                _chkEnabled[i] = new CheckBox { Text = (i + 1).ToString() };
                _chkEnabled[i].SetBounds(14, y, 50, 22);
                _cboKey[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                _cboKey[i].Items.AddRange(keys);
                _cboKey[i].SetBounds(70, y, 80, 23);
                _numInterval[i] = MakeNumber(50, 3600000, 1000);
                _numInterval[i].SetBounds(160, y, 110, 23);
                _chkMouse[i] = new CheckBox { Text = "點一下" };
                _chkMouse[i].SetBounds(290, y, 80, 22);
                _numDelay[i] = MakeNumber(0, 60000, 200);
                _numDelay[i].SetBounds(390, y, 110, 23);
                _grpSlots.Controls.AddRange(new Control[] { _chkEnabled[i], _cboKey[i], _numInterval[i], _chkMouse[i], _numDelay[i] });
            }

            _grpOptions.Text = "選項";
            _grpOptions.SetBounds(12, 394, 536, 200);
            Controls.Add(_grpOptions);
            // 兩組 RadioButton 需要各自的容器，才不會互相影響。
            var modePanel = new Panel();
            modePanel.SetBounds(8, 18, 520, 50);
            _rbBackground.Text = "背景送出（不搶焦點，同原版，多開建議）";
            _rbBackground.SetBounds(6, 2, 500, 22);
            _rbForeground.Text = "前景送出（切到視窗 + 模擬實體按鍵，相容性較高但會搶焦點）";
            _rbForeground.SetBounds(6, 26, 500, 22);
            modePanel.Controls.Add(_rbBackground);
            modePanel.Controls.Add(_rbForeground);
            _grpOptions.Controls.Add(modePanel);

            AddLabel(_grpOptions, "滑鼠按鍵", 14, 78);
            _cboButton.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboButton.Items.AddRange(new object[] { "左鍵", "右鍵" });
            _cboButton.SetBounds(80, 75, 60, 23);

            var clickPanel = new Panel();
            clickPanel.SetBounds(8, 102, 520, 52);
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
            _chkCtrlT.SetBounds(14, 164, 240, 22);
            AddLabel(_grpOptions, "多開時每個視窗錯開 (毫秒)", 262, 167);
            _numStagger.Maximum = 10000;
            _numStagger.SetBounds(436, 164, 80, 23);
            _grpOptions.Controls.AddRange(new Control[] { _cboButton, _chkCtrlT, _numStagger });

            _btnStart.Text = "開始（Ctrl+F10）";
            _btnStart.SetBounds(12, 604, 130, 32);
            _btnStart.Click += delegate { StartRun(); };
            _btnStop.Text = "停止";
            _btnStop.SetBounds(150, 604, 80, 32);
            _btnStop.Click += delegate { StopRun("已停止"); };
            _btnSave.Text = "儲存設定";
            _btnSave.SetBounds(238, 604, 90, 32);
            _btnSave.Click += delegate { SaveSettings(); _lblStatus.Text = "設定已儲存"; };
            _lblStatus.SetBounds(338, 606, 214, 40);
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
            var label = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
            parent.Controls.Add(label);
        }

        // ---------- 視窗列表 ----------

        private void RefreshWindows()
        {
            var keep = new HashSet<IntPtr>();
            foreach (object o in _lstWindows.CheckedItems) keep.Add(((WindowItem)o).Hwnd);

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

            _lstWindows.BeginUpdate();
            _lstWindows.Items.Clear();
            foreach (WindowItem w in found) _lstWindows.Items.Add(w, keep.Contains(w.Hwnd));
            _lstWindows.EndUpdate();
            _lblStatus.Text = string.Format("找到 {0} 個視窗", found.Count);
        }

        // ---------- 開始 / 停止 ----------

        private void StartRun()
        {
            if (_scheduler != null) return;

            var windows = new List<IntPtr>();
            foreach (object o in _lstWindows.CheckedItems) windows.Add(((WindowItem)o).Hwnd);
            if (windows.Count == 0)
            {
                MessageBox.Show(this, "請先勾選至少一個遊戲視窗。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            RunConfig cfg = ReadConfig();
            bool anySlot = false;
            foreach (SlotConfig s in cfg.Slots) anySlot |= s.Enabled;
            if (!anySlot)
            {
                MessageBox.Show(this, "請至少啟用一個技能按鍵。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _activeWindows = windows.Count;
            _scheduler = new Scheduler(cfg, windows);
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
            _lstWindows.Enabled = !running;
            _grpSlots.Enabled = !running;
            _grpOptions.Enabled = !running;
            _txtKeyword.Enabled = !running;
            if (running) _uiTimer.Start(); else _uiTimer.Stop();
        }

        private void UpdateStatus()
        {
            if (_scheduler == null) return;
            _lblStatus.Text = string.Format("執行中：{0} 個視窗\n已送出 {1} 次按鍵", _activeWindows, _scheduler.SentCount);
        }

        private RunConfig ReadConfig()
        {
            var cfg = new RunConfig
            {
                Slots = new SlotConfig[SlotCount],
                Mode = _rbForeground.Checked ? SendMode.Foreground : SendMode.Background,
                RightButton = _cboButton.SelectedIndex == 1,
                ClickCenter = !_rbCustom.Checked,
                ClickX = (int)_numX.Value,
                ClickY = (int)_numY.Value,
                StaggerMs = (int)_numStagger.Value,
                SendCtrlT = _chkCtrlT.Checked
            };
            for (int i = 0; i < SlotCount; i++)
            {
                var key = (KeyItem)_cboKey[i].SelectedItem;
                cfg.Slots[i] = new SlotConfig
                {
                    Enabled = _chkEnabled[i].Checked && key != null,
                    VirtualKey = key != null ? key.Vk : 0,
                    IntervalMs = (int)_numInterval[i].Value,
                    Mouse = _chkMouse[i].Checked,
                    DelayMs = (int)_numDelay[i].Value
                };
            }
            return cfg;
        }

        // ---------- 擷取座標 ----------

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

        // ---------- 設定檔 ----------

        private void LoadSettings()
        {
            var s = new Settings(_settingsPath);
            _txtKeyword.Text = s.Get("keyword", "eco");
            for (int i = 0; i < SlotCount; i++)
            {
                string n = (i + 1).ToString();
                _chkEnabled[i].Checked = s.GetBool("slot" + n + ".enabled", false);
                SelectKey(_cboKey[i], s.Get("slot" + n + ".key", "F" + n));
                _numInterval[i].Value = Clamp(_numInterval[i], s.GetInt("slot" + n + ".interval", 1000));
                _chkMouse[i].Checked = s.GetBool("slot" + n + ".mouse", false);
                _numDelay[i].Value = Clamp(_numDelay[i], s.GetInt("slot" + n + ".delay", 200));
            }
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
            UpdateClickControls();
        }

        private void UpdateClickControls()
        {
            _numX.Enabled = _rbCustom.Checked;
            _numY.Enabled = _rbCustom.Checked;
        }

        private void SaveSettings()
        {
            var s = new Settings(_settingsPath);
            s.Set("keyword", _txtKeyword.Text.Trim());
            for (int i = 0; i < SlotCount; i++)
            {
                string n = (i + 1).ToString();
                s.Set("slot" + n + ".enabled", _chkEnabled[i].Checked);
                s.Set("slot" + n + ".key", _cboKey[i].SelectedItem != null ? _cboKey[i].SelectedItem.ToString() : "");
                s.Set("slot" + n + ".interval", (int)_numInterval[i].Value);
                s.Set("slot" + n + ".mouse", _chkMouse[i].Checked);
                s.Set("slot" + n + ".delay", (int)_numDelay[i].Value);
            }
            s.Set("mode", _rbForeground.Checked ? 1 : 0);
            s.Set("mouse.right", _cboButton.SelectedIndex == 1);
            s.Set("mouse.center", !_rbCustom.Checked);
            s.Set("ctrlT", _chkCtrlT.Checked);
            s.Set("mouse.x", (int)_numX.Value);
            s.Set("mouse.y", (int)_numY.Value);
            s.Set("stagger", (int)_numStagger.Value);
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

        // ---------- 全域快捷鍵 ----------

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

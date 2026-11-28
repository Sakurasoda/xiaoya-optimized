using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace XiaoYaOpt
{
    internal enum SendMode
    {
        /// <summary>SendMessage／PostMessage 直接送到遊戲視窗，不搶焦點（同原版）。</summary>
        Background = 0,
        /// <summary>先把遊戲視窗切到前景，再用 SendInput 模擬實體按鍵。</summary>
        Foreground = 1
    }

    internal sealed class SlotConfig
    {
        public bool Enabled;
        public int VirtualKey;
        /// <summary>同一個技能兩次施放的間隔（原版「技能時間」）。</summary>
        public int IntervalMs;
        /// <summary>按完技能後是否點一下滑鼠左鍵（原版「鼠標」）。</summary>
        public bool Mouse;
        /// <summary>按完技能（和點擊）後的等待時間，期間這個視窗的其他技能會順延（原版「延遲時間」）。</summary>
        public int DelayMs;
    }

    internal sealed class RunConfig
    {
        public SendMode Mode;
        public bool RightButton;
        public int StaggerMs;
        /// <summary>ECO 的 Ctrl+T 是切換拍照模式（隱藏 UI）。開始時送一次進入、停止時再送一次離開（同原版）。</summary>
        public bool SendCtrlT;
        /// <summary>已經用「拍照模式」按鈕手動切進拍照模式的視窗：開始時不再送、停止時也不切回。</summary>
        public HashSet<IntPtr> AlreadyInPhotoMode = new HashSet<IntPtr>();
        /// <summary>
        /// 背景點擊時先把實體游標移到點擊位置、點完再移回（游標會瞬間跳動）。
        /// false = 同原版，只送滑鼠訊息、不動游標。
        /// </summary>
        public bool MoveCursorForClick;
    }

    /// <summary>
    /// 單一執行緒的排程器。
    /// 原版用忙碌迴圈掃描視窗代碼，這裡改成「睡到下一個工作到期才醒來」，
    /// 沒有工作到期時執行緒完全不佔 CPU。
    ///
    /// 單次施放的流程照原版 FUN_00402f45：
    ///   按下 → 30ms → 放開 → （點滑鼠：按下 → 30ms → 放開）→ 等「延遲時間」
    /// </summary>
    internal sealed class Scheduler
    {
        private enum JobKind { KeyDown, KeyUp, MouseDown, MouseUp }

        private sealed class Job
        {
            public long Due;
            public long Seq;
            public IntPtr Hwnd;
            public int Slot;
            public JobKind Kind;
        }

        private const int PressMs = 30;
        private const int MinIntervalMs = 50;
        /// <summary>同步送按鍵時最多等遊戲處理多久（某個遊戲卡住時不會拖住其他視窗）。</summary>
        private const uint KeySendTimeoutMs = 500;
        /// <summary>放開技能鍵後等多久才點滑鼠（原版 0）：讓遊戲先進入選目標狀態，點擊太早到會被忽略。</summary>
        private const int ClickGapMs = 150;
        /// <summary>背景點擊（只送訊息）時滑鼠按住的時間（原版 30）：按下和放開落在同一幀時遊戲可能認不到。</summary>
        private const int BgClickHoldMs = 100;
        /// <summary>背景點擊時滑鼠按住的時間（要讓遊戲至少跑過一幀才會認到）。</summary>
        private const int ClickHoldMs = 100;
        /// <summary>放開後游標再停留的時間，讓遊戲處理完放開才把游標移回去。</summary>
        private const int ClickSettleMs = 60;
        /// <summary>判斷「游標還在我們放的位置」時容許的誤差（像素）。</summary>
        private const int CursorSlackPx = 8;

        private readonly RunConfig _cfg;
        private readonly List<IntPtr> _windows;
        /// <summary>每個視窗各自的技能設定（來自各自的角色設定）。</summary>
        private readonly Dictionary<IntPtr, SlotConfig[]> _slots;
        private readonly List<Job> _jobs = new List<Job>();
        private readonly Dictionary<IntPtr, long> _busyUntil = new Dictionary<IntPtr, long>();
        /// <summary>已送過 Ctrl+T、目前處於拍照模式的視窗；停止時要再送一次切回來。</summary>
        private readonly List<IntPtr> _photoMode = new List<IntPtr>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Stopwatch _clock = new Stopwatch();
        private Thread _thread;
        private volatile bool _stop;
        private long _sent;
        private long _seq;

        /// <summary>某個視窗被關閉、已從排程移除時觸發（在排程執行緒上呼叫）。</summary>
        public event Action<IntPtr> WindowLost;

        /// <summary>所有視窗都關閉、排程自行結束時觸發（在排程執行緒上呼叫）。</summary>
        public event Action Finished;

        /// <param name="windows">視窗與它的技能設定；順序決定多開時的錯開順序。</param>
        public Scheduler(RunConfig cfg, IList<KeyValuePair<IntPtr, SlotConfig[]>> windows)
        {
            _cfg = cfg;
            _windows = new List<IntPtr>();
            _slots = new Dictionary<IntPtr, SlotConfig[]>();
            foreach (var kv in windows)
            {
                if (_slots.ContainsKey(kv.Key)) continue;
                _windows.Add(kv.Key);
                _slots[kv.Key] = kv.Value;
            }
        }

        public long SentCount { get { return Interlocked.Read(ref _sent); } }

        public void Start()
        {
            _clock.Start();
            for (int w = 0; w < _windows.Count; w++)
            {
                _busyUntil[_windows[w]] = 0;
                SlotConfig[] slots = _slots[_windows[w]];
                for (int s = 0; s < slots.Length; s++)
                {
                    if (!slots[s].Enabled) continue;
                    // 每個視窗錯開一點時間，避免同一瞬間全部擠在一起送出。
                    Add((long)w * _cfg.StaggerMs, _windows[w], s, JobKind.KeyDown);
                }
            }
            _thread = new Thread(Run) { IsBackground = true, Name = "XiaoYaScheduler" };
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            _wake.Set();
            if (_thread != null && Thread.CurrentThread != _thread)
            {
                // 停止時要對每個視窗補送 Ctrl+T（每個約 150ms），多留一點時間。
                _thread.Join(2000 + 200 * _windows.Count);
            }
        }

        private void Run()
        {
            if (_cfg.SendCtrlT)
            {
                // 進入拍照模式（隱藏遊戲 UI），之後點視窗中央才不會點到介面。
                foreach (IntPtr hwnd in _windows)
                {
                    if (_stop) break;
                    if (!Native.IsWindow(hwnd) || _cfg.AlreadyInPhotoMode.Contains(hwnd)) continue;
                    SendCtrlT(hwnd);
                    _photoMode.Add(hwnd);
                }
            }
            _clock.Restart();

            bool finishedNaturally = false;
            while (!_stop)
            {
                if (_jobs.Count == 0)
                {
                    finishedNaturally = true;
                    break;
                }

                int idx = 0;
                for (int i = 1; i < _jobs.Count; i++)
                {
                    Job a = _jobs[i], b = _jobs[idx];
                    if (a.Due < b.Due || (a.Due == b.Due && a.Seq < b.Seq)) idx = i;
                }

                long now = _clock.ElapsedMilliseconds;
                long wait = _jobs[idx].Due - now;
                if (wait > 0)
                {
                    _wake.WaitOne((int)Math.Min(wait, 1000));
                    continue;
                }

                Job job = _jobs[idx];
                _jobs.RemoveAt(idx);
                Execute(job, now);
            }

            // 停止時把還沒送出的「放開」補送完，避免遊戲以為某個鍵或滑鼠一直被按住。
            foreach (Job pending in _jobs)
            {
                if (pending.Kind != JobKind.KeyUp && pending.Kind != JobKind.MouseUp) continue;
                if (!Native.IsWindow(pending.Hwnd)) continue;
                if (pending.Kind == JobKind.KeyUp)
                    SendKeyMessage(pending.Hwnd, _slots[pending.Hwnd][pending.Slot].VirtualKey, true);
                else
                    PostMouse(pending.Hwnd, true);
            }
            _jobs.Clear();

            // 離開拍照模式，把遊戲 UI 恢復（同原版停止時再送一次 Ctrl+T）。
            foreach (IntPtr hwnd in _photoMode)
            {
                if (Native.IsWindow(hwnd)) SendCtrlT(hwnd);
            }
            _photoMode.Clear();

            if (finishedNaturally)
            {
                var handler = Finished;
                if (handler != null) handler();
            }
        }

        private void Execute(Job job, long now)
        {
            if (!Native.IsWindow(job.Hwnd))
            {
                _jobs.RemoveAll(j => j.Hwnd == job.Hwnd);
                _busyUntil.Remove(job.Hwnd);
                _photoMode.Remove(job.Hwnd);
                var lost = WindowLost;
                if (lost != null) lost(job.Hwnd);
                return;
            }

            SlotConfig slot = _slots[job.Hwnd][job.Slot];
            switch (job.Kind)
            {
                case JobKind.KeyDown:
                    {
                        // 這個視窗還在上一個技能的「延遲時間」內：順延到結束後再施放。
                        long busy = _busyUntil[job.Hwnd];
                        if (now < busy)
                        {
                            job.Due = busy;
                            job.Seq = ++_seq;
                            _jobs.Add(job);
                            return;
                        }

                        if (_cfg.Mode == SendMode.Background)
                        {
                            SendKeyMessage(job.Hwnd, slot.VirtualKey, false);
                            now = _clock.ElapsedMilliseconds; // 從遊戲處理完按下起算（同原版）
                            Add(now + PressMs, job.Hwnd, job.Slot, JobKind.KeyUp);
                            if (slot.Mouse) Add(now + PressMs + ClickGapMs, job.Hwnd, job.Slot, JobKind.MouseDown);
                        }
                        else
                        {
                            ActivateWindow(job.Hwnd);
                            SendKeyInput(slot.VirtualKey, false);
                            Thread.Sleep(PressMs);
                            SendKeyInput(slot.VirtualKey, true);
                            if (slot.Mouse) ClickForeground(job.Hwnd);
                            now = _clock.ElapsedMilliseconds;
                        }
                        Interlocked.Increment(ref _sent);

                        long pressEnd = now + (_cfg.Mode == SendMode.Background
                            ? PressMs + (!slot.Mouse ? 0 : ClickGapMs + (_cfg.MoveCursorForClick ? ClickHoldMs + ClickSettleMs : BgClickHoldMs))
                            : 0);
                        _busyUntil[job.Hwnd] = pressEnd + Math.Max(0, slot.DelayMs);

                        // 以排定時間為基準計算下一次，避免誤差累積；落後太多就從現在重新起算。
                        int interval = Math.Max(MinIntervalMs, slot.IntervalMs);
                        long next = job.Due + interval;
                        if (next <= now) next = now + interval;
                        Add(next, job.Hwnd, job.Slot, JobKind.KeyDown);
                        break;
                    }

                case JobKind.KeyUp:
                    SendKeyMessage(job.Hwnd, slot.VirtualKey, true);
                    break;

                case JobKind.MouseDown:
                    if (_cfg.MoveCursorForClick)
                    {
                        ClickWithCursor(job.Hwnd);
                    }
                    else
                    {
                        // 同原版 FUN_00403712：移動 → 按下，按住後再 移動 → 放開，不動實體游標。
                        PostMouse(job.Hwnd, false);
                        Add(now + BgClickHoldMs, job.Hwnd, job.Slot, JobKind.MouseUp);
                    }
                    break;

                case JobKind.MouseUp:
                    PostMouse(job.Hwnd, true);
                    break;
            }
        }

        private void Add(long due, IntPtr hwnd, int slot, JobKind kind)
        {
            _jobs.Add(new Job { Due = due, Seq = ++_seq, Hwnd = hwnd, Slot = slot, Kind = kind });
        }

        // ---------- 背景送出 ----------

        private static IntPtr KeyLParam(int vk, bool up)
        {
            uint scan = Native.MapVirtualKeyW((uint)vk, 0);
            long lParam = 1 | ((long)scan << 16);
            if (up) lParam |= 0xC0000000L;
            return new IntPtr(unchecked((int)lParam));
        }

        /// <summary>
        /// 同原版用 SendMessage 送技能鍵：等遊戲處理完這個按鍵才回來，點擊一定落在遊戲進入選目標狀態之後。
        /// 加上逾時：某個遊戲卡住時最多等 KeySendTimeoutMs，不會拖住其他視窗。
        /// </summary>
        private static void SendKeyMessage(IntPtr hwnd, int vk, bool up)
        {
            IntPtr result;
            Native.SendMessageTimeoutW(hwnd, up ? Native.WM_KEYUP : Native.WM_KEYDOWN,
                new IntPtr(vk), KeyLParam(vk, up), Native.SMTO_ABORTIFHUNG, KeySendTimeoutMs, out result);
        }

        /// <summary>點擊位置：遊戲視窗正中央（同原版）。</summary>
        private static Native.POINT ClickPoint(IntPtr hwnd)
        {
            Native.RECT rc;
            Native.GetClientRect(hwnd, out rc);
            return new Native.POINT { X = rc.Right / 2, Y = rc.Bottom / 2 };
        }

        /// <summary>
        /// 背景點擊。eco.exe 每一幀用 GetCursorPos 讀實體游標（FUN_0054e380）：
        /// 實體游標在遊戲視窗範圍內時，才採用點擊訊息帶來的座標；不在範圍內就改用實體游標的位置，
        /// 背景送出的點擊等於點到視窗外面。所以點擊前先把游標移到目標位置，送完再移回原處。
        /// 遊戲只比對座標，視窗被其他視窗蓋住也沒關係；按住一段時間，讓遊戲至少跑過一幀。
        /// </summary>
        private void ClickWithCursor(IntPtr hwnd)
        {
            Native.POINT target = ClickPoint(hwnd);
            Native.ClientToScreen(hwnd, ref target);
            Native.POINT saved;
            bool haveSaved = Native.GetCursorPos(out saved);

            Native.SetCursorPos(target.X, target.Y);
            PostMouse(hwnd, false);
            Thread.Sleep(ClickHoldMs);
            PostMouse(hwnd, true);
            Thread.Sleep(ClickSettleMs);

            // 使用者在這段時間內自己移動了滑鼠，就不要把游標拉回去。
            // 螢幕有縮放時，讀回的游標座標會和設定值差 1～2 像素，所以容許一點誤差。
            Native.POINT now;
            if (haveSaved && Native.GetCursorPos(out now)
                && Math.Abs(now.X - target.X) <= CursorSlackPx && Math.Abs(now.Y - target.Y) <= CursorSlackPx)
                Native.SetCursorPos(saved.X, saved.Y);
        }

        private void PostMouse(IntPtr hwnd, bool up)
        {
            Native.POINT p = ClickPoint(hwnd);
            IntPtr pos = Native.MakeLParam(p.X, p.Y);
            if (!up)
            {
                Native.PostMessageW(hwnd, Native.WM_MOUSEMOVE, IntPtr.Zero, pos);
                Native.PostMessageW(hwnd, _cfg.RightButton ? Native.WM_RBUTTONDOWN : Native.WM_LBUTTONDOWN,
                    new IntPtr(_cfg.RightButton ? Native.MK_RBUTTON : Native.MK_LBUTTON), pos);
            }
            else
            {
                Native.PostMessageW(hwnd, Native.WM_MOUSEMOVE, IntPtr.Zero, pos); // 原版放開前也會先送一次移動
                Native.PostMessageW(hwnd, _cfg.RightButton ? Native.WM_RBUTTONUP : Native.WM_LBUTTONUP,
                    IntPtr.Zero, pos);
            }
        }

        /// <summary>
        /// 同原版 FUN_004011c5：Ctrl 用實體按鍵模擬（背景訊息無法讓遊戲認定 Ctrl 被按住），
        /// T 用 PostMessage 送進遊戲視窗。
        /// </summary>
        internal static void SendCtrlT(IntPtr hwnd)
        {
            const int VK_T = 0x54;
            SendKeyInput(Native.VK_CONTROL, false);
            Thread.Sleep(50);
            Native.PostMessageW(hwnd, Native.WM_KEYDOWN, new IntPtr(VK_T), KeyLParam(VK_T, false));
            Thread.Sleep(50);
            Native.PostMessageW(hwnd, Native.WM_KEYUP, new IntPtr(VK_T), KeyLParam(VK_T, true));
            SendKeyInput(Native.VK_CONTROL, true);
            Thread.Sleep(50);
        }

        // ---------- 前景送出 ----------

        private static void SendKeyInput(int vk, bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.u.ki.wVk = 0;
            input.u.ki.wScan = (ushort)Native.MapVirtualKeyW((uint)vk, 0);
            input.u.ki.dwFlags = Native.KEYEVENTF_SCANCODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        private void ClickForeground(IntPtr hwnd)
        {
            Native.POINT p = ClickPoint(hwnd);
            Native.ClientToScreen(hwnd, ref p);
            Native.SetCursorPos(p.X, p.Y);
            SendMouseInput(false);
            Thread.Sleep(PressMs);
            SendMouseInput(true);
        }

        private void SendMouseInput(bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_MOUSE };
            if (_cfg.RightButton)
                input.u.mi.dwFlags = up ? Native.MOUSEEVENTF_RIGHTUP : Native.MOUSEEVENTF_RIGHTDOWN;
            else
                input.u.mi.dwFlags = up ? Native.MOUSEEVENTF_LEFTUP : Native.MOUSEEVENTF_LEFTDOWN;
            Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        private static void ActivateWindow(IntPtr hwnd)
        {
            if (Native.GetForegroundWindow() == hwnd) return;
            if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);

            uint pid;
            uint me = Native.GetCurrentThreadId();
            uint fg = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            bool attached = fg != 0 && fg != me && Native.AttachThreadInput(me, fg, true);
            Native.SetForegroundWindow(hwnd);
            if (attached) Native.AttachThreadInput(me, fg, false);
            Thread.Sleep(50); // 給遊戲一點時間接收焦點
        }
    }
}

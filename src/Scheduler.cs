using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace XiaoYaOpt
{
    internal enum SendMode
    {
        /// <summary>PostMessage 直接送到遊戲視窗，不搶焦點。多開時建議使用。</summary>
        Background = 0,
        /// <summary>先把遊戲視窗切到前景，再用 SendInput 模擬實體按鍵。</summary>
        Foreground = 1
    }

    internal sealed class SlotConfig
    {
        public bool Enabled;
        public int VirtualKey;
        public int IntervalMs;
        public bool Mouse;
        public int MouseDelayMs;
    }

    internal sealed class RunConfig
    {
        public SlotConfig[] Slots;
        public SendMode Mode;
        public bool RightButton;
        public int ClickX;
        public int ClickY;
        public int StaggerMs;
    }

    /// <summary>
    /// 單一執行緒的排程器。
    /// 原版用忙碌迴圈掃描視窗代碼，這裡改成「睡到下一個工作到期才醒來」，
    /// 沒有工作到期時執行緒完全不佔 CPU。
    /// </summary>
    internal sealed class Scheduler
    {
        private enum JobKind { KeyDown, KeyUp, MouseDown, MouseUp }

        private sealed class Job
        {
            public long Due;
            public IntPtr Hwnd;
            public int Slot;
            public JobKind Kind;
        }

        private const int PressMs = 30;
        private const int MinIntervalMs = 50;

        private readonly RunConfig _cfg;
        private readonly List<IntPtr> _windows;
        private readonly List<Job> _jobs = new List<Job>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Stopwatch _clock = new Stopwatch();
        private Thread _thread;
        private volatile bool _stop;
        private long _sent;

        /// <summary>某個視窗被關閉、已從排程移除時觸發（在排程執行緒上呼叫）。</summary>
        public event Action<IntPtr> WindowLost;

        /// <summary>所有視窗都關閉、排程自行結束時觸發（在排程執行緒上呼叫）。</summary>
        public event Action Finished;

        public Scheduler(RunConfig cfg, IEnumerable<IntPtr> windows)
        {
            _cfg = cfg;
            _windows = new List<IntPtr>(windows);
        }

        public long SentCount { get { return Interlocked.Read(ref _sent); } }

        public void Start()
        {
            _clock.Start();
            for (int w = 0; w < _windows.Count; w++)
            {
                for (int s = 0; s < _cfg.Slots.Length; s++)
                {
                    if (!_cfg.Slots[s].Enabled) continue;
                    // 每個視窗、每個技能錯開一點時間，避免同一瞬間全部擠在一起送出。
                    long first = (long)w * _cfg.StaggerMs + s * 20L;
                    _jobs.Add(new Job { Due = first, Hwnd = _windows[w], Slot = s, Kind = JobKind.KeyDown });
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
                _thread.Join(2000);
            }
        }

        private void Run()
        {
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
                    if (_jobs[i].Due < _jobs[idx].Due) idx = i;
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
                var lost = WindowLost;
                if (lost != null) lost(job.Hwnd);
                return;
            }

            SlotConfig slot = _cfg.Slots[job.Slot];
            switch (job.Kind)
            {
                case JobKind.KeyDown:
                    if (_cfg.Mode == SendMode.Background)
                    {
                        PostKey(job.Hwnd, slot.VirtualKey, false);
                        Add(now + PressMs, job.Hwnd, job.Slot, JobKind.KeyUp);
                    }
                    else
                    {
                        ActivateWindow(job.Hwnd);
                        SendKey(slot.VirtualKey, false);
                        Thread.Sleep(PressMs);
                        SendKey(slot.VirtualKey, true);
                    }
                    Interlocked.Increment(ref _sent);

                    if (slot.Mouse)
                    {
                        Add(now + Math.Max(PressMs + 10, slot.MouseDelayMs), job.Hwnd, job.Slot, JobKind.MouseDown);
                    }

                    // 以排定時間為基準計算下一次，避免誤差累積；落後太多就從現在重新起算。
                    int interval = Math.Max(MinIntervalMs, slot.IntervalMs);
                    long next = job.Due + interval;
                    if (next <= now) next = now + interval;
                    Add(next, job.Hwnd, job.Slot, JobKind.KeyDown);
                    break;

                case JobKind.KeyUp:
                    PostKey(job.Hwnd, slot.VirtualKey, true);
                    break;

                case JobKind.MouseDown:
                    if (_cfg.Mode == SendMode.Background)
                    {
                        PostMouse(job.Hwnd, false);
                        Add(now + PressMs, job.Hwnd, job.Slot, JobKind.MouseUp);
                    }
                    else
                    {
                        ActivateWindow(job.Hwnd);
                        Native.POINT p = new Native.POINT { X = _cfg.ClickX, Y = _cfg.ClickY };
                        Native.ClientToScreen(job.Hwnd, ref p);
                        Native.SetCursorPos(p.X, p.Y);
                        SendMouse(false);
                        Thread.Sleep(PressMs);
                        SendMouse(true);
                    }
                    break;

                case JobKind.MouseUp:
                    PostMouse(job.Hwnd, true);
                    break;
            }
        }

        private void Add(long due, IntPtr hwnd, int slot, JobKind kind)
        {
            _jobs.Add(new Job { Due = due, Hwnd = hwnd, Slot = slot, Kind = kind });
        }

        private static void PostKey(IntPtr hwnd, int vk, bool up)
        {
            uint scan = Native.MapVirtualKeyW((uint)vk, 0);
            long lParam = 1 | ((long)scan << 16);
            if (up) lParam |= 0xC0000000L;
            Native.PostMessageW(hwnd, up ? Native.WM_KEYUP : Native.WM_KEYDOWN,
                new IntPtr(vk), new IntPtr(unchecked((int)lParam)));
        }

        private void PostMouse(IntPtr hwnd, bool up)
        {
            IntPtr pos = Native.MakeLParam(_cfg.ClickX, _cfg.ClickY);
            if (!up)
            {
                Native.PostMessageW(hwnd, Native.WM_MOUSEMOVE, IntPtr.Zero, pos);
                Native.PostMessageW(hwnd, _cfg.RightButton ? Native.WM_RBUTTONDOWN : Native.WM_LBUTTONDOWN,
                    new IntPtr(_cfg.RightButton ? Native.MK_RBUTTON : Native.MK_LBUTTON), pos);
            }
            else
            {
                Native.PostMessageW(hwnd, _cfg.RightButton ? Native.WM_RBUTTONUP : Native.WM_LBUTTONUP,
                    IntPtr.Zero, pos);
            }
        }

        private static void SendKey(int vk, bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.u.ki.wVk = 0;
            input.u.ki.wScan = (ushort)Native.MapVirtualKeyW((uint)vk, 0);
            input.u.ki.dwFlags = Native.KEYEVENTF_SCANCODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            Native.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
        }

        private void SendMouse(bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_MOUSE };
            if (_cfg.RightButton)
                input.u.mi.dwFlags = up ? Native.MOUSEEVENTF_RIGHTUP : Native.MOUSEEVENTF_RIGHTDOWN;
            else
                input.u.mi.dwFlags = up ? Native.MOUSEEVENTF_LEFTUP : Native.MOUSEEVENTF_LEFTDOWN;
            Native.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
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

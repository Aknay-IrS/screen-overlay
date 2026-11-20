// OverlayLock - a password-protected screen overlay for Windows.
//
// Purpose: hide the screen and block input WITHOUT locking Windows, so long-running
// processes, UI automation and browser sessions keep running. Optionally keeps the PC
// awake while the overlay is up.
//
// Lock: left-click the tray icon, press Ctrl+Alt+L, run "OverlayLock.exe /lock",
// or stay idle for N minutes. Unlock: type the password and press Enter.
//
// Written for C# 5 so it compiles with the csc.exe built into Windows (.NET Framework 4.x).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("OverlayLock")]
[assembly: System.Reflection.AssemblyProduct("OverlayLock")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

namespace OverlayLock
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        public const string LockEventName = @"Local\OverlayLock_LockNow_Event_v1";
        const string MutexName = @"Local\OverlayLock_SingleInstance_v1";

        [STAThread]
        static void Main(string[] args)
        {
            bool lockArg = false;
            foreach (string a in args)
            {
                string x = a.Trim().ToLowerInvariant();
                if (x == "/lock" || x == "-lock" || x == "--lock") lockArg = true;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    if (lockArg)
                    {
                        try { using (EventWaitHandle ev = EventWaitHandle.OpenExisting(LockEventName)) ev.Set(); }
                        catch { }
                    }
                    else
                    {
                        MessageBox.Show("OverlayLock is already running.\n\nLook for the shield icon in the system tray.",
                            "OverlayLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp(lockArg));
                GC.KeepAlive(mutex);
            }
        }
    }

    // ------------------------------------------------------------------ Settings

    class Settings
    {
        public const int MinPasswordLength = 8;

        public string PasswordHash = "";
        public string PasswordSalt = "";
        public int PasswordIterations = 20000; // v1.0 files have no value -> 20000
        const int NewIterations = 100000;
        public int IdleMinutes = 5;
        public bool KeepAwake = true;
        public bool KeepDisplayOn = false;
        public bool DisableTaskManager = true;
        public int Opacity = 85;
        public Color BackColor = Color.FromArgb(12, 14, 22);
        public string BackgroundImage = "";
        public ImageLayout ImageLayout = ImageLayout.Zoom;
        public bool ShowClock = true;
        public bool Use24Hour = false;
        public bool ShowDate = true;
        public Color ClockColor = Color.White;
        public int ClockSize = 72;
        public string Message = "This computer is locked";

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "OverlayLock";

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverlayLock"); }
        }
        static string FilePath { get { return Path.Combine(Dir, "settings.cfg"); } }

        public bool HasPassword { get { return PasswordHash.Length > 0 && PasswordSalt.Length > 0; } }

        public void SetPassword(string pw)
        {
            byte[] salt = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider()) rng.GetBytes(salt);
            PasswordSalt = Convert.ToBase64String(salt);
            PasswordIterations = NewIterations;
            PasswordHash = Hash(pw, salt, PasswordIterations);
        }

        public bool Verify(string pw)
        {
            if (!HasPassword || pw == null) return false;
            try
            {
                string h = Hash(pw, Convert.FromBase64String(PasswordSalt), PasswordIterations);
                int diff = h.Length ^ PasswordHash.Length;
                for (int i = 0; i < h.Length && i < PasswordHash.Length; i++) diff |= h[i] ^ PasswordHash[i];
                return diff == 0;
            }
            catch { return false; }
        }

        static string Hash(string pw, byte[] salt, int iterations)
        {
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(pw), salt, iterations))
                return Convert.ToBase64String(kdf.GetBytes(32));
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string v = line.Substring(i + 1);
                    switch (k)
                    {
                        case "PasswordHash": s.PasswordHash = v.Trim(); break;
                        case "PasswordSalt": s.PasswordSalt = v.Trim(); break;
                        case "PasswordIterations": s.PasswordIterations = ParseInt(v, 1000, 10000000, 20000); break;
                        case "IdleMinutes": s.IdleMinutes = ParseInt(v, 0, 240, s.IdleMinutes); break;
                        case "KeepAwake": s.KeepAwake = v.Trim() == "1"; break;
                        case "KeepDisplayOn": s.KeepDisplayOn = v.Trim() == "1"; break;
                        case "DisableTaskManager": s.DisableTaskManager = v.Trim() == "1"; break;
                        case "Opacity": s.Opacity = ParseInt(v, 10, 100, s.Opacity); break;
                        case "BackColor": s.BackColor = ParseColor(v, s.BackColor); break;
                        case "BackgroundImage": s.BackgroundImage = v.Trim(); break;
                        case "ImageLayout":
                            try { s.ImageLayout = (ImageLayout)Enum.Parse(typeof(ImageLayout), v.Trim(), true); } catch { }
                            break;
                        case "ShowClock": s.ShowClock = v.Trim() == "1"; break;
                        case "Use24Hour": s.Use24Hour = v.Trim() == "1"; break;
                        case "ShowDate": s.ShowDate = v.Trim() == "1"; break;
                        case "ClockColor": s.ClockColor = ParseColor(v, s.ClockColor); break;
                        case "ClockSize": s.ClockSize = ParseInt(v, 20, 200, s.ClockSize); break;
                        case "Message": s.Message = v; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PasswordHash=" + PasswordHash);
            sb.AppendLine("PasswordSalt=" + PasswordSalt);
            sb.AppendLine("PasswordIterations=" + PasswordIterations);
            sb.AppendLine("IdleMinutes=" + IdleMinutes);
            sb.AppendLine("KeepAwake=" + B(KeepAwake));
            sb.AppendLine("KeepDisplayOn=" + B(KeepDisplayOn));
            sb.AppendLine("DisableTaskManager=" + B(DisableTaskManager));
            sb.AppendLine("Opacity=" + Opacity);
            sb.AppendLine("BackColor=" + BackColor.ToArgb());
            sb.AppendLine("BackgroundImage=" + BackgroundImage);
            sb.AppendLine("ImageLayout=" + ImageLayout);
            sb.AppendLine("ShowClock=" + B(ShowClock));
            sb.AppendLine("Use24Hour=" + B(Use24Hour));
            sb.AppendLine("ShowDate=" + B(ShowDate));
            sb.AppendLine("ClockColor=" + ClockColor.ToArgb());
            sb.AppendLine("ClockSize=" + ClockSize);
            sb.AppendLine("Message=" + (Message ?? "").Replace("\r", " ").Replace("\n", " "));
            // write to temp then replace, so a crash mid-write can't corrupt the password
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }

        static string B(bool b) { return b ? "1" : "0"; }

        static int ParseInt(string v, int min, int max, int def)
        {
            int n;
            if (!int.TryParse(v.Trim(), out n)) return def;
            return Math.Max(min, Math.Min(max, n));
        }

        static Color ParseColor(string v, Color def)
        {
            int n;
            if (!int.TryParse(v.Trim(), out n)) return def;
            return Color.FromArgb(255, Color.FromArgb(n));
        }

        public static bool GetStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue(RunName) != null) k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ Native helpers

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

        public static uint IdleMilliseconds()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref lii)) return 0;
            return unchecked((uint)Environment.TickCount - lii.dwTime);
        }

        public static void MakeTopmost(IntPtr h)
        {
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    // Live list of monitors (Screen.AllScreens can be cached/stale after a hot-plug).
    static class Monitors
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
        delegate bool EnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);

        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumProc cb, IntPtr data);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);

        public struct Info { public Rectangle Bounds; public bool Primary; }

        public static List<Info> Get()
        {
            List<Info> list = new List<Info>();
            try
            {
                EnumProc cb = delegate(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data)
                {
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        Info i = new Info();
                        i.Bounds = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
                        i.Primary = (mi.dwFlags & 1) != 0;
                        list.Add(i);
                    }
                    return true;
                };
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
                GC.KeepAlive(cb);
            }
            catch { }

            if (list.Count == 0)
            {
                foreach (Screen sc in Screen.AllScreens)
                {
                    Info i = new Info();
                    i.Bounds = sc.Bounds;
                    i.Primary = sc.Primary;
                    list.Add(i);
                }
            }
            return list;
        }

        public static string Signature(List<Info> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Info i in list)
                sb.Append(i.Bounds.X).Append(',').Append(i.Bounds.Y).Append(',')
                  .Append(i.Bounds.Width).Append(',').Append(i.Bounds.Height).Append(i.Primary ? "P" : "").Append('|');
            return sb.ToString();
        }
    }

    // Global hotkey (Ctrl+Alt+L)
    class HotkeyWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
        public event EventHandler Pressed;
        public readonly bool Registered;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
            Registered = RegisterHotKey(Handle, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.L);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && Pressed != null) Pressed(this, EventArgs.Empty);
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) { UnregisterHotKey(Handle, 1); DestroyHandle(); }
        }
    }

    // Low-level keyboard hook on its own thread (so a busy UI thread can never make Windows
    // silently drop it), re-installed every 10 seconds as a safety net.
    class KeyboardBlocker
    {
        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        const uint WM_QUIT = 0x0012, WM_APP_REHOOK = 0x8001;

        HookProc proc;
        IntPtr hook = IntPtr.Zero;
        Thread thread;
        uint threadId;
        System.Threading.Timer refresher;

        public void Start()
        {
            if (thread != null) return;
            ManualResetEvent ready = new ManualResetEvent(false);
            thread = new Thread(delegate()
            {
                threadId = GetCurrentThreadId();
                proc = Callback;
                MSG msg;
                PeekMessage(out msg, IntPtr.Zero, 0, 0, 0); // make sure this thread has a message queue
                Install();
                ready.Set();
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.message == WM_APP_REHOOK) { Uninstall(); Install(); }
                }
                Uninstall();
            });
            thread.IsBackground = true;
            thread.Name = "OverlayLock keyboard hook";
            thread.Start();
            ready.WaitOne(3000);
            ready.Close();
            refresher = new System.Threading.Timer(delegate(object st)
            {
                PostThreadMessage(threadId, WM_APP_REHOOK, IntPtr.Zero, IntPtr.Zero);
            }, null, 10000, 10000);
        }

        public void Stop()
        {
            if (thread == null) return;
            if (refresher != null) { refresher.Dispose(); refresher = null; }
            PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(2000);
            thread = null;
        }

        void Install()
        {
            IntPtr mod = IntPtr.Zero;
            try { mod = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName); } catch { }
            hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, mod, 0);
        }

        void Uninstall()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                Keys key = (Keys)k.vkCode;
                bool alt = (k.flags & 0x20) != 0;
                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;

                if (key == Keys.LWin || key == Keys.RWin || key == Keys.Apps) return (IntPtr)1;
                if (alt && (key == Keys.Tab || key == Keys.Escape || key == Keys.F4 || key == Keys.Space)) return (IntPtr)1;
                if (alt && (key == Keys.PrintScreen || key == Keys.NumLock)) return (IntPtr)1; // High Contrast / Mouse Keys shortcuts
                if (ctrl && key == Keys.Escape) return (IntPtr)1;   // Ctrl+Esc and Ctrl+Shift+Esc (Task Manager)
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }
    }

    // System changes made while locked (keep awake, accessibility shortcuts off, Task Manager off).
    // Original values are written to a state file so they can be restored even after a crash.
    class LockGuard
    {
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;

        [StructLayout(LayoutKind.Sequential)] struct FLAGS2 { public uint cbSize, dwFlags; }
        [StructLayout(LayoutKind.Sequential)]
        struct FILTERKEYS { public uint cbSize, dwFlags, iWaitMSec, iDelayMSec, iRepeatMSec, iBounceMSec; }

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo")]
        static extern bool SpiFlags(uint action, uint param, ref FLAGS2 v, uint ini);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo")]
        static extern bool SpiFilter(uint action, uint param, ref FILTERKEYS v, uint ini);

        const uint SPI_GETFILTERKEYS = 0x32, SPI_SETFILTERKEYS = 0x33;
        const uint SPI_GETTOGGLEKEYS = 0x34, SPI_SETTOGGLEKEYS = 0x35;
        const uint SPI_GETSTICKYKEYS = 0x3A, SPI_SETSTICKYKEYS = 0x3B;
        const uint HOTKEYACTIVE = 0x4;

        const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        const string TaskMgrValue = "DisableTaskMgr";

        public class Saved
        {
            public bool HaveAccess;
            public uint Sticky, Filter, Toggle;
            public bool TouchedTaskMgr;
            public bool HadTaskMgrValue;
            public int TaskMgrValueOld;
        }

        Saved saved;

        static string MarkerPath { get { return Path.Combine(Settings.Dir, "locked.state"); } }

        public void Apply(Settings s, Saved recovered, int fails)
        {
            saved = recovered ?? Capture(s);
            WriteMarker(saved, fails);

            uint es = ES_CONTINUOUS;
            if (s.KeepAwake) es |= ES_SYSTEM_REQUIRED;
            if (s.KeepDisplayOn) es |= ES_DISPLAY_REQUIRED;
            SetThreadExecutionState(es);

            if (saved.HaveAccess) SetHotkeyBits(false);
            if (saved.TouchedTaskMgr) SetTaskMgr(true);
        }

        public void UpdateFails(int fails)
        {
            if (saved != null) WriteMarker(saved, fails);
        }

        public void Restore()
        {
            SetThreadExecutionState(ES_CONTINUOUS);
            if (saved != null)
            {
                if (saved.HaveAccess) SetHotkeyBits(true);
                if (saved.TouchedTaskMgr) SetTaskMgr(false);
            }
            try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); } catch { }
        }

        // Used when the app starts and finds a state file but no password (can't re-lock).
        public static void RestoreFrom(Saved s)
        {
            LockGuard g = new LockGuard();
            g.saved = s;
            g.Restore();
        }

        static Saved Capture(Settings s)
        {
            Saved v = new Saved();
            FLAGS2 st = NewFlags(), tg = NewFlags();
            FILTERKEYS fk = NewFilter();
            v.HaveAccess = SpiFlags(SPI_GETSTICKYKEYS, st.cbSize, ref st, 0)
                        && SpiFlags(SPI_GETTOGGLEKEYS, tg.cbSize, ref tg, 0)
                        && SpiFilter(SPI_GETFILTERKEYS, fk.cbSize, ref fk, 0);
            v.Sticky = st.dwFlags; v.Toggle = tg.dwFlags; v.Filter = fk.dwFlags;

            v.TouchedTaskMgr = s.DisableTaskManager;
            if (v.TouchedTaskMgr)
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(PolicyKey))
                    {
                        object o = k == null ? null : k.GetValue(TaskMgrValue);
                        if (o is int) { v.HadTaskMgrValue = true; v.TaskMgrValueOld = (int)o; }
                    }
                }
                catch { v.TouchedTaskMgr = false; }
            }
            return v;
        }

        void SetHotkeyBits(bool restore)
        {
            FLAGS2 st = NewFlags(), tg = NewFlags();
            FILTERKEYS fk = NewFilter();
            if (SpiFlags(SPI_GETSTICKYKEYS, st.cbSize, ref st, 0))
            {
                st.dwFlags = restore ? (st.dwFlags & ~HOTKEYACTIVE) | (saved.Sticky & HOTKEYACTIVE) : st.dwFlags & ~HOTKEYACTIVE;
                SpiFlags(SPI_SETSTICKYKEYS, st.cbSize, ref st, 0);
            }
            if (SpiFlags(SPI_GETTOGGLEKEYS, tg.cbSize, ref tg, 0))
            {
                tg.dwFlags = restore ? (tg.dwFlags & ~HOTKEYACTIVE) | (saved.Toggle & HOTKEYACTIVE) : tg.dwFlags & ~HOTKEYACTIVE;
                SpiFlags(SPI_SETTOGGLEKEYS, tg.cbSize, ref tg, 0);
            }
            if (SpiFilter(SPI_GETFILTERKEYS, fk.cbSize, ref fk, 0))
            {
                fk.dwFlags = restore ? (fk.dwFlags & ~HOTKEYACTIVE) | (saved.Filter & HOTKEYACTIVE) : fk.dwFlags & ~HOTKEYACTIVE;
                SpiFilter(SPI_SETFILTERKEYS, fk.cbSize, ref fk, 0);
            }
        }

        void SetTaskMgr(bool disable)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(PolicyKey))
                {
                    if (disable) k.SetValue(TaskMgrValue, 1, RegistryValueKind.DWord);
                    else if (saved.HadTaskMgrValue) k.SetValue(TaskMgrValue, saved.TaskMgrValueOld, RegistryValueKind.DWord);
                    else k.DeleteValue(TaskMgrValue, false);
                }
            }
            catch { }
        }

        static FLAGS2 NewFlags() { FLAGS2 f = new FLAGS2(); f.cbSize = (uint)Marshal.SizeOf(typeof(FLAGS2)); return f; }
        static FILTERKEYS NewFilter() { FILTERKEYS f = new FILTERKEYS(); f.cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS)); return f; }

        static void WriteMarker(Saved v, int fails)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Fails=" + fails);
                sb.AppendLine("HaveAccess=" + (v.HaveAccess ? 1 : 0));
                sb.AppendLine("Sticky=" + v.Sticky);
                sb.AppendLine("Filter=" + v.Filter);
                sb.AppendLine("Toggle=" + v.Toggle);
                sb.AppendLine("TouchedTaskMgr=" + (v.TouchedTaskMgr ? 1 : 0));
                sb.AppendLine("HadTaskMgrValue=" + (v.HadTaskMgrValue ? 1 : 0));
                sb.AppendLine("TaskMgrValueOld=" + v.TaskMgrValueOld);
                File.WriteAllText(MarkerPath, sb.ToString());
            }
            catch { }
        }

        public static bool ReadMarker(out Saved v, out int fails)
        {
            v = null; fails = 0;
            try
            {
                if (!File.Exists(MarkerPath)) return false;
                Dictionary<string, string> d = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(MarkerPath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1).Trim();
                }
                v = new Saved();
                fails = GetInt(d, "Fails");
                v.HaveAccess = GetInt(d, "HaveAccess") == 1;
                v.Sticky = (uint)GetLong(d, "Sticky");
                v.Filter = (uint)GetLong(d, "Filter");
                v.Toggle = (uint)GetLong(d, "Toggle");
                v.TouchedTaskMgr = GetInt(d, "TouchedTaskMgr") == 1;
                v.HadTaskMgrValue = GetInt(d, "HadTaskMgrValue") == 1;
                v.TaskMgrValueOld = GetInt(d, "TaskMgrValueOld");
                return true;
            }
            catch { v = null; return false; }
        }

        static int GetInt(Dictionary<string, string> d, string k)
        {
            string s; int n;
            return d.TryGetValue(k, out s) && int.TryParse(s, out n) ? n : 0;
        }
        static long GetLong(Dictionary<string, string> d, string k)
        {
            string s; long n;
            return d.TryGetValue(k, out s) && long.TryParse(s, out n) ? n : 0;
        }
    }

    // ------------------------------------------------------------------ Tray application

    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly HotkeyWindow hotkey;
        readonly System.Windows.Forms.Timer poll;
        readonly EventWaitHandle lockEvent;
        Settings settings;
        LockSession session;
        bool dialogOpen;
        bool pendingLock;
        LockGuard.Saved recovered;
        int recoveredFails;

        public TrayApp(bool lockNow)
        {
            settings = Settings.Load();
            lockEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.LockEventName);

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripItem lockItem = menu.Items.Add("Lock now", null, delegate { Lock(); });
            lockItem.Font = new Font(lockItem.Font, FontStyle.Bold);
            menu.Items.Add("Settings...", null, delegate { ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitApp(); });

            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Shield;
            tray.Text = "OverlayLock - click to lock (Ctrl+Alt+L)";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Lock(); };
            tray.Visible = true;

            hotkey = new HotkeyWindow();
            hotkey.Pressed += delegate { Lock(); };

            // Were we locked when the app last stopped (crash, power loss, sign-out)?
            // Put back the original system settings. We deliberately don't re-lock here:
            // getting back in already needed the Windows sign-in, and re-locking would trap
            // someone who forgot the overlay password.
            LockGuard.Saved st; int fails;
            if (LockGuard.ReadMarker(out st, out fails)) LockGuard.RestoreFrom(st);

            poll = new System.Windows.Forms.Timer();
            poll.Interval = 250;
            poll.Tick += OnPoll;
            poll.Start();

            if (!settings.HasPassword)
            {
                tray.ShowBalloonTip(4000, "OverlayLock", "Set a password to start using OverlayLock.", ToolTipIcon.Info);
                System.Windows.Forms.Timer once = new System.Windows.Forms.Timer();
                once.Interval = 300;
                once.Tick += delegate { once.Stop(); once.Dispose(); ShowSettings(); };
                once.Start();
            }
            else
            {
                pendingLock = lockNow;
                if (!hotkey.Registered)
                    tray.ShowBalloonTip(4000, "OverlayLock",
                        "Ctrl+Alt+L is used by another app. Click the tray icon to lock instead.", ToolTipIcon.Warning);
            }
        }

        void OnPoll(object sender, EventArgs e)
        {
            if (pendingLock) { pendingLock = false; Lock(); }
            if (lockEvent.WaitOne(0)) Lock();

            if (session == null && !dialogOpen && settings.HasPassword && settings.IdleMinutes > 0)
            {
                if (Native.IdleMilliseconds() >= (uint)settings.IdleMinutes * 60000u) Lock();
            }
        }

        void Lock()
        {
            if (session != null || dialogOpen) return;
            if (!settings.HasPassword) { ShowSettings(); return; }
            session = new LockSession(settings, recovered, recoveredFails);
            recovered = null; recoveredFails = 0;
            session.Unlocked += delegate { session = null; };
            session.Start();
        }

        // Settings and Exit need the password, so someone at an unlocked PC can't quietly
        // turn off auto-lock or quit the app.
        bool Confirm(string action)
        {
            if (!settings.HasPassword) return true;
            using (PasswordPrompt p = new PasswordPrompt(settings, action))
                return p.ShowDialog() == DialogResult.OK;
        }

        void ShowSettings()
        {
            if (dialogOpen || session != null) return;
            dialogOpen = true;
            try
            {
                if (!Confirm("open Settings")) return;
                using (SettingsForm f = new SettingsForm(settings)) f.ShowDialog();
                settings = Settings.Load();
            }
            finally { dialogOpen = false; }
        }

        void ExitApp()
        {
            if (dialogOpen || session != null) return;
            dialogOpen = true;
            try { if (!Confirm("exit OverlayLock")) return; }
            finally { dialogOpen = false; }

            poll.Stop();
            hotkey.Dispose();
            tray.Visible = false;
            tray.Dispose();
            lockEvent.Dispose();
            ExitThread();
        }
    }

    class PasswordPrompt : Form
    {
        readonly Settings s;
        readonly TextBox box;
        int fails;

        public PasswordPrompt(Settings s, string action)
        {
            this.s = s;
            Text = "OverlayLock";
            Icon = SystemIcons.Shield;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            SuspendLayout();
            Label l = new Label();
            l.Text = "Enter your password to " + action + ":";
            l.AutoSize = true;
            l.Location = new Point(14, 14);
            box = new TextBox();
            box.UseSystemPasswordChar = true;
            box.SetBounds(16, 40, 260, 24);
            Button ok = new Button();
            ok.Text = "OK";
            ok.SetBounds(110, 76, 80, 28);
            ok.Click += OnOk;
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(196, 76, 80, 28);
            Controls.Add(l); Controls.Add(box); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(292, 118);
            ResumeLayout(false);
        }

        void OnOk(object sender, EventArgs e)
        {
            if (s.Verify(box.Text)) { DialogResult = DialogResult.OK; Close(); return; }
            fails++;
            box.Clear();
            Thread.Sleep(Math.Min(5000, 500 * fails));
            MessageBox.Show(this, "Incorrect password.", "OverlayLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (fails >= 3) { DialogResult = DialogResult.Cancel; Close(); }
        }
    }

    // ------------------------------------------------------------------ Lock session

    class LockSession
    {
        readonly Settings s;
        readonly List<OverlayForm> overlays = new List<OverlayForm>();
        readonly KeyboardBlocker blocker = new KeyboardBlocker();
        readonly LockGuard guard = new LockGuard();
        readonly LockGuard.Saved recovered;
        LockPanel panel;
        Image bg;
        System.Windows.Forms.Timer keeper;
        string monitorSig = "";
        bool done;
        int fails;
        DateTime cooldownUntil = DateTime.MinValue;

        public event EventHandler Unlocked;

        public LockSession(Settings s, LockGuard.Saved recovered, int recoveredFails)
        {
            this.s = s;
            this.recovered = recovered;
            fails = Math.Max(0, recoveredFails);
            if (fails >= 3) cooldownUntil = DateTime.UtcNow.AddSeconds(CooldownFor(fails));
        }

        public int Fails { get { return fails; } }

        public int CooldownSecondsLeft
        {
            get
            {
                double sec = (cooldownUntil - DateTime.UtcNow).TotalSeconds;
                return sec > 0 ? (int)Math.Ceiling(sec) : 0;
            }
        }

        // 3rd miss: 5s, then 10, 20, 40, 80, 160, then 5 minutes per try.
        static int CooldownFor(int fails)
        {
            if (fails < 3) return 0;
            return Math.Min(300, 5 << Math.Min(fails - 3, 8));
        }

        public void Start()
        {
            guard.Apply(s, recovered, fails);
            bg = LoadImage(s.BackgroundImage);
            BuildWindows();
            blocker.Start();

            keeper = new System.Windows.Forms.Timer();
            keeper.Interval = 400;
            keeper.Tick += delegate { Keep(); };
            keeper.Start();

            FocusPanel();
        }

        void BuildWindows()
        {
            List<Monitors.Info> mons = Monitors.Get();
            monitorSig = Monitors.Signature(mons);

            OverlayForm primary = null;
            foreach (Monitors.Info m in mons)
            {
                OverlayForm f = new OverlayForm(m.Bounds, s, bg);
                f.MouseDown += delegate { FocusPanel(); };
                f.Show();
                overlays.Add(f);
                if (m.Primary && primary == null) primary = f;
            }
            if (primary == null) primary = overlays[0];

            panel = new LockPanel(primary.Bounds, s, this);
            panel.Show(primary);
        }

        void TearDownWindows()
        {
            if (panel != null) { panel.AllowClose = true; panel.Close(); panel.Dispose(); panel = null; }
            foreach (OverlayForm f in overlays) { f.AllowClose = true; f.Close(); f.Dispose(); }
            overlays.Clear();
        }

        public bool Submit(string pw)
        {
            if (done) return true;
            if (CooldownSecondsLeft > 0) return false;
            if (s.Verify(pw)) { Finish(); return true; }
            fails++;
            if (fails >= 3) cooldownUntil = DateTime.UtcNow.AddSeconds(CooldownFor(fails));
            guard.UpdateFails(fails);
            return false;
        }

        void Keep()
        {
            if (done) return;

            // A monitor was added/removed or a resolution changed: cover the new layout.
            string sig = Monitors.Signature(Monitors.Get());
            if (sig != monitorSig)
            {
                TearDownWindows();
                BuildWindows();
                FocusPanel();
                return;
            }

            foreach (OverlayForm f in overlays) Native.MakeTopmost(f.Handle);
            Native.MakeTopmost(panel.Handle);
            if (Native.GetForegroundWindow() != panel.Handle) FocusPanel();
        }

        void FocusPanel()
        {
            if (done || panel == null) return;
            panel.Activate();
            Native.SetForegroundWindow(panel.Handle);
            panel.FocusBox();
        }

        void Finish()
        {
            if (done) return;
            done = true;
            keeper.Stop();
            keeper.Dispose();
            blocker.Stop();
            TearDownWindows();
            if (bg != null) { bg.Dispose(); bg = null; }
            guard.Restore();
            if (Unlocked != null) Unlocked(this, EventArgs.Empty);
        }

        static Image LoadImage(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (Image img = Image.FromStream(fs))
                    return new Bitmap(img);
            }
            catch { return null; }
        }
    }

    // Full-screen tinted background, one per monitor.
    class OverlayForm : Form
    {
        public bool AllowClose;

        public OverlayForm(Rectangle bounds, Settings s, Image bg)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = s.BackColor;
            Opacity = s.Opacity / 100.0;
            if (bg != null)
            {
                BackgroundImage = bg;
                BackgroundImageLayout = s.ImageLayout;
            }
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80; /* WS_EX_TOOLWINDOW */ return cp; }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }

    // Clock, message and password box, floating fully opaque over the overlay.
    // The form's own background colour is keyed out so only the controls show.
    class LockPanel : Form
    {
        static readonly Color Key = Color.FromArgb(1, 1, 2);
        const string Prompt = "Type your password and press Enter";

        public bool AllowClose;

        readonly Settings s;
        readonly LockSession session;
        readonly Label clock, date, message, hint;
        readonly TextBox box;
        readonly System.Windows.Forms.Timer tick;
        readonly float scale = 1f;
        bool lastWasError;

        public LockPanel(Rectangle bounds, Settings s, LockSession session)
        {
            this.s = s;
            this.session = session;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Key;
            TransparencyKey = Key;

            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;

            clock = MakeLabel(new Font("Segoe UI Light", s.ClockSize, FontStyle.Regular, GraphicsUnit.Point), s.ClockColor);
            date = MakeLabel(new Font("Segoe UI", 18f, FontStyle.Regular, GraphicsUnit.Point), s.ClockColor);
            message = MakeLabel(new Font("Segoe UI Semibold", 14f, FontStyle.Regular, GraphicsUnit.Point), s.ClockColor);
            hint = MakeLabel(new Font("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point), Dim(s.ClockColor));

            clock.Visible = s.ShowClock;
            date.Visible = s.ShowDate;
            message.Text = s.Message ?? "";
            message.Visible = message.Text.Trim().Length > 0;
            hint.Text = Prompt;

            box = new TextBox();
            box.Font = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Point);
            box.UseSystemPasswordChar = true;
            box.ShortcutsEnabled = false;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Color.FromArgb(34, 38, 50);
            box.ForeColor = Color.White;
            box.TextAlign = HorizontalAlignment.Center;
            box.Width = (int)(340 * scale);
            box.KeyDown += OnBoxKeyDown;
            Controls.Add(box);

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 500;
            tick.Tick += delegate { Refresh2(); };
            tick.Start();

            Refresh2();
        }

        Label MakeLabel(Font font, Color color)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Key;
            l.UseMnemonic = false;
            Controls.Add(l);
            return l;
        }

        static Color Dim(Color c)
        {
            return Color.FromArgb((int)(c.R * 0.75 + 20), (int)(c.G * 0.75 + 20), (int)(c.B * 0.75 + 20));
        }

        // Updates clock and cooldown state.
        void Refresh2()
        {
            DateTime now = DateTime.Now;
            clock.Text = now.ToString(s.Use24Hour ? "HH:mm" : "h:mm tt");
            date.Text = now.ToString("dddd, MMMM d");

            int wait = session.CooldownSecondsLeft;
            if (wait > 0)
            {
                if (box.Enabled) { box.Clear(); box.Enabled = false; }
                SetHint(string.Format("Incorrect password. Try again in {0}s", wait), true);
            }
            else if (!box.Enabled)
            {
                box.Enabled = true;
                SetHint(Prompt, false);
                box.Focus();
            }
            Relayout();
        }

        void Relayout()
        {
            List<Control> items = new List<Control>();
            if (clock.Visible) items.Add(clock);
            if (date.Visible) items.Add(date);
            if (message.Visible) items.Add(message);
            items.Add(box);
            items.Add(hint);

            int gap = (int)(10 * scale);
            int total = (int)(30 * scale);
            foreach (Control c in items) total += c.Height + gap;

            int y = (ClientSize.Height - total) / 2 - (int)(40 * scale);
            foreach (Control c in items)
            {
                if (c == box) y += (int)(30 * scale);
                c.Left = (ClientSize.Width - c.Width) / 2;
                c.Top = y;
                y += c.Height + gap;
            }
        }

        public void FocusBox()
        {
            if (box.Enabled && !box.Focused) box.Focus();
        }

        void OnBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                string pw = box.Text;
                box.Clear();
                if (session.Submit(pw)) return; // session closes this form
                if (session.CooldownSecondsLeft == 0) SetHint("Incorrect password", true);
                Refresh2();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                box.Clear();
            }
        }

        void SetHint(string text, bool error)
        {
            if (hint.Text == text && lastWasError == error) return;
            lastWasError = error;
            hint.Text = text;
            hint.ForeColor = error ? Color.FromArgb(255, 120, 120) : Dim(s.ClockColor);
            Relayout();
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80; /* WS_EX_TOOLWINDOW */ return cp; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Relayout();
            FocusBox();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose) { e.Cancel = true; return; }
            tick.Stop();
            base.OnFormClosing(e);
        }
    }

    // ------------------------------------------------------------------ Settings window

    class SettingsForm : Form
    {
        readonly Settings s;
        readonly TextBox newPw, confPw, imgPath, msg;
        readonly NumericUpDown idle, clockSize;
        readonly TrackBar opacity;
        readonly Label opacityVal;
        readonly ComboBox layout;
        readonly CheckBox startup, keepAwake, keepDisplay, blockTaskMgr, showClock, use24, showDate;
        Color bgColor, clockColor;

        const int LabelX = 16, CtrlX = 200, RowH = 32;
        int y = 12;

        public SettingsForm(Settings s)
        {
            this.s = s;
            bgColor = s.BackColor;
            clockColor = s.ClockColor;

            Text = "OverlayLock Settings";
            Icon = SystemIcons.Shield;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            SuspendLayout();

            Section("Password");
            newPw = AddText(s.HasPassword ? "New password" : "Password", true, 220);
            confPw = AddText("Confirm password", true, 220);
            Note(s.HasPassword
                ? "Leave blank to keep the current password."
                : "At least " + Settings.MinPasswordLength + " characters.");

            Section("Locking");
            idle = AddNumeric("Auto-lock when idle (min)", 0, 240, s.IdleMinutes);
            Note("0 turns auto-lock off. Lock anytime: left-click the tray icon or press Ctrl+Alt+L.");
            keepAwake = AddCheck("Keep the PC awake while locked (jobs keep running)", s.KeepAwake);
            keepDisplay = AddCheck("Keep the display on too", s.KeepDisplayOn);
            blockTaskMgr = AddCheck("Block Task Manager while locked", s.DisableTaskManager);
            startup = AddCheck("Start with Windows", Settings.GetStartup());

            Section("Background");
            Row("Opacity");
            opacity = new TrackBar();
            opacity.Minimum = 10; opacity.Maximum = 100; opacity.TickFrequency = 10;
            opacity.Value = s.Opacity;
            opacity.SetBounds(CtrlX - 6, y - 4, 230, 30);
            opacityVal = new Label();
            opacityVal.AutoSize = true;
            opacityVal.SetBounds(CtrlX + 230, y, 50, 20);
            opacity.ValueChanged += delegate { opacityVal.Text = opacity.Value + "%"; };
            opacityVal.Text = opacity.Value + "%";
            Controls.Add(opacity); Controls.Add(opacityVal);
            y += RowH + 4;

            AddColor("Background color", bgColor, delegate(Color c) { bgColor = c; });

            Row("Background image");
            imgPath = new TextBox();
            imgPath.Text = s.BackgroundImage;
            imgPath.SetBounds(CtrlX, y - 2, 170, 24);
            Button browse = new Button();
            browse.Text = "Browse...";
            browse.SetBounds(CtrlX + 176, y - 3, 76, 26);
            browse.Click += OnBrowse;
            Button clear = new Button();
            clear.Text = "Clear";
            clear.SetBounds(CtrlX + 256, y - 3, 56, 26);
            clear.Click += delegate { imgPath.Text = ""; };
            Controls.Add(imgPath); Controls.Add(browse); Controls.Add(clear);
            y += RowH;

            Row("Image layout");
            layout = new ComboBox();
            layout.DropDownStyle = ComboBoxStyle.DropDownList;
            layout.Items.AddRange(new object[] { "Zoom", "Stretch", "Center", "Tile" });
            layout.SelectedItem = s.ImageLayout.ToString();
            if (layout.SelectedIndex < 0) layout.SelectedIndex = 0;
            layout.SetBounds(CtrlX, y - 2, 120, 24);
            Controls.Add(layout);
            y += RowH;

            Section("Clock & text");
            showClock = AddCheck("Show clock", s.ShowClock);
            use24 = AddCheck("24-hour time", s.Use24Hour);
            showDate = AddCheck("Show date", s.ShowDate);
            clockSize = AddNumeric("Clock size (pt)", 20, 200, s.ClockSize);
            AddColor("Text color", clockColor, delegate(Color c) { clockColor = c; });
            msg = AddText("Message", false, 300);
            msg.Text = s.Message;

            y += 10;
            Button save = new Button();
            save.Text = "Save";
            save.SetBounds(CtrlX + 132, y, 86, 30);
            save.Click += OnSave;
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(CtrlX + 226, y, 86, 30);
            Controls.Add(save); Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            y += 46;

            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(CtrlX + 330, y);
            ResumeLayout(false);
        }

        void Section(string text)
        {
            if (y > 12) y += 8;
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            l.AutoSize = true;
            l.Location = new Point(LabelX - 4, y);
            Controls.Add(l);
            y += 28;
        }

        void Row(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(LabelX, y + 2);
            Controls.Add(l);
        }

        void Note(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = SystemColors.GrayText;
            l.SetBounds(LabelX, y - 6, CtrlX + 300 - LabelX, 34);
            Controls.Add(l);
            y += 30;
        }

        TextBox AddText(string label, bool password, int width)
        {
            Row(label);
            TextBox t = new TextBox();
            t.UseSystemPasswordChar = password;
            t.SetBounds(CtrlX, y - 2, width, 24);
            Controls.Add(t);
            y += RowH;
            return t;
        }

        NumericUpDown AddNumeric(string label, int min, int max, int value)
        {
            Row(label);
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min; n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.SetBounds(CtrlX, y - 2, 80, 24);
            Controls.Add(n);
            y += RowH;
            return n;
        }

        CheckBox AddCheck(string label, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = label;
            c.Checked = value;
            c.AutoSize = true;
            c.Location = new Point(LabelX, y);
            Controls.Add(c);
            y += 26;
            return c;
        }

        void AddColor(string label, Color initial, Action<Color> onPick)
        {
            Row(label);
            Button b = new Button();
            b.SetBounds(CtrlX, y - 3, 80, 26);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = initial;
            b.Click += delegate
            {
                using (ColorDialog d = new ColorDialog())
                {
                    d.FullOpen = true;
                    d.Color = b.BackColor;
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        Color c = Color.FromArgb(255, d.Color);
                        b.BackColor = c;
                        onPick(c);
                    }
                }
            };
            Controls.Add(b);
            y += RowH;
        }

        void OnBrowse(object sender, EventArgs e)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Choose a background image";
                d.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) imgPath.Text = d.FileName;
            }
        }

        void OnSave(object sender, EventArgs e)
        {
            string np = newPw.Text;
            if (np.Length > 0)
            {
                if (np.Length < Settings.MinPasswordLength)
                { Warn("Use a password of at least " + Settings.MinPasswordLength + " characters."); return; }
                if (np != confPw.Text) { Warn("The passwords don't match."); return; }
            }
            else if (!s.HasPassword)
            {
                Warn("Please set a password.");
                return;
            }

            string img = imgPath.Text.Trim();
            if (img.Length > 0 && !File.Exists(img)) { Warn("The background image file wasn't found."); return; }

            if (np.Length > 0) s.SetPassword(np);
            s.IdleMinutes = (int)idle.Value;
            s.KeepAwake = keepAwake.Checked;
            s.KeepDisplayOn = keepDisplay.Checked;
            s.DisableTaskManager = blockTaskMgr.Checked;
            s.Opacity = opacity.Value;
            s.BackColor = bgColor;
            s.BackgroundImage = img;
            s.ImageLayout = (ImageLayout)Enum.Parse(typeof(ImageLayout), (string)layout.SelectedItem);
            s.ShowClock = showClock.Checked;
            s.Use24Hour = use24.Checked;
            s.ShowDate = showDate.Checked;
            s.ClockSize = (int)clockSize.Value;
            s.ClockColor = clockColor;
            s.Message = msg.Text;

            try { s.Save(); }
            catch (Exception ex) { Warn("Couldn't save settings: " + ex.Message); return; }
            Settings.SetStartup(startup.Checked);

            DialogResult = DialogResult.OK;
            Close();
        }

        void Warn(string text)
        {
            MessageBox.Show(this, text, "OverlayLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

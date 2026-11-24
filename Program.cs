using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarDeskController
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "Local\\TaskbarDeskController.SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "TaskbarDeskController";
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem enabledItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly System.Windows.Forms.Timer settleTimer;
        private readonly System.Windows.Forms.Timer startupTimer;
        private readonly WinEventDelegate callback;
        private readonly uint foregroundHook, showHook, hideHook, minStartHook, minEndHook;
        private readonly string configPath;
        private readonly string recoveryPath;
        private bool enabled = true;
        private bool appBarStateCaptured;
        private uint originalAppBarState;
        private bool disposed;
        private int startupChecksRemaining;

        public TrayApp()
        {
            configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarDeskController", "settings.ini");
            recoveryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarDeskController", "appbar-recovery.txt");
            LoadSettings();
            var menu = new ContextMenuStrip();
            enabledItem = new ToolStripMenuItem("Activado", null, (s, e) => { enabled = !enabled; enabledItem.Checked = enabled; SaveSettings(); Reconcile(); });
            startupItem = new ToolStripMenuItem("Iniciar con Windows", null, (s, e) => ToggleStartup());
            menu.Items.Add(enabledItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Salir", null, (s, e) => ExitThread()));
            enabledItem.Checked = enabled;
            startupItem.Checked = IsStartupEnabled();
            tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Control de barra de tareas", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (s, e) => { enabled = !enabled; enabledItem.Checked = enabled; SaveSettings(); Reconcile(); };
            settleTimer = new System.Windows.Forms.Timer { Interval = 140 };
            settleTimer.Tick += (s, e) => { settleTimer.Stop(); Reconcile(); };
            // Run entries can launch before Explorer has finished restoring the taskbar
            // and desktop. Retry briefly after logon; normal operation remains event-driven.
            startupChecksRemaining = 12;
            startupTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            startupTimer.Tick += (s, e) =>
            {
                Reconcile();
                if (--startupChecksRemaining <= 0)
                {
                    startupTimer.Stop();
                    startupTimer.Dispose();
                }
            };
            startupTimer.Start();
            callback = OnWinEvent;
            foregroundHook = Native.SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, callback, 0, 0, 0x0000);
            showHook = Native.SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, callback, 0, 0, 0x0000);
            hideHook = Native.SetWinEventHook(0x8003, 0x8003, IntPtr.Zero, callback, 0, 0, 0x0000);
            minStartHook = Native.SetWinEventHook(0x0016, 0x0016, IntPtr.Zero, callback, 0, 0, 0x0000);
            minEndHook = Native.SetWinEventHook(0x0017, 0x0017, IntPtr.Zero, callback, 0, 0, 0x0000);
            Reconcile();
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (disposed || hwnd == IntPtr.Zero) return;
            // Foreground transitions cover desktop, Alt+Tab and Win+D; show/hide/minimize events
            // catch shell timing edges. A short debounce lets Explorer finish the transition.
            if (eventType == 0x0003 || idObject == 0) ScheduleReconcile();
        }

        private void ScheduleReconcile()
        {
            if (settleTimer == null || disposed) return;
            settleTimer.Stop();
            settleTimer.Start();
        }

        private void Reconcile()
        {
            if (disposed) return;
            IntPtr taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return;
            CaptureOrRecoverAppBarState(taskbar);
            if (!appBarStateCaptured) return;
            if (!enabled)
            {
                RestoreOriginalAppBarState(taskbar);
                return;
            }

            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return;
            if (IsFullscreen(foreground)) return;
            bool desktop = IsDesktopWindow(foreground);
            // Native taskbar auto-hide keeps the shell's taskband and keyboard handling
            // alive, so Win+1/Win+2 and edge-hover continue to work on the desktop.
            SetManagedAutoHide(taskbar, desktop);
        }

        private void CaptureOrRecoverAppBarState(IntPtr taskbar)
        {
            if (appBarStateCaptured) return;
            uint recovered;
            if (TryReadRecoveryState(out recovered))
            {
                Native.SetAppBarState(taskbar, recovered);
                uint actual = Native.GetAppBarState(taskbar);
                if (actual != recovered) return;
                ClearRecoveryState();
            }
            originalAppBarState = Native.GetAppBarState(taskbar);
            appBarStateCaptured = true;
        }

        private void SetManagedAutoHide(IntPtr taskbar, bool autoHide)
        {
            uint current = Native.GetAppBarState(taskbar);
            uint desired = autoHide ? (current | Native.ABS_AUTOHIDE) : (current & ~Native.ABS_AUTOHIDE);
            if (current == desired) return;
            if (!WriteRecoveryState(originalAppBarState)) return;
            Native.SetAppBarState(taskbar, desired);
            uint actual = Native.GetAppBarState(taskbar);
            if (actual == originalAppBarState) ClearRecoveryState();
        }

        private void RestoreOriginalAppBarState(IntPtr taskbar)
        {
            if (!appBarStateCaptured) return;
            uint current = Native.GetAppBarState(taskbar);
            if (current == originalAppBarState)
            {
                ClearRecoveryState();
                return;
            }
            if (!File.Exists(recoveryPath) && !WriteRecoveryState(originalAppBarState)) return;
            Native.SetAppBarState(taskbar, originalAppBarState);
            uint actual = Native.GetAppBarState(taskbar);
            if (actual == originalAppBarState) ClearRecoveryState();
        }

        private bool WriteRecoveryState(uint state)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(recoveryPath));
                File.WriteAllText(recoveryPath, state.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return true;
            }
            catch { return false; }
        }

        private bool TryReadRecoveryState(out uint state)
        {
            state = 0;
            try { return File.Exists(recoveryPath) && uint.TryParse(File.ReadAllText(recoveryPath).Trim(), out state); }
            catch { return false; }
        }

        private void ClearRecoveryState()
        {
            try { if (File.Exists(recoveryPath)) File.Delete(recoveryPath); } catch { }
        }

        private static bool IsDesktopWindow(IntPtr hwnd)
        {
            if (hwnd == Native.GetShellWindow()) return true;
            IntPtr root = Native.GetAncestor(hwnd, 2); // GA_ROOT
            if (root == IntPtr.Zero) root = hwnd;
            var cls = new System.Text.StringBuilder(128);
            Native.GetClassName(root, cls, cls.Capacity);
            string name = cls.ToString();
            return name == "Progman" || name == "WorkerW";
        }

        private static bool IsFullscreen(IntPtr hwnd)
        {
            if (IsDesktopWindow(hwnd)) return false;
            var placement = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT)) };
            if (!Native.GetWindowPlacement(hwnd, ref placement) || placement.showCmd == 2) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return false;
            IntPtr monitor = Native.MonitorFromWindow(hwnd, 2); // nearest monitor
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            if (!Native.GetMonitorInfo(monitor, ref mi)) return false;
            const int tolerance = 2;
            return r.Left <= mi.rcMonitor.Left + tolerance && r.Top <= mi.rcMonitor.Top + tolerance &&
                   r.Right >= mi.rcMonitor.Right - tolerance && r.Bottom >= mi.rcMonitor.Bottom - tolerance;
        }

        private void ToggleStartup()
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (IsStartupEnabled()) key.DeleteValue(RunName, false);
                else key.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
            }
            startupItem.Checked = IsStartupEnabled();
        }

        private static bool IsStartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false)) return key != null && key.GetValue(RunName) != null;
        }

        private void LoadSettings()
        {
            try { if (File.Exists(configPath)) enabled = File.ReadAllText(configPath).Trim() != "0"; } catch { }
        }
        private void SaveSettings()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(configPath)); File.WriteAllText(configPath, enabled ? "1" : "0"); } catch { }
        }

        protected override void ExitThreadCore()
        {
            disposed = true;
            if (settleTimer != null) { settleTimer.Stop(); settleTimer.Dispose(); }
            if (startupTimer != null) { startupTimer.Stop(); startupTimer.Dispose(); }
            Native.UnhookWinEvent(foregroundHook); Native.UnhookWinEvent(showHook); Native.UnhookWinEvent(hideHook);
            Native.UnhookWinEvent(minStartHook); Native.UnhookWinEvent(minEndHook);
            if (appBarStateCaptured) RestoreOriginalAppBarState(Native.FindWindow("Shell_TrayWnd", null));
            tray.Visible = false; tray.Dispose();
            base.ExitThreadCore();
        }
    }

    internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    internal static class Native
    {
        internal const uint ABS_AUTOHIDE = 0x00000001;
        private const uint ABM_GETSTATE = 0x00000004;
        private const uint ABM_SETSTATE = 0x0000000A;
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT ptMinPosition, ptMaxPosition; public RECT rcNormalPosition; }
        [StructLayout(LayoutKind.Sequential)] internal struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        [StructLayout(LayoutKind.Sequential)] internal struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public IntPtr lParam; }
        [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);
        internal static uint GetAppBarState(IntPtr taskbar)
        {
            var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)), hWnd = taskbar };
            return unchecked((uint)SHAppBarMessage(ABM_GETSTATE, ref data).ToUInt64());
        }
        internal static void SetAppBarState(IntPtr taskbar, uint state)
        {
            var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)), hWnd = taskbar, lParam = new IntPtr(state) };
            SHAppBarMessage(ABM_SETSTATE, ref data);
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] internal static extern uint SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(uint hook);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    }
}

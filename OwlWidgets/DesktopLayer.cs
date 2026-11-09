using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OwlWidgets
{
    /// <summary>
    /// Keeps widget windows above the desktop and below regular applications.
    /// The windows stay top-level, so mouse input reaches them.
    /// Win+D and "Show desktop" raise the icons host; the layer puts the widgets
    /// right above it and moves them back down when the windows are restored.
    /// </summary>
    internal static class DesktopLayer
    {
        private const string AnchorClassName = "OwlWidgetsDesktopLayer";
        private const string SystemWindowTitle = "System";
        private const string HelperWindowTitle = "PositioningHelper";

        private const uint GwHwndNext = 2;
        private const uint GwHwndPrev = 3;
        private const int GwlExStyle = -20;
        private const int ErrorClassAlreadyExists = 1410;

        private const uint WsPopup = 0x80000000;
        private const uint WsDisabled = 0x08000000;
        private const uint WsExToolWindow = 0x00000080;
        private const uint WsExTopmost = 0x00000008;

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpHideWindow = 0x0080;
        private const uint SwpNoOwnerZOrder = 0x0200;
        private const uint ZOrderFlags = SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder;

        private const uint EventSystemForeground = 0x0003;
        private const uint EventSystemMenuEnd = 0x0005;
        private const uint EventSystemMenuPopupEnd = 0x0007;
        private const uint WineventOutOfContext = 0x0000;
        private const uint WineventSkipOwnProcess = 0x0002;
        private const uint ObjidWindow = 0;
        private const int DwmwaCloaked = 14;

        private const int PollMs = 100;
        private const int RepairRetries = 8;

        private static readonly IntPtr HwndBottom = new(1);
        private static readonly IntPtr HwndTopmost = new(-1);
        private static readonly IntPtr HwndNotTopmost = new(-2);
        private static readonly WndProc AnchorWndProcDelegate = AnchorWndProc;
        private static readonly WinEventProc WinEventProcDelegate = OnWinEvent;

        private static readonly List<IntPtr> _windows = [];

        private static IntPtr _systemWindow;
        private static IntPtr _helperWindow;
        private static IntPtr _winEventHook;
        private static readonly EventHandler RetryTick = OnRetryTick;

        private static DispatcherTimer? _timer;
        private static DispatcherTimer? _retryTimer;
        private static int _adjusting;
        private static int _repairRetries;
        private static bool _disposed;
        private static bool _desktopRaised;

        /// <summary>
        /// The desktop was raised above regular windows: Win+D or "Show desktop".
        /// </summary>
        internal static event Action? DesktopRaised;

        /// <summary>
        /// HWND the wrappers must keep their windows below. The layer does not move widget windows itself:
        /// a foreign SetWindowPos runs into the z-order lock inside the wrapper process.
        /// </summary>
        internal static event Action<IntPtr>? WidgetPlacement;

        internal static void Initialize()
        {
            if (_disposed || _systemWindow != IntPtr.Zero)
                return;

            var wndClass = new WndClass
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(AnchorWndProcDelegate),
                hInstance = GetModuleHandle(null),
                lpszClassName = AnchorClassName,
            };
            if (RegisterClass(ref wndClass) == 0 && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register the desktop layer window class.");

            _systemWindow = CreateAnchor(SystemWindowTitle);
            _helperWindow = CreateAnchor(HelperWindowTitle);

            using (AdjustZOrder())
            {
                SetWindowPos(_systemWindow, HwndBottom, 0, 0, 0, 0, ZOrderFlags);
                SetWindowPos(_helperWindow, HwndBottom, 0, 0, 0, 0, ZOrderFlags);
            }

            _winEventHook = SetWinEventHook(EventSystemForeground, EventSystemMenuPopupEnd, IntPtr.Zero,
                WinEventProcDelegate, 0, 0, WineventOutOfContext | WineventSkipOwnProcess);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollMs) };
            _timer.Tick += (_, _) => RepairIfNeeded();
            _timer.Start();
        }

        internal static void Register(IntPtr hwnd)
        {
            if (_disposed || hwnd == IntPtr.Zero)
                return;
            Initialize();
            if (!_windows.Contains(hwnd))
                _windows.Add(hwnd);
            PlaceWindows();
        }

        internal static void Unregister(IntPtr hwnd)
        {
            _windows.Remove(hwnd);
            if (_disposed)
                return;
            PlaceWindows();
        }

        internal static void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer?.Stop();
            _retryTimer?.Stop();
            if (_winEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            _windows.Clear();
            DestroyAnchor(ref _helperWindow);
            DestroyAnchor(ref _systemWindow);
        }

        private static void RepairIfNeeded()
        {
            if (_disposed || _windows.Count == 0)
                return;

            var host = GetDesktopIconsHost();
            var raised = host != IntPtr.Zero && IsDesktopRaised(host);
            if (raised && !_desktopRaised)
                DesktopRaised?.Invoke();
            _desktopRaised = raised;

            if (host == IntPtr.Zero || !NeedsRepair(host))
                return;

            Debug.WriteLine("DesktopLayer: widgets were covered by the desktop, restoring");
            PlaceWindows();
        }

        /// <summary>
        /// Puts the widgets right above the icons host and below the first foreign window above it.
        /// Selecting a shortcut or closing the desktop menu raises the host without Win+D.
        /// </summary>
        private static void PlaceWindows()
        {
            if (_disposed || _helperWindow == IntPtr.Zero)
                return;

            var host = GetDesktopIconsHost();
            var raised = host != IntPtr.Zero && IsDesktopRaised(host);
            var cover = host == IntPtr.Zero ? HwndBottom : FindCover(host, raised);

            using (AdjustZOrder())
            {
                PlaceBelow(_systemWindow, cover, raised);
                PlaceBelow(_helperWindow, cover, raised);
            }

            WidgetPlacement?.Invoke(cover);
        }

        private static void PlaceBelow(IntPtr hwnd, IntPtr cover, bool raised)
        {
            if (hwnd == IntPtr.Zero)
                return;

            if (cover == HwndBottom || cover == IntPtr.Zero)
            {
                SetWindowPos(hwnd, raised ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0, ZOrderFlags);
                return;
            }

            var coverIsTopmost = raised && IsTopmost(cover);
            SetWindowPos(hwnd, coverIsTopmost ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0, ZOrderFlags);
            SetWindowPos(hwnd, cover, 0, 0, 0, 0, ZOrderFlags);
        }

        private static bool NeedsRepair(IntPtr host)
        {
            if (WidgetsAreBehind(host))
                return true;
            return !IsDesktopRaised(host) && WidgetsAreTopmost();
        }

        private static bool WidgetsAreBehind(IntPtr host)
        {
            foreach (var hwnd in _windows)
            {
                if (hwnd == IntPtr.Zero)
                    continue;
                if (!IsAbove(hwnd, host) || DesktopLayerCovers(hwnd))
                    return true;
            }

            return false;
        }

        private static bool WidgetsAreTopmost()
        {
            foreach (var hwnd in _windows)
            {
                if (hwnd != IntPtr.Zero && IsTopmost(hwnd))
                    return true;
            }

            return false;
        }

        private static IntPtr FindCover(IntPtr host, bool raised)
        {
            var cover = GetWindow(host, GwHwndPrev);
            while (cover != IntPtr.Zero &&
                   (IsOurWindow(cover) || IsDesktopLayerWindow(cover) || (!raised && IsTopmost(cover))))
                cover = GetWindow(cover, GwHwndPrev);
            return cover == IntPtr.Zero ? HwndBottom : cover;
        }

        private static bool IsDesktopRaised(IntPtr host)
        {
            for (var hwnd = GetWindow(host, GwHwndNext); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, GwHwndNext))
            {
                if (!IsWindowVisible(hwnd) || IsCloaked(hwnd) || !HasVisibleArea(hwnd))
                    continue;
                if (IsOurWindow(hwnd) || IsTopmost(hwnd) || IsShellWindow(hwnd))
                    continue;
                return true;
            }

            return false;
        }

        private static bool IsAbove(IntPtr window, IntPtr other)
        {
            for (var hwnd = GetWindow(window, GwHwndNext); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, GwHwndNext))
            {
                if (hwnd == other)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Progman or WorkerW is above the widget before any regular window.
        /// </summary>
        private static bool DesktopLayerCovers(IntPtr widget)
        {
            for (var hwnd = GetWindow(widget, GwHwndPrev); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, GwHwndPrev))
            {
                if (IsOurWindow(hwnd) || !IsWindowVisible(hwnd) || IsCloaked(hwnd) || !HasVisibleArea(hwnd))
                    continue;
                if (IsTopmost(hwnd))
                    continue;
                return IsDesktopLayerWindow(hwnd);
            }

            return false;
        }

        private static bool IsOurWindow(IntPtr hwnd)
        {
            if (hwnd == _systemWindow || hwnd == _helperWindow)
                return true;
            return _windows.Contains(hwnd);
        }

        private static bool IsTopmost(IntPtr hwnd)
        {
            return (GetWindowLong(hwnd, GwlExStyle) & (int)WsExTopmost) != 0;
        }

        private static bool IsShellWindow(IntPtr hwnd)
        {
            var shell = GetShellWindow();
            if (shell == IntPtr.Zero || !BelongsToSameProcess(shell, hwnd))
                return false;

            var name = GetClassName(hwnd);
            return name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        }

        private static bool IsDesktopLayerWindow(IntPtr hwnd)
        {
            if (!IsShellWindow(hwnd))
                return false;
            var name = GetClassName(hwnd);
            return name is "Progman" or "WorkerW";
        }

        private static bool IsCloaked(IntPtr hwnd)
        {
            return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        }

        private static bool HasVisibleArea(IntPtr hwnd)
        {
            return GetWindowRect(hwnd, out var rect) && rect.Right > rect.Left && rect.Bottom > rect.Top;
        }

        private static void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
            int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (_disposed)
                return;
            if (eventType == EventSystemForeground)
            {
                if ((uint)idObject != ObjidWindow)
                    return;
            }
            else if (eventType is not (EventSystemMenuEnd or EventSystemMenuPopupEnd))
            {
                return;
            }

            var foreground = hwnd;
            var type = eventType;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (type == EventSystemForeground && !IsDesktopForegroundCandidate(foreground))
                    return;
                _repairRetries = RepairRetries;
                TryRepair();
            });
        }

        private static void TryRepair()
        {
            if (_disposed)
                return;

            RepairIfNeeded();
            if (--_repairRetries <= 0)
                return;

            _retryTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _retryTimer.Stop();
            _retryTimer.Tick -= RetryTick;
            _retryTimer.Tick += RetryTick;
            _retryTimer.Start();
        }

        private static void OnRetryTick(object? sender, EventArgs e)
        {
            _retryTimer?.Stop();
            TryRepair();
        }

        private static bool IsDesktopForegroundCandidate(IntPtr hwnd)
        {
            var shell = GetShellWindow();
            if (shell == IntPtr.Zero || hwnd == IntPtr.Zero)
                return false;
            if (hwnd == shell)
                return true;
            return BelongsToSameProcess(shell, hwnd) && GetClassName(hwnd) == "WorkerW";
        }

        /// <summary>
        /// Parent of SHELLDLL_DefView: Progman on Windows 11 24H2 and WorkerW on earlier systems.
        /// </summary>
        private static IntPtr GetDesktopIconsHost()
        {
            var shell = GetShellWindow();
            if (shell == IntPtr.Zero)
                return IntPtr.Zero;

            if (FindWindowEx(shell, IntPtr.Zero, "SHELLDLL_DefView", string.Empty) != IntPtr.Zero)
                return shell;

            var worker = IntPtr.Zero;
            while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                if (!IsWindowVisible(worker) || !BelongsToSameProcess(shell, worker))
                    continue;
                if (FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", string.Empty) != IntPtr.Zero)
                    return worker;
            }

            return IntPtr.Zero;
        }

        private static IntPtr CreateAnchor(string title)
        {
            var hwnd = CreateWindowEx(WsExToolWindow, AnchorClassName, title, WsPopup | WsDisabled,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create the desktop layer window.");
            return hwnd;
        }

        private static void DestroyAnchor(ref IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;
            DestroyWindow(hwnd);
            hwnd = IntPtr.Zero;
        }

        private static bool BelongsToSameProcess(IntPtr first, IntPtr second)
        {
            GetWindowThreadProcessId(first, out var firstProcess);
            GetWindowThreadProcessId(second, out var secondProcess);
            return firstProcess == secondProcess;
        }

        private static string GetClassName(IntPtr hwnd)
        {
            var buffer = new char[64];
            var length = GetClassName(hwnd, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }

        private static IntPtr AnchorWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == 0x0046 && _adjusting == 0)
            {
                var position = Marshal.PtrToStructure<WindowPos>(lParam);
                position.flags |= SwpNoZOrder;
                Marshal.StructureToPtr(position, lParam, false);
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private static ZOrderScope AdjustZOrder()
        {
            _adjusting++;
            return new ZOrderScope();
        }

        private readonly struct ZOrderScope : IDisposable
        {
            public void Dispose()
            {
                _adjusting--;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPos
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClass
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
            int idChild, uint dwEventThread, uint dwmsEventTime);

        #region WinAPI

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClass(ref WndClass lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className,
            string? windowName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute,
            int cbAttribute);

        #endregion WinAPI
    }
}

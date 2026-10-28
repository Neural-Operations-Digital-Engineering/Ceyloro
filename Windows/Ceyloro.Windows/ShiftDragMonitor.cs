using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ceyloro.Windows;

internal sealed class ShiftDragMonitor : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseMove = 0x0200;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int SmCxDrag = 68;
    private const int SmCyDrag = 69;
    private const uint GaRoot = 2;

    private readonly LowLevelMouseProc hookProc;
    private readonly SynchronizationContext? syncContext;
    private readonly DragGestureTracker tracker;
    private IntPtr hookHandle = IntPtr.Zero;
    private bool disposed;

    public event Action<Point, RadialWheelMode>? GestureTriggered;
    public event Action? DragCompleted;

    public ShiftDragMonitor(SynchronizationContext? syncContext = null)
    {
        this.syncContext = syncContext ?? SynchronizationContext.Current;
        var threshold = Math.Max(GetSystemMetrics(SmCxDrag), 6);
        tracker = new DragGestureTracker(threshold);
        hookProc = HookCallback;
    }

    public bool IsActive => hookHandle != IntPtr.Zero;

    public bool Start()
    {
        if (hookHandle != IntPtr.Zero)
            return true;

        IntPtr moduleHandle = IntPtr.Zero;
        try
        {
            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            if (module?.ModuleName is not null)
                moduleHandle = GetModuleHandle(module.ModuleName);
        }
        catch
        {
            // Abaikan kegagalan akses process module
        }

        if (moduleHandle == IntPtr.Zero)
            moduleHandle = GetModuleHandle(null);

        hookHandle = SetWindowsHookEx(WhMouseLl, hookProc, moduleHandle, 0);
        if (hookHandle == IntPtr.Zero)
        {
            var errorCode = Marshal.GetLastWin32Error();
            AppLog.Write($"Gagal mendaftarkan low-level mouse hook (error {errorCode}).");
            return false;
        }

        AppLog.Write("ShiftDragMonitor aktif.");
        return true;
    }

    public void Stop()
    {
        if (hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
            tracker.Reset();
            AppLog.Write("ShiftDragMonitor dinonaktifkan.");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var pt = new Point(hookStruct.pt.x, hookStruct.pt.y);

            switch (message)
            {
                case WmLButtonDown:
                    var shiftDown = (GetAsyncKeyState(VkShift) & 0x8000) != 0;
                    var ctrlDown = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
                    var altDown = (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
                    var modifierDown = shiftDown || ctrlDown || altDown;
                    var isExplorer = modifierDown && IsExplorerWindowAt(hookStruct.pt);
                    var initialMode = ctrlDown ? RadialWheelMode.Tools : (altDown ? RadialWheelMode.Actions : RadialWheelMode.Convert);
                    tracker.OnMouseDown(pt, modifierDown, isExplorer, initialMode);
                    break;

                case WmMouseMove:
                    if (tracker.OnMouseMove(pt, out var triggerPoint))
                    {
                        var mode = tracker.Mode;
                        Dispatch(() => GestureTriggered?.Invoke(triggerPoint, mode));
                    }
                    break;

                case WmLButtonUp:
                    if (tracker.OnMouseUp())
                    {
                        Dispatch(() => DragCompleted?.Invoke());
                    }
                    break;
            }
        }

        return CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }

    private void Dispatch(Action action)
    {
        if (syncContext != null)
        {
            syncContext.Post(_ => action(), null);
        }
        else
        {
            Task.Run(action);
        }
    }

    public static bool IsExplorerWindowAt(POINT pt)
    {
        var hWnd = WindowFromPoint(pt);
        if (hWnd == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0)
            return false;

        var className = GetWindowClassName(hWnd);
        if (IsKnownExplorerClass(className))
            return true;

        var rootWnd = GetAncestor(hWnd, GaRoot);
        if (rootWnd != IntPtr.Zero && rootWnd != hWnd)
        {
            var rootClass = GetWindowClassName(rootWnd);
            if (IsKnownExplorerClass(rootClass))
                return true;
        }

        try
        {
            using var proc = Process.GetProcessById((int)processId);
            return string.Equals(proc.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKnownExplorerClass(string className)
    {
        return className is "CabinetWClass" or "Progman" or "WorkerW" or "DirectUIHWND" or "SysListView32" or "Shell_TrayWnd" or "ShellTabWindowClass" or "XamlExplorerHostIslandWindow";
    }

    private static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            Stop();
            disposed = true;
        }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
        public POINT(int x, int y) { this.x = x; this.y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

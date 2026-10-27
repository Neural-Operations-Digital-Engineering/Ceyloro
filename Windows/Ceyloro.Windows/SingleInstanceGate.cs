using System.Runtime.InteropServices;

namespace Ceyloro.Windows;

internal sealed class SingleInstanceGate : IDisposable
{
    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private static readonly uint ActivateMessage = RegisterWindowMessage($"Ceyloro.Activate.{Environment.UserName}");
    private readonly Mutex mutex;

    private SingleInstanceGate(Mutex mutex) => this.mutex = mutex;

    public static int ActivationMessage => unchecked((int)ActivateMessage);

    public static SingleInstanceGate? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, $"Local\\Ceyloro-{Environment.UserName}", out var createdNew);
        if (createdNew)
            return new SingleInstanceGate(mutex);

        mutex.Dispose();
        if (ActivateMessage != 0)
            PostMessage(HwndBroadcast, ActivateMessage, IntPtr.Zero, IntPtr.Zero);
        return null;
    }

    public void Dispose()
    {
        try { mutex.ReleaseMutex(); }
        finally { mutex.Dispose(); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}


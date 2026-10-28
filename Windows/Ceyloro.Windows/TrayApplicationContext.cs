using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace Ceyloro.Windows;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore settings = SettingsStore.Load();
    private readonly ConversionEngine engine;
    private readonly NotifyIcon trayIcon;
    private readonly HotKeyWindow hotKeyWindow;
    private readonly ShiftDragMonitor shiftDragMonitor;
    private readonly UpdateService updateService = new();
    private readonly Icon brandedIcon;
    private readonly SynchronizationContext? syncContext;
    private readonly string[]? initialStartupFiles;
    private RadialOverlayForm? overlay;
    private SettingsForm? settingsForm;

    public TrayApplicationContext(bool openOverlayOnStartup = false, string[]? initialFiles = null)
    {
        syncContext = SynchronizationContext.Current;
        initialStartupFiles = initialFiles;
        SingleInstanceGate.FilesReceived += OnFilesReceivedViaIpc;

        engine = new ConversionEngine(settings);
        brandedIcon = CreateTrayIcon();
        trayIcon = new NotifyIcon
        {
            Icon = brandedIcon,
            Text = "Ceyloro — Ctrl+Alt+Space untuk membuka roda",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        trayIcon.DoubleClick += (_, _) => OpenOverlay();
        hotKeyWindow = new HotKeyWindow(OpenOverlay);
        if (!hotKeyWindow.Register())
            NotificationService.ShowWarningToast("Ceyloro", "Hotkey Ctrl+Alt+Space tidak tersedia di sistem ini.");
        shiftDragMonitor = new ShiftDragMonitor(SynchronizationContext.Current);
        shiftDragMonitor.GestureTriggered += OnShiftDragGesture;
        shiftDragMonitor.DragCompleted += OnShiftDragCompleted;
        if (settings.Current.EnableShiftDragGesture)
        {
            if (!shiftDragMonitor.Start())
                NotificationService.ShowWarningToast("Ceyloro", "Gesture Shift-drag tidak dapat didaftarkan.");
        }
        if (settings.LoadWarning is not null)
        {
            AppLog.Write(settings.LoadWarning);
            NotificationService.ShowWarningToast("Ceyloro — Pengaturan", settings.LoadWarning);
        }
        if (openOverlayOnStartup)
            Application.Idle += OpenOverlayOnFirstIdle;

        // Daftar handler jika pengguna klik "Unduh & Pasang" dari toast update
        NotificationService.UpdateRequested += async (_, _) =>
        {
            await CheckForUpdatesAsync(userInitiated: true, skipPrompt: true);
        };

        if (settings.Current.AutoCheckUpdates)
            _ = Task.Run(async () =>
            {
                await Task.Delay(4000);
                await CheckForUpdatesAsync(userInitiated: false);
            });
    }

    private void OpenOverlayOnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OpenOverlayOnFirstIdle;
        if (initialStartupFiles != null && initialStartupFiles.Length > 0)
            OpenOverlayWithFiles(initialStartupFiles);
        else
            OpenOverlay();
    }

    private void OnFilesReceivedViaIpc(string[] files)
    {
        if (syncContext != null)
            syncContext.Post(_ => OpenOverlayWithFiles(files), null);
        else
            OpenOverlayWithFiles(files);
    }

    public void OpenOverlayWithFiles(string[] files, Point? cursorPosition = null)
    {
        var targetPoint = cursorPosition ?? Cursor.Position;
        if (overlay is { IsDisposed: false })
        {
            overlay.RepositionTo(targetPoint);
            overlay.PreloadFiles(files);
            overlay.Activate();
            return;
        }
        overlay = new RadialOverlayForm(engine, ShowMessage);
        overlay.FormClosed += (_, _) => overlay = null;
        overlay.RepositionTo(targetPoint);
        overlay.PreloadFiles(files);
        overlay.Show();
        overlay.Activate();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Buka roda", null, (_, _) => OpenOverlay());
        menu.Items.Add("Pengaturan…", null, (_, _) => OpenSettings());
        menu.Items.Add("Periksa pembaruan…", null, async (_, _) => await CheckForUpdatesAsync(userInitiated: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Keluar", null, (_, _) => ExitThread());
        return menu;
    }

    private async Task CheckForUpdatesAsync(bool userInitiated, bool skipPrompt = false)
    {
        try
        {
            var result = await updateService.CheckForUpdatesAsync();
            if (!result.HasUpdate)
            {
                if (userInitiated)
                    NotificationService.ShowInfoToast("Ceyloro", $"Sudah menggunakan versi terbaru (v{result.CurrentVersion}).");
                return;
            }

            if (skipPrompt)
            {
                // Dipanggil dari klik tombol toast → langsung unduh
                NotificationService.ShowInfoToast("Ceyloro", "Mengunduh pembaruan…");
                var zipPath = await updateService.DownloadAndVerifyAsync(result);
                NotificationService.ShowInfoToast("Ceyloro", "Memasang pembaruan dan memuat ulang…");
                await Task.Delay(1000);
                UpdateService.ApplyUpdateAndRestart(zipPath);
                return;
            }

            // Tampilkan toast dengan tombol interaktif
            NotificationService.ShowUpdateAvailableToast(result.CurrentVersion, result.LatestVersion);
        }
        catch (Exception error)
        {
            AppLog.Write($"Pemeriksaan pembaruan gagal: {error.Message}");
            if (userInitiated)
                NotificationService.ShowWarningToast("Ceyloro — Pembaruan Gagal", $"Gagal memeriksa atau mengunduh pembaruan:\n{error.Message}");
        }
    }

    private void OpenOverlay()
    {
        if (overlay is { IsDisposed: false })
        {
            overlay.Activate();
            return;
        }
        overlay = new RadialOverlayForm(engine, ShowMessage);
        overlay.FormClosed += (_, _) => overlay = null;
        overlay.Show();
    }

    private void OnShiftDragGesture(Point screenPoint, RadialWheelMode mode)
    {
        if (overlay is { IsDisposed: false })
        {
            if (overlay.CurrentMode != mode)
                overlay.SwitchMode(mode);
            overlay.RepositionTo(screenPoint);
            return;
        }
        overlay = new RadialOverlayForm(engine, ShowMessage, openedByGesture: true, initialMode: mode);
        overlay.FormClosed += (_, _) => overlay = null;
        overlay.RepositionTo(screenPoint);
        overlay.Show();
    }

    private async void OnShiftDragCompleted()
    {
        // Beri jeda singkat agar event OLE DragDrop di UI thread dapat dieksekusi terlebih dahulu
        // sebelum memeriksa apakah konversi sedang berjalan atau overlay harus ditutup.
        await Task.Delay(150);
        if (overlay is { IsDisposed: false, OpenedByGesture: true, IsConverting: false })
        {
            overlay.Close();
        }
    }

    private void UpdateShiftDragMonitorState()
    {
        if (settings.Current.EnableShiftDragGesture && !shiftDragMonitor.IsActive)
        {
            if (!shiftDragMonitor.Start())
                trayIcon.ShowBalloonTip(3000, "Ceyloro", "Gesture Shift-drag tidak dapat didaftarkan.", ToolTipIcon.Warning);
        }
        else if (!settings.Current.EnableShiftDragGesture && shiftDragMonitor.IsActive)
        {
            shiftDragMonitor.Stop();
        }
    }

    private void OpenSettings()
    {
        if (settingsForm is { IsDisposed: false })
        {
            settingsForm.Activate();
            return;
        }
        settingsForm = new SettingsForm(settings);
        settingsForm.FormClosed += (_, _) =>
        {
            settingsForm = null;
            UpdateShiftDragMonitorState();
        };
        settingsForm.Show();
        settingsForm.Activate();
    }

    private static void ShowMessage(string message)
    {
        NotificationService.ShowInfoToast("Ceyloro", message);
    }

    protected override void ExitThreadCore()
    {
        SingleInstanceGate.FilesReceived -= OnFilesReceivedViaIpc;
        shiftDragMonitor.Dispose();
        hotKeyWindow.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
        brandedIcon.Dispose();
        overlay?.Close();
        settingsForm?.Close();
        base.ExitThreadCore();
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var outline = new Pen(Color.FromArgb(180, 224, 92), 2.4f))
        using (var ray = new Pen(Color.White, 1.8f))
        using (var centerBrush = new SolidBrush(Color.FromArgb(180, 224, 92)))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            graphics.DrawEllipse(outline, 5, 5, 22, 22);
            for (var index = 0; index < 8; index++)
            {
                var angle = index * Math.PI / 4;
                var x = 16 + (float)Math.Cos(angle) * 4.5f;
                var y = 16 + (float)Math.Sin(angle) * 4.5f;
                var outerX = 16 + (float)Math.Cos(angle) * 10.2f;
                var outerY = 16 + (float)Math.Sin(angle) * 10.2f;
                graphics.DrawLine(ray, x, y, outerX, outerY);
            }
            graphics.FillEllipse(centerBrush, 12.5f, 12.5f, 7, 7);
        }
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

internal sealed class HotKeyWindow : NativeWindow, IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkSpace = 0x20;
    private const int HotKeyId = 0xCE71;
    private readonly Action action;
    private bool registered;

    public HotKeyWindow(Action action)
    {
        this.action = action;
        CreateHandle(new CreateParams());
    }

    public bool Register()
    {
        registered = RegisterHotKey(Handle, HotKeyId, ModControl | ModAlt, VkSpace);
        return registered;
    }

    protected override void WndProc(ref Message message)
    {
        if ((message.Msg == WmHotKey && message.WParam.ToInt32() == HotKeyId) || message.Msg == SingleInstanceGate.ActivationMessage)
            action();
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (registered)
            UnregisterHotKey(Handle, HotKeyId);
        DestroyHandle();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

}

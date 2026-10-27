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
    private RadialOverlayForm? overlay;
    private SettingsForm? settingsForm;

    public TrayApplicationContext(bool openOverlayOnStartup = false)
    {
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
            trayIcon.ShowBalloonTip(3000, "Ceyloro", "Hotkey Ctrl+Alt+Space tidak tersedia.", ToolTipIcon.Warning);
        shiftDragMonitor = new ShiftDragMonitor(SynchronizationContext.Current);
        shiftDragMonitor.GestureTriggered += OnShiftDragGesture;
        shiftDragMonitor.DragCompleted += OnShiftDragCompleted;
        if (settings.Current.EnableShiftDragGesture)
        {
            if (!shiftDragMonitor.Start())
                trayIcon.ShowBalloonTip(3000, "Ceyloro", "Gesture Shift-drag tidak dapat didaftarkan.", ToolTipIcon.Warning);
        }
        if (settings.LoadWarning is not null)
        {
            AppLog.Write(settings.LoadWarning);
            trayIcon.ShowBalloonTip(6000, "Ceyloro", settings.LoadWarning, ToolTipIcon.Warning);
        }
        if (openOverlayOnStartup)
            Application.Idle += OpenOverlayOnFirstIdle;

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
        OpenOverlay();
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

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        try
        {
            var result = await updateService.CheckForUpdatesAsync();
            if (!result.HasUpdate)
            {
                if (userInitiated)
                {
                    MessageBox.Show(
                        $"Ceyloro sudah menggunakan versi terbaru (v{result.CurrentVersion}).",
                        "Pembaruan Ceyloro",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            var prompt = $"Versi baru Ceyloro tersedia!\n\n" +
                         $"Versi terpasang: v{result.CurrentVersion}\n" +
                         $"Versi terbaru: v{result.LatestVersion}\n\n" +
                         (string.IsNullOrWhiteSpace(result.ReleaseNotes) ? string.Empty : $"Catatan Rilis:\n{result.ReleaseNotes}\n\n") +
                         $"Apakah Anda ingin mengunduh dan memasang pembaruan sekarang?";

            var choice = MessageBox.Show(prompt, "Pembaruan Ceyloro", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (choice != DialogResult.Yes)
                return;

            trayIcon.ShowBalloonTip(4000, "Ceyloro", "Mengunduh pembaruan…", ToolTipIcon.Info);
            var zipPath = await updateService.DownloadAndVerifyAsync(result);

            trayIcon.ShowBalloonTip(3000, "Ceyloro", "Memasang pembaruan dan memuat ulang…", ToolTipIcon.Info);
            await Task.Delay(1000);
            UpdateService.ApplyUpdateAndRestart(zipPath);
        }
        catch (Exception error)
        {
            AppLog.Write($"Pemeriksaan pembaruan gagal: {error.Message}");
            if (userInitiated)
            {
                MessageBox.Show(
                    $"Gagal memeriksa atau mengunduh pembaruan:\n{error.Message}",
                    "Pembaruan Ceyloro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
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

    private void OnShiftDragGesture(Point screenPoint)
    {
        if (overlay is { IsDisposed: false })
        {
            overlay.RepositionTo(screenPoint);
            return;
        }
        overlay = new RadialOverlayForm(engine, ShowMessage, openedByGesture: true);
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

    private void ShowMessage(string message)
    {
        trayIcon.ShowBalloonTip(5000, "Ceyloro", message, ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
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

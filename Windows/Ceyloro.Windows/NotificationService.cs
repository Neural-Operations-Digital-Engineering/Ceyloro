using System.Diagnostics;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Ceyloro.Windows;

/// <summary>
/// Mengirim Windows Action Center Toast Notifications native.
/// Menggantikan seluruh MessageBox.Show modal agar non-intrusif.
/// </summary>
public sealed class NotificationService
{
    private const string AppId = "Ceyloro.Windows";
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ceyloro", "Logs", "app.log");

    // -----------------------------------------------------------------
    // Public API: Completion toasts
    // -----------------------------------------------------------------

    /// <summary>
    /// Toast sukses setelah konversi/kompresi/tools selesai.
    /// Tombol: [Buka File] [Buka Folder]
    /// </summary>
    public static void ShowCompletionToast(string title, string body, IReadOnlyList<string> outputs)
    {
        var firstOutput = outputs.Count > 0 ? outputs[0] : string.Empty;
        var folder = firstOutput.Length > 0
            ? (Directory.Exists(firstOutput) ? firstOutput : Path.GetDirectoryName(firstOutput) ?? string.Empty)
            : string.Empty;

        var actionsXml = "";

        if (!string.IsNullOrWhiteSpace(firstOutput))
            actionsXml += $"<action content=\"Buka File\" arguments=\"action=openFile&amp;path={EscapeXml(firstOutput)}\" activationType=\"background\" />";

        if (!string.IsNullOrWhiteSpace(folder))
            actionsXml += $"<action content=\"Buka Folder\" arguments=\"action=openFolder&amp;path={EscapeXml(folder)}\" activationType=\"background\" />";

        var xml = $@"<toast duration=""short"" scenario=""default"">
  <visual>
    <binding template=""ToastGeneric"">
      <text hint-maxLines=""1"">{EscapeXml(title)}</text>
      <text>{EscapeXml(body)}</text>
    </binding>
  </visual>
  <actions>
    {actionsXml}
  </actions>
</toast>";

        SendToast(xml);
    }

    /// <summary>
    /// Toast konversi selesai dengan sebagian file gagal.
    /// Tombol: [Buka Folder] [Lihat Log]
    /// </summary>
    public static void ShowPartialFailureToast(string title, string body, string folder)
    {
        var folderAction = !string.IsNullOrWhiteSpace(folder)
            ? $"<action content=\"Buka Folder\" arguments=\"action=openFolder&amp;path={EscapeXml(folder)}\" activationType=\"background\" />"
            : string.Empty;

        var logAction = File.Exists(LogFilePath)
            ? $"<action content=\"Lihat Log\" arguments=\"action=openLog&amp;path={EscapeXml(LogFilePath)}\" activationType=\"background\" />"
            : string.Empty;

        var xml = $@"<toast duration=""long"" scenario=""default"">
  <visual>
    <binding template=""ToastGeneric"">
      <text hint-maxLines=""1"">{EscapeXml(title)}</text>
      <text>{EscapeXml(body)}</text>
    </binding>
  </visual>
  <actions>
    {folderAction}
    {logAction}
  </actions>
</toast>";

        SendToast(xml);
    }

    /// <summary>
    /// Toast informatif sederhana (pengganti showMessage/trayIcon.ShowBalloonTip).
    /// </summary>
    public static void ShowInfoToast(string title, string body)
    {
        var xml = $@"<toast duration=""short"">
  <visual>
    <binding template=""ToastGeneric"">
      <text hint-maxLines=""1"">{EscapeXml(title)}</text>
      <text>{EscapeXml(body)}</text>
    </binding>
  </visual>
</toast>";

        SendToast(xml);
    }

    /// <summary>
    /// Toast pembaruan tersedia — tombol [Unduh &amp; Pasang].
    /// </summary>
    public static void ShowUpdateAvailableToast(string currentVersion, string latestVersion)
    {
        var body = $"Versi baru tersedia: v{latestVersion} (terpasang: v{currentVersion}). Klik untuk mengunduh dan memasang sekarang.";
        var xml = $@"<toast duration=""long"" scenario=""reminder"">
  <visual>
    <binding template=""ToastGeneric"">
      <text hint-maxLines=""1"">Pembaruan Ceyloro Tersedia</text>
      <text>{EscapeXml(body)}</text>
    </binding>
  </visual>
  <actions>
    <action content=""Unduh &amp; Pasang"" arguments=""action=installUpdate"" activationType=""background"" />
    <action content=""Nanti"" arguments=""action=dismissUpdate"" activationType=""system"" hint-buttonStyle=""Critical"" />
  </actions>
</toast>";

        SendToast(xml);
    }

    /// <summary>
    /// Toast peringatan / error.
    /// </summary>
    public static void ShowWarningToast(string title, string body)
    {
        var logAction = File.Exists(LogFilePath)
            ? $"<action content=\"Lihat Log\" arguments=\"action=openLog&amp;path={EscapeXml(LogFilePath)}\" activationType=\"background\" />"
            : string.Empty;

        var xml = $@"<toast duration=""short"">
  <visual>
    <binding template=""ToastGeneric"">
      <text hint-maxLines=""1"">{EscapeXml(title)}</text>
      <text>{EscapeXml(body)}</text>
    </binding>
  </visual>
  <actions>
    {logAction}
  </actions>
</toast>";

        SendToast(xml);
    }

    // -----------------------------------------------------------------
    // Activation handler — dipanggil saat pengguna klik tombol toast
    // -----------------------------------------------------------------

    public static void HandleActivation(string arguments)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(arguments)) return;

            var pairs = arguments.Split('&');
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in pairs)
            {
                var idx = pair.IndexOf('=');
                if (idx > 0)
                    dict[pair[..idx]] = Uri.UnescapeDataString(pair[(idx + 1)..]);
            }

            if (!dict.TryGetValue("action", out var action)) return;

            switch (action)
            {
                case "openFile":
                    if (dict.TryGetValue("path", out var filePath) && File.Exists(filePath))
                        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                    break;

                case "openFolder":
                    if (dict.TryGetValue("path", out var folder) && Directory.Exists(folder))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
                    else if (dict.TryGetValue("path", out var fileForReveal) && File.Exists(fileForReveal))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fileForReveal}\"") { UseShellExecute = true });
                    break;

                case "openLog":
                    if (dict.TryGetValue("path", out var logPath) && File.Exists(logPath))
                        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{logPath}\"") { UseShellExecute = true });
                    break;

                case "installUpdate":
                    // Sinyal ke TrayApplicationContext untuk memulai pengunduhan update
                    UpdateRequested?.Invoke(null, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"NotificationService.HandleActivation error: {ex.Message}");
        }
    }

    /// <summary>Dipanggil jika pengguna mengklik tombol "Unduh &amp; Pasang" di toast.</summary>
    public static event EventHandler? UpdateRequested;

    // -----------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------

    private static void SendToast(string xml)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            var notification = new ToastNotification(doc);
            notification.Activated += (_, e) =>
            {
                if (e is ToastActivatedEventArgs args)
                    HandleActivation(args.Arguments);
            };
            notifier.Show(notification);
        }
        catch (Exception ex)
        {
            // Fallback: jika Action Center tidak tersedia, tulis ke log
            AppLog.Write($"NotificationService.SendToast fallback (toast tidak didukung): {ex.Message}");
        }
    }

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}


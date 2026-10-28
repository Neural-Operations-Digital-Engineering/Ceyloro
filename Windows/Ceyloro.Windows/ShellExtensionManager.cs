using Microsoft.Win32;

namespace Ceyloro.Windows;

/// <summary>
/// Mengelola pendaftaran menu konteks File Explorer (Windows 10 dan Windows 11).
/// Menggunakan registry tingkat pengguna (HKCU) tanpa memerlukan hak Administrator.
/// </summary>
public static class ShellExtensionManager
{
    private const string MenuTitle = "Buka dengan Roda Ceyloro";
    private static readonly string[] Subkeys = [@"*\shell\Ceyloro", @"Directory\shell\Ceyloro"];

    public static bool IsContextMenuRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\Ceyloro");
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public static void RegisterContextMenu(string? exePath = null)
    {
        try
        {
            var targetExe = exePath ?? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Ceyloro.exe");
            if (!File.Exists(targetExe))
                return;

            foreach (var sub in Subkeys)
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{sub}");
                if (key == null) continue;

                key.SetValue(string.Empty, MenuTitle);
                key.SetValue("Icon", $"\"{targetExe}\",0");

                using var cmdKey = key.CreateSubKey("command");
                if (cmdKey != null)
                {
                    cmdKey.SetValue(string.Empty, $"\"{targetExe}\" --files \"%1\"");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"Gagal mendaftarkan menu konteks: {ex.Message}");
        }
    }

    public static void UnregisterContextMenu()
    {
        try
        {
            foreach (var sub in Subkeys)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{sub}", throwOnMissingSubKey: false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"Gagal menghapus menu konteks: {ex.Message}");
        }
    }
}

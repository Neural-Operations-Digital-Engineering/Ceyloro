using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace Ceyloro.Windows;

public enum PathFormatStyle
{
    Standard,
    Quoted,
    Unix,
    Wsl
}

[ComImport]
[Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataTransferManagerInterop
{
    IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
    void ShowShareUIForWindow([In] IntPtr appWindow);
}

public static class FileActionsEngine
{
    public static readonly string[] AvailableActions =
    [
        "salin path",
        "terminal",
        "buka folder",
        "salin file",
        "potong file",
        "bagikan"
    ];

    public static bool ExecuteAction(string action, string[] files, out string feedbackMessage, IntPtr windowHandle = default, string? pathStyleSetting = null)
    {
        feedbackMessage = string.Empty;
        if (files.Length == 0)
        {
            feedbackMessage = "Tidak ada file yang dipilih.";
            return false;
        }

        try
        {
            switch (action.ToLowerInvariant())
            {
                case "salin path":
                    var style = ParsePathFormatStyle(pathStyleSetting);
                    var formattedList = files.Select(f => FormatPath(f, style)).ToArray();
                    var formattedPaths = string.Join(Environment.NewLine, formattedList);
                    Clipboard.SetText(formattedPaths);
                    feedbackMessage = files.Length == 1
                        ? $"Path disalin ke clipboard:\n{formattedList[0]}"
                        : $"{files.Length} path berkas disalin ke clipboard.";
                    return true;

                case "terminal":
                    var targetDir = Directory.Exists(files[0])
                        ? files[0]
                        : Path.GetDirectoryName(files[0]) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                    LaunchTerminal(targetDir);
                    feedbackMessage = $"Terminal dibuka di: {targetDir}";
                    return true;

                case "buka folder":
                    var first = files[0];
                    if (File.Exists(first))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{first}\"") { UseShellExecute = true });
                        feedbackMessage = $"Folder dibuka dan {Path.GetFileName(first)} disorot di Explorer.";
                    }
                    else if (Directory.Exists(first))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{first}\"") { UseShellExecute = true });
                        feedbackMessage = "Folder dibuka di Explorer.";
                    }
                    else
                    {
                        feedbackMessage = "Berkas tidak ditemukan.";
                        return false;
                    }
                    return true;

                case "salin file":
                    SetClipboardFiles(files, isMove: false);
                    feedbackMessage = files.Length == 1
                        ? $"Berkas disalin ke clipboard (Ctrl+V): {Path.GetFileName(files[0])}"
                        : $"{files.Length} berkas disalin ke clipboard (Ctrl+V).";
                    return true;

                case "potong file":
                    SetClipboardFiles(files, isMove: true);
                    feedbackMessage = files.Length == 1
                        ? $"Berkas dipotong ke clipboard (Ctrl+V untuk memindahkan): {Path.GetFileName(files[0])}"
                        : $"{files.Length} berkas dipotong ke clipboard.";
                    return true;

                case "bagikan":
                    return ShareFiles(windowHandle, files, out feedbackMessage);

                default:
                    feedbackMessage = $"Aksi '{action}' belum didukung.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            feedbackMessage = $"Gagal mengeksekusi aksi '{action}': {ex.Message}";
            AppLog.Write($"FileActionsEngine error: {ex}");
            return false;
        }
    }

    public static PathFormatStyle ParsePathFormatStyle(string? text) =>
        (text?.Trim().ToLowerInvariant()) switch
        {
            "quoted" => PathFormatStyle.Quoted,
            "unix" => PathFormatStyle.Unix,
            "wsl" => PathFormatStyle.Wsl,
            _ => PathFormatStyle.Standard
        };

    public static string FormatPath(string path, PathFormatStyle style)
    {
        var full = Path.GetFullPath(path);
        return style switch
        {
            PathFormatStyle.Quoted => $"\"{full}\"",
            PathFormatStyle.Unix => full.Replace('\\', '/'),
            PathFormatStyle.Wsl => ToWslPath(full),
            _ => full
        };
    }

    public static string ToWslPath(string windowsPath)
    {
        var full = Path.GetFullPath(windowsPath);
        if (full.Length >= 2 && full[1] == ':')
        {
            var drive = char.ToLowerInvariant(full[0]);
            var rest = full.Substring(2).Replace('\\', '/');
            return $"/mnt/{drive}{rest}";
        }
        return full.Replace('\\', '/');
    }

    public static void SetClipboardFiles(string[] files, bool isMove)
    {
        var data = new DataObject();
        var collection = new StringCollection();
        foreach (var f in files)
        {
            if (File.Exists(f) || Directory.Exists(f))
                collection.Add(Path.GetFullPath(f));
        }
        if (collection.Count == 0) return;

        data.SetFileDropList(collection);

        // Preferred DropEffect: 1 = DROPEFFECT_COPY, 2 = DROPEFFECT_MOVE
        var dropEffect = new byte[] { (byte)(isMove ? 2 : 1), 0, 0, 0 };
        data.SetData("Preferred DropEffect", new MemoryStream(dropEffect));
        Clipboard.SetDataObject(data, copy: true);
    }

    public static bool ShareFiles(IntPtr hwnd, string[] files, out string feedback)
    {
        try
        {
            if (hwnd != IntPtr.Zero)
            {
                var interop = DataTransferManager.As<IDataTransferManagerInterop>();
                var dtmIid = new Guid(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);
                var nativeptr = interop.GetForWindow(hwnd, ref dtmIid);
                var dtm = Marshal.GetObjectForIUnknown(nativeptr) as DataTransferManager;
                Marshal.Release(nativeptr);

                if (dtm != null)
                {
                    TypedEventHandler<DataTransferManager, DataRequestedEventArgs>? handler = null;
                    handler = (sender, args) =>
                    {
                        var request = args.Request;
                        request.Data.Properties.Title = files.Length == 1 ? Path.GetFileName(files[0]) : $"{files.Length} Berkas Ceyloro";
                        var storageFiles = new List<IStorageItem>();
                        foreach (var f in files)
                        {
                            if (File.Exists(f))
                                storageFiles.Add(StorageFile.GetFileFromPathAsync(f).AsTask().GetAwaiter().GetResult());
                            else if (Directory.Exists(f))
                                storageFiles.Add(StorageFolder.GetFolderFromPathAsync(f).AsTask().GetAwaiter().GetResult());
                        }
                        if (storageFiles.Count > 0)
                            request.Data.SetStorageItems(storageFiles);
                        else
                            request.Data.SetText(string.Join(Environment.NewLine, files));

                        dtm.DataRequested -= handler;
                    };

                    dtm.DataRequested += handler;
                    interop.ShowShareUIForWindow(hwnd);
                    feedback = "Membuka dialog Berbagi Windows...";
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"ShareFiles COM Interop warning: {ex.Message}");
        }

        // Fallback: buka antarmuka Nearby Sharing Windows
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:nearbysharing") { UseShellExecute = true });
            feedback = "Membuka Pengaturan Berbagi Windows (Nearby Sharing)...";
            return true;
        }
        catch
        {
            feedback = "Fitur Berbagi tidak didukung pada sistem ini.";
            return false;
        }
    }

    private static void LaunchTerminal(string directory)
    {
        // 1. Coba Windows Terminal (wt.exe)
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{directory}\"",
                UseShellExecute = true
            });
            return;
        }
        catch { }

        // 2. Coba PowerShell 7 (pwsh.exe)
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = $"-NoExit -WorkingDirectory \"{directory}\"",
                UseShellExecute = true
            });
            return;
        }
        catch { }

        // 3. Fallback Windows PowerShell bawaan
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"Set-Location -LiteralPath '{directory.Replace("'", "''")}'\"",
                UseShellExecute = true
            });
            return;
        }
        catch { }

        // 4. Fallback Command Prompt dasar
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/k cd /d \"{directory}\"",
            UseShellExecute = true
        });
    }
}

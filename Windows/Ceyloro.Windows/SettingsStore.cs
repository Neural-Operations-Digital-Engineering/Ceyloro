using System.Text.Json;

namespace Ceyloro.Windows;

public sealed class AppSettings
{
    public string OutputDirectory { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = string.Empty;
    public string LibreOfficePath { get; set; } = string.Empty;
    public string PandocPath { get; set; } = string.Empty;
    public int DocumentTimeoutSeconds { get; set; } = 180;
    public string MultiPageOutput { get; set; } = "folder";
    public string PdfPageSize { get; set; } = "a4";
    public string PdfMargin { get; set; } = "none";
    public string PdfQuality { get; set; } = "balanced";
    public string PdfMergeMode { get; set; } = "single";
    public double JpgQuality { get; set; } = 0.92;
    public double HeicQuality { get; set; } = 0.92;
    public bool AskPdfMergeEveryTime { get; set; } = true;
    public bool EnableShiftDragGesture { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public string PathFormatStyle { get; set; } = "standard";
}

public sealed class SettingsStore
{
    private readonly string filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Ceyloro",
        "settings.json");

    public AppSettings Current { get; private set; } = new();
    public string? LoadWarning { get; private set; }

    public static SettingsStore Load()
    {
        var store = new SettingsStore();
        try
        {
            if (File.Exists(store.filePath))
            {
                var json = File.ReadAllText(store.filePath);
                store.Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception error)
        {
            store.Current = new AppSettings();
            store.LoadWarning = $"Pengaturan lama tidak dapat dibaca. Pengaturan default digunakan. Detail: {error.Message}";
            try
            {
                var backup = store.filePath + $".corrupt-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}";
                File.Copy(store.filePath, backup, overwrite: false);
                store.LoadWarning += $" Salinan berkas rusak disimpan sebagai {Path.GetFileName(backup)}.";
            }
            catch { }
        }
        return store;
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        var temporary = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, json);
            if (File.Exists(filePath))
                File.Replace(temporary, filePath, null);
            else
                File.Move(temporary, filePath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }
}

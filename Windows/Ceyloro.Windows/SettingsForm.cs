using Microsoft.Win32;
using System.Diagnostics;

namespace Ceyloro.Windows;

public sealed class SettingsForm : Form
{
    private readonly SettingsStore store;
    private readonly TextBox outputBox = new();
    private readonly TextBox ffmpegBox = new();
    private readonly ComboBox pageSizeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox marginBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox qualityBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox multiPageBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown jpgQuality = new() { Minimum = 10, Maximum = 100, Increment = 5, Width = 86 };
    private readonly NumericUpDown heicQuality = new() { Minimum = 10, Maximum = 100, Increment = 5, Width = 86 };
    private readonly CheckBox startAtLogin = new() { Text = "Mulai saat login" };
    private readonly CheckBox askPdfMerge = new() { Text = "Tanyakan opsi saat menggabungkan beberapa gambar ke PDF" };
    private readonly CheckBox enableShiftDrag = new() { Text = "Buka roda saat Shift-drag dari Explorer" };
    private readonly CheckBox autoCheckUpdates = new() { Text = "Periksa pembaruan otomatis" };
    private readonly Label diagnosticsLabel = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 0) };

    public SettingsForm(SettingsStore store)
    {
        this.store = store;
        Text = "Pengaturan Ceyloro";
        ClientSize = new Size(660, 610);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        pageSizeBox.Items.AddRange(["A4", "Letter", "Sesuai gambar"]);
        marginBox.Items.AddRange(["Tanpa", "Sedang"]);
        qualityBox.Items.AddRange(["Seimbang", "Asli"]);
        multiPageBox.Items.AddRange(["Folder", "ZIP"]);
        LoadSettings();

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 14, 18, 8),
            ColumnCount = 3,
            RowCount = 13,
            AutoSize = false
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        AddRow(grid, 0, "Folder output", outputBox, BrowseOutput, 50);
        AddRow(grid, 1, "FFmpeg", ffmpegBox, BrowseFfmpeg, 50);
        AddRow(grid, 2, "Ukuran halaman PDF", pageSizeBox, null, 42);
        AddRow(grid, 3, "Margin PDF", marginBox, null, 42);
        AddRow(grid, 4, "Kualitas PDF", qualityBox, null, 42);
        AddRow(grid, 5, "Hasil multi-halaman", multiPageBox, null, 42);
        AddRow(grid, 6, "Kualitas JPG", MakeQualityControl(jpgQuality), null, 38);
        AddRow(grid, 7, "Kualitas HEIC", MakeQualityControl(heicQuality), null, 38);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.Controls.Add(startAtLogin, 0, 8);
        grid.SetColumnSpan(startAtLogin, 3);
        startAtLogin.Dock = DockStyle.Fill;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.Controls.Add(askPdfMerge, 0, 9);
        grid.SetColumnSpan(askPdfMerge, 3);
        askPdfMerge.Dock = DockStyle.Fill;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.Controls.Add(enableShiftDrag, 0, 10);
        grid.SetColumnSpan(enableShiftDrag, 3);
        enableShiftDrag.Dock = DockStyle.Fill;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.Controls.Add(autoCheckUpdates, 0, 11);
        grid.SetColumnSpan(autoCheckUpdates, 3);
        autoCheckUpdates.Dock = DockStyle.Fill;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        grid.Controls.Add(diagnosticsLabel, 0, 12);
        grid.SetColumnSpan(diagnosticsLabel, 3);
        diagnosticsLabel.Dock = DockStyle.Fill;
        Controls.Add(grid);

        var save = new Button { Text = "Simpan", AutoSize = true };
        var cancel = new Button { Text = "Batal", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => SaveSettings();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void LoadSettings()
    {
        var current = store.Current;
        outputBox.Text = current.OutputDirectory;
        ffmpegBox.Text = current.FfmpegPath;
        pageSizeBox.SelectedIndex = current.PdfPageSize.ToLowerInvariant() switch { "letter" => 1, "matchimage" => 2, _ => 0 };
        marginBox.SelectedIndex = current.PdfMargin.Equals("medium", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        qualityBox.SelectedIndex = current.PdfQuality.Equals("original", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        multiPageBox.SelectedIndex = current.MultiPageOutput.Equals("zip", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        jpgQuality.Value = ClampQuality(current.JpgQuality);
        heicQuality.Value = ClampQuality(current.HeicQuality);
        askPdfMerge.Checked = current.AskPdfMergeEveryTime;
        enableShiftDrag.Checked = current.EnableShiftDragGesture;
        autoCheckUpdates.Checked = current.AutoCheckUpdates;
        startAtLogin.Checked = IsStartAtLoginEnabled();

        var engines = DocumentEngineLocator.Locate(current);
        var ffmpegStatus = (!string.IsNullOrWhiteSpace(current.FfmpegPath) && File.Exists(current.FfmpegPath))
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")) ? "Tersedia" : "Tidak ditemukan";
        var loStatus = engines.LibreOfficePath != null ? "Tersedia" : "Tidak ditemukan";
        var pandocStatus = engines.PandocPath != null ? "Tersedia" : "Tidak ditemukan";
        diagnosticsLabel.Text = $"Status engine: FFmpeg ({ffmpegStatus}) • LibreOffice ({loStatus}) • Pandoc ({pandocStatus})";
    }

    private void SaveSettings()
    {
        try
        {
            ValidateOutputDirectory(outputBox.Text.Trim());
            ValidateFfmpeg(ffmpegBox.Text.Trim());
            SetStartAtLogin(startAtLogin.Checked);
            var current = store.Current;
            current.OutputDirectory = outputBox.Text.Trim();
            current.FfmpegPath = ffmpegBox.Text.Trim();
            current.PdfPageSize = pageSizeBox.SelectedIndex switch { 1 => "letter", 2 => "matchImage", _ => "a4" };
            current.PdfMargin = marginBox.SelectedIndex == 1 ? "medium" : "none";
            current.PdfQuality = qualityBox.SelectedIndex == 1 ? "original" : "balanced";
            current.MultiPageOutput = multiPageBox.SelectedIndex == 1 ? "zip" : "folder";
            current.JpgQuality = (double)jpgQuality.Value / 100;
            current.HeicQuality = (double)heicQuality.Value / 100;
            current.AskPdfMergeEveryTime = askPdfMerge.Checked;
            current.EnableShiftDragGesture = enableShiftDrag.Checked;
            current.AutoCheckUpdates = autoCheckUpdates.Checked;
            store.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Pengaturan Ceyloro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void AddRow(TableLayoutPanel grid, int rowIndex, string labelText, Control input, Action? browse, int height)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var label = new Label { Text = labelText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        input.Dock = DockStyle.Fill;
        grid.Controls.Add(label, 0, rowIndex);
        grid.Controls.Add(input, 1, rowIndex);
        if (browse is null)
        {
            grid.SetColumnSpan(input, 2);
            return;
        }
        var button = new Button { Text = "Pilih…", Dock = DockStyle.Fill };
        button.Click += (_, _) => browse();
        grid.Controls.Add(button, 2, rowIndex);
    }

    private static Control MakeQualityControl(NumericUpDown value)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        panel.Controls.Add(value);
        panel.Controls.Add(new Label { Text = "%", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        return panel;
    }

    private void BrowseOutput()
    {
        using var dialog = new FolderBrowserDialog { Description = "Pilih folder output" };
        if (dialog.ShowDialog(this) == DialogResult.OK) outputBox.Text = dialog.SelectedPath;
    }

    private void BrowseFfmpeg()
    {
        using var dialog = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe|Semua file|*.*", Title = "Pilih ffmpeg.exe" };
        if (dialog.ShowDialog(this) == DialogResult.OK) ffmpegBox.Text = dialog.FileName;
    }

    private static void ValidateOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        var probe = Path.Combine(fullPath, ".ceyloro-write-check-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { }
        File.Delete(probe);
    }

    private static void ValidateFfmpeg(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path)) throw new FileNotFoundException("ffmpeg.exe tidak ditemukan.", path);
        using var process = Process.Start(new ProcessStartInfo(path, "-version") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
        if (process is null) throw new InvalidOperationException("FFmpeg tidak dapat dijalankan.");
        process.WaitForExit(5000);
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("FFmpeg tidak merespons pemeriksaan versi.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("Berkas yang dipilih bukan FFmpeg yang dapat dijalankan.");
    }

    private static decimal ClampQuality(double value) => (decimal)Math.Clamp(Math.Round(value * 100), 10, 100);

    private static bool IsStartAtLoginEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
        return key?.GetValue("Ceyloro") is string;
    }

    private static void SetStartAtLogin(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? throw new InvalidOperationException("Pengaturan mulai saat login tidak dapat dibuka.");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Jalur executable Ceyloro tidak tersedia.");
            key.SetValue("Ceyloro", $"\"{executable}\"", RegistryValueKind.String);
        }
        else key.DeleteValue("Ceyloro", throwOnMissingValue: false);
    }
}

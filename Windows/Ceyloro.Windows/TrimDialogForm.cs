using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Ceyloro.Windows;

public sealed class TrimDialogForm : Form
{
    private readonly TextBox startBox;
    private readonly TextBox endBox;
    private readonly CheckBox losslessCheck;
    private readonly Label infoLabel;

    public TimeSpan StartTime { get; private set; }
    public TimeSpan EndTime { get; private set; }
    public bool LosslessMode => losslessCheck.Checked;

    public TrimDialogForm(string filePath, TimeSpan? defaultDuration = null)
    {
        Text = "Potong Media (Trim) — Ceyloro";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(390, 240);
        BackColor = Color.FromArgb(24, 24, 26);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);

        var titleLabel = new Label
        {
            Text = $"Potong: {Path.GetFileName(filePath)}",
            Location = new Point(20, 16),
            Size = new Size(350, 24),
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.FromArgb(255, 149, 0),
            AutoEllipsis = true
        };

        var startLabel = new Label
        {
            Text = "Waktu Mulai (hh:mm:ss):",
            Location = new Point(20, 52),
            Size = new Size(165, 20),
            ForeColor = Color.FromArgb(200, 200, 200)
        };

        startBox = new TextBox
        {
            Text = "00:00:00",
            Location = new Point(20, 74),
            Size = new Size(165, 26),
            BackColor = Color.FromArgb(36, 36, 38),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var endLabel = new Label
        {
            Text = "Waktu Selesai (hh:mm:ss):",
            Location = new Point(205, 52),
            Size = new Size(165, 20),
            ForeColor = Color.FromArgb(200, 200, 200)
        };

        var initialEnd = defaultDuration.HasValue && defaultDuration.Value.TotalSeconds > 1
            ? defaultDuration.Value
            : TimeSpan.FromSeconds(30);

        endBox = new TextBox
        {
            Text = initialEnd.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            Location = new Point(205, 74),
            Size = new Size(165, 26),
            BackColor = Color.FromArgb(36, 36, 38),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        losslessCheck = new CheckBox
        {
            Text = "Mode Lossless Copy (Sangat Cepat, Tanpa Re-encode)",
            Checked = true,
            Location = new Point(20, 114),
            Size = new Size(350, 24),
            ForeColor = Color.FromArgb(220, 220, 220)
        };

        infoLabel = new Label
        {
            Text = defaultDuration.HasValue
                ? $"Durasi berkas terdeteksi: {defaultDuration.Value:hh\\:mm\\:ss}"
                : "Format waktu: jam:menit:detik (contoh: 00:01:30)",
            Location = new Point(20, 142),
            Size = new Size(350, 20),
            ForeColor = Color.FromArgb(140, 140, 140),
            Font = new Font("Segoe UI", 8f)
        };

        var btnCancel = new Button
        {
            Text = "Batal",
            DialogResult = DialogResult.Cancel,
            Location = new Point(190, 185),
            Size = new Size(85, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(44, 44, 46),
            ForeColor = Color.White
        };
        btnCancel.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);

        var btnOk = new Button
        {
            Text = "Potong",
            Location = new Point(285, 185),
            Size = new Size(85, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 149, 0),
            ForeColor = Color.Black,
            Font = new Font("Segoe UI Semibold", 9f)
        };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.Click += OnOkClick;

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.AddRange([titleLabel, startLabel, startBox, endLabel, endBox, losslessCheck, infoLabel, btnCancel, btnOk]);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (!TryParseTime(startBox.Text, out var start))
        {
            MessageBox.Show(this, "Format Waktu Mulai tidak valid. Gunakan format hh:mm:ss atau detik.", "Ceyloro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            startBox.Focus();
            return;
        }

        if (!TryParseTime(endBox.Text, out var end))
        {
            MessageBox.Show(this, "Format Waktu Selesai tidak valid. Gunakan format hh:mm:ss atau detik.", "Ceyloro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            endBox.Focus();
            return;
        }

        if (end <= start)
        {
            MessageBox.Show(this, "Waktu Selesai harus lebih besar daripada Waktu Mulai.", "Ceyloro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            endBox.Focus();
            return;
        }

        StartTime = start;
        EndTime = end;
        DialogResult = DialogResult.OK;
        Close();
    }

    public static bool TryParseTime(string text, out TimeSpan time)
    {
        text = text.Trim();
        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out time))
            return true;

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
        {
            time = TimeSpan.FromSeconds(seconds);
            return true;
        }

        var parts = text.Split(':');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var m) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
        {
            time = TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s);
            return true;
        }

        time = TimeSpan.Zero;
        return false;
    }
}


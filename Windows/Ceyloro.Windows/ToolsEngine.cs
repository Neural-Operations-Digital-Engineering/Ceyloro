using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace Ceyloro.Windows;

public static class ToolsEngine
{
    public static string[] GetApplicableTools(string[] files, ConversionEngine engine)
    {
        if (files.Length == 0) return [];

        var tools = new List<string>();
        var formats = files.Select(PortableFileFormat.Detect).ToArray();

        // 1. Kompres selalu ditawarkan jika didukung oleh ConversionEngine
        if (engine.CanCompress(files))
        {
            tools.Add("kompres");
        }

        // 2. Video Mute & Snapshot
        if (formats.All(f => f.Kind == PortableMediaKind.Video))
        {
            tools.Add("mute");
            tools.Add("snapshot");
        }

        // 3. Trim untuk Video atau Audio (termasuk batch media campuran audio/video)
        if (formats.All(f => f.Kind is PortableMediaKind.Video or PortableMediaKind.Audio))
        {
            tools.Add("trim");
        }

        // 4. Ekstrak untuk arsip
        if (formats.All(f => f.Kind == PortableMediaKind.Archive))
        {
            tools.Add("ekstrak");
        }

        // 5. Split untuk dokumen PDF
        if (formats.All(f => f.Kind == PortableMediaKind.Pdf))
        {
            tools.Add("split");
        }

        return tools.ToArray();
    }

    public static string[] BuildMuteArguments(string input, string output) =>
        ["-y", "-i", input, "-c:v", "copy", "-an", output];

    public static async Task<string> ExecuteMuteAsync(string input, string outputDir, AppSettings settings, CancellationToken cancellationToken)
    {
        var baseName = Path.GetFileNameWithoutExtension(input);
        var ext = Path.GetExtension(input);
        var outputPath = UniqueOutputPath(outputDir, $"{baseName}-mute", ext.TrimStart('.'));

        var ffmpeg = ResolveFfmpeg(settings);
        var args = BuildMuteArguments(input, outputPath);

        await RunProcessAsync(ffmpeg, args, cancellationToken);
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            throw new InvalidOperationException("Gagal membungkam video (output tidak dihasilkan).");

        return outputPath;
    }

    public static string[] BuildSnapshotArguments(string input, string output, TimeSpan timestamp)
    {
        var timeStr = timestamp.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        return ["-y", "-ss", timeStr, "-i", input, "-frames:v", "1", "-q:v", "2", output];
    }

    public static async Task<string> ExecuteSnapshotAsync(string input, string outputDir, AppSettings settings, TimeSpan? timestamp, CancellationToken cancellationToken)
    {
        var baseName = Path.GetFileNameWithoutExtension(input);
        var outputPath = UniqueOutputPath(outputDir, $"{baseName}-snapshot", "png");

        var actualTimestamp = timestamp ?? TimeSpan.FromSeconds(1);
        if (!timestamp.HasValue)
        {
            var probed = await ProbeDurationAsync(input, settings, cancellationToken);
            if (probed.HasValue && probed.Value > 10.0)
            {
                actualTimestamp = TimeSpan.FromSeconds(probed.Value * 0.1);
            }
        }

        var ffmpeg = ResolveFfmpeg(settings);
        var args = BuildSnapshotArguments(input, outputPath, actualTimestamp);

        await RunProcessAsync(ffmpeg, args, cancellationToken);
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            throw new InvalidOperationException("Gagal mengambil snapshot frame video.");

        return outputPath;
    }

    public static string[] BuildTrimArguments(string input, string output, TimeSpan start, TimeSpan end, bool lossless)
    {
        var startStr = start.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        var endStr = end.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

        return lossless
            ? ["-y", "-ss", startStr, "-to", endStr, "-i", input, "-c", "copy", output]
            : ["-y", "-ss", startStr, "-to", endStr, "-i", input, output];
    }

    public static async Task<string> ExecuteTrimAsync(string input, string outputDir, TimeSpan start, TimeSpan end, AppSettings settings, bool lossless, CancellationToken cancellationToken)
    {
        var baseName = Path.GetFileNameWithoutExtension(input);
        var ext = Path.GetExtension(input);
        var outputPath = UniqueOutputPath(outputDir, $"{baseName}-trim", ext.TrimStart('.'));

        var ffmpeg = ResolveFfmpeg(settings);
        var args = BuildTrimArguments(input, outputPath, start, end, lossless);

        await RunProcessAsync(ffmpeg, args, cancellationToken);
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            throw new InvalidOperationException("Gagal memotong durasi media.");

        return outputPath;
    }

    public static Task<string> ExecuteExtractAsync(string input, string outputDir, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var baseName = Path.GetFileNameWithoutExtension(input);
            if (baseName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                baseName = Path.GetFileNameWithoutExtension(baseName);

            var targetFolder = Path.Combine(outputDir, baseName);
            for (var suffix = 2; Directory.Exists(targetFolder) || File.Exists(targetFolder); suffix++)
                targetFolder = Path.Combine(outputDir, $"{baseName} {suffix}");

            Directory.CreateDirectory(targetFolder);

            try
            {
                var format = PortableFileFormat.Detect(input);
                ArchiveConverter.Extract(input, format.Extension, targetFolder, cancellationToken);
                return targetFolder;
            }
            catch
            {
                try { if (Directory.Exists(targetFolder)) Directory.Delete(targetFolder, recursive: true); } catch { }
                throw;
            }
        }, cancellationToken);
    }

    public static List<int> ParsePageRange(string? rangeText, int maxPages)
    {
        if (maxPages <= 0) return [];
        if (string.IsNullOrWhiteSpace(rangeText) || rangeText.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return Enumerable.Range(1, maxPages).ToList();
        }

        var result = new HashSet<int>();
        var parts = rangeText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                var dashParts = trimmed.Split('-');
                if (dashParts.Length == 2 && int.TryParse(dashParts[0], out var start) && int.TryParse(dashParts[1], out var end))
                {
                    var lower = Math.Min(start, end);
                    var upper = Math.Max(start, end);
                    for (var p = lower; p <= upper; p++)
                    {
                        if (p >= 1 && p <= maxPages)
                            result.Add(p);
                    }
                }
            }
            else if (int.TryParse(trimmed, out var pageNum))
            {
                if (pageNum >= 1 && pageNum <= maxPages)
                    result.Add(pageNum);
            }
        }

        return result.OrderBy(p => p).ToList();
    }

    public static Task<string> ExecutePdfSplitAsync(string input, string outputDir, string? pageRange, CancellationToken cancellationToken, Action<double>? progress = null)
    {
        return Task.Run(() =>
        {
            var baseName = Path.GetFileNameWithoutExtension(input);
            var targetFolder = Path.Combine(outputDir, baseName);
            for (var suffix = 2; Directory.Exists(targetFolder) || File.Exists(targetFolder); suffix++)
                targetFolder = Path.Combine(outputDir, $"{baseName} {suffix}");

            Directory.CreateDirectory(targetFolder);

            try
            {
                using var sourceDoc = PdfDocument.Open(input);
                var pagesToExtract = ParsePageRange(pageRange, sourceDoc.NumberOfPages);
                if (pagesToExtract.Count == 0)
                    throw new InvalidOperationException("Tidak ada halaman yang dipilih atau rentang halaman tidak valid.");

                var width = Math.Max(sourceDoc.NumberOfPages, 1).ToString(CultureInfo.InvariantCulture).Length;
                width = Math.Max(width, 2);

                for (var i = 0; i < pagesToExtract.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pageNum = pagesToExtract[i];
                    var outFileName = $"{baseName}-{pageNum.ToString($"D{width}", CultureInfo.InvariantCulture)}.pdf";
                    var outFilePath = Path.Combine(targetFolder, outFileName);

                    var builder = new PdfDocumentBuilder();
                    builder.AddPage(sourceDoc, pageNum);
                    var bytes = builder.Build();
                    File.WriteAllBytes(outFilePath, bytes);

                    progress?.Invoke((double)(i + 1) / pagesToExtract.Count);
                }

                return targetFolder;
            }
            catch
            {
                try { if (Directory.Exists(targetFolder)) Directory.Delete(targetFolder, recursive: true); } catch { }
                throw;
            }
        }, cancellationToken);
    }

    public static async Task<double?> ProbeDurationAsync(string input, AppSettings settings, CancellationToken cancellationToken)
    {
        var probe = ResolveFfprobe(settings);
        if (!File.Exists(probe))
            return null;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = probe,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", input })
                process.StartInfo.ArgumentList.Add(argument);

            if (!process.Start())
                return null;

            using var reg = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
            });

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            await stderrTask;

            return process.ExitCode == 0 &&
                   double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) &&
                   double.IsFinite(duration) && duration > 0
                ? duration
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveFfmpeg(AppSettings settings)
    {
        var configured = settings.FfmpegPath.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(bundled))
            return bundled;
        return "ffmpeg.exe";
    }

    private static string ResolveFfprobe(AppSettings settings)
    {
        var ffmpeg = ResolveFfmpeg(settings);
        var dir = Path.GetDirectoryName(ffmpeg);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            var probePath = Path.Combine(dir, "ffprobe.exe");
            if (File.Exists(probePath))
                return probePath;
        }
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffprobe.exe");
        if (File.Exists(bundled))
            return bundled;
        return "ffprobe.exe";
    }

    private static async Task RunProcessAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var arg in arguments)
            process.StartInfo.ArgumentList.Add(arg);

        if (!process.Start())
            throw new InvalidOperationException($"Gagal menjalankan proses: {executable}");

        using var reg = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
            });

        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "Operasi tool media gagal." : stderr.Trim());
    }

    private static string UniqueOutputPath(string directory, string baseName, string extension)
    {
        var candidate = Path.Combine(directory, $"{baseName}.{extension}");
        for (var suffix = 2; File.Exists(candidate) || Directory.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{baseName} {suffix}.{extension}");
        return candidate;
    }
}

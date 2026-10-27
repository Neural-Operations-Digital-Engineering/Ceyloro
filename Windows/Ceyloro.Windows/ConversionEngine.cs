using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;

namespace Ceyloro.Windows;

public sealed record ConversionFailure(string Input, string Message);
public sealed record BatchResult(IReadOnlyList<string> Outputs, IReadOnlyList<ConversionFailure> Failures);

public sealed class ConversionEngine
{
    private readonly SettingsStore settings;
    private readonly object capabilityGate = new();
    private string? capabilityPath;
    private FfmpegCapabilities? cachedCapabilities;
    private Task<FfmpegCapabilities?>? capabilityTask;

    public ConversionEngine(SettingsStore settings)
    {
        this.settings = settings;
        StartCapabilityProbe();
    }

    public SettingsStore Settings => settings;
    public bool IsCheckingCapabilities => capabilityTask is { IsCompleted: false };

    public IReadOnlyList<string> TargetsFor(IEnumerable<string> paths)
    {
        var files = paths.ToArray();
        var targets = PortablePlanner.TargetsFor(files).ToList();
        if (files.Length == 0) return targets;
        var formats = files.Select(PortableFileFormat.Detect).ToArray();
        if (formats.All(format => format.Kind == PortableMediaKind.Document))
        {
            var engines = DocumentEngineLocator.Locate(settings.Current);
            return targets.Where(target => formats.All(format => DocumentConversion.CanConvert(format, target, engines))).ToArray();
        }
        if (formats.All(format => format.Kind is PortableMediaKind.Pdf or PortableMediaKind.Subtitle or PortableMediaKind.Archive))
            return targets;

        var capabilities = TryGetCapabilities();
        var hasAnimatedGif = formats.All(format => format.Extension == "gif") && files.Any(IsAnimatedGif);
        if (hasAnimatedGif)
        {
            targets.AddRange(["mp4", "mov", "mkv", "webm", "avi", "wmv", "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff"]);
            targets = targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        return targets.Where(target =>
        {
            if (target == "pdf" && formats.Any(format =>
                    format.Kind == PortableMediaKind.Image && !PdfConversion.CanCreatePdfFromImage(format.Extension)))
            {
                return capabilities?.Supports("png") == true && formats
                    .Where(format => format.Kind == PortableMediaKind.Image && !PdfConversion.CanCreatePdfFromImage(format.Extension))
                    .All(format => capabilities.CanDecodeImage(format.Extension));
            }
            var needsFfmpeg = formats.Any(format =>
                format.Kind is PortableMediaKind.Audio or PortableMediaKind.Video ||
                (format.Kind == PortableMediaKind.Image &&
                    (target == "heic" || ((target is "jpg" or "png" or "tiff") && !WindowsImageConverter.CanDecode(format.Extension)))));
            // B3: GIF animasi → target video/audio juga memerlukan FFmpeg dan harus diperiksa kapabilitasnya.
            if (hasAnimatedGif && !needsFfmpeg && target is not ("gif" or "png" or "pdf"))
                needsFfmpeg = true;
            return !needsFfmpeg || capabilities?.Supports(target) == true;
        }).ToArray();
    }

    private static bool IsAnimatedGif(string path)
    {
        try
        {
            using var image = Image.FromFile(path);
            var dimension = new System.Drawing.Imaging.FrameDimension(image.FrameDimensionsList[0]);
            return image.GetFrameCount(dimension) > 1;
        }
        catch { return false; }
    }

    public string EmptyStateFor(IEnumerable<string> paths)
    {
        var files = paths.ToArray();
        if (files.Length == 0) return "File tidak dapat dibaca";
        var formats = files.Select(PortableFileFormat.Detect).ToArray();
        if (formats.Any(format => !Enum.IsDefined(format.Kind) || format.Kind == PortableMediaKind.Unknown))
            return "Format file belum didukung";
        if (formats.All(format => format.Kind == PortableMediaKind.Document) && !DocumentEngineLocator.Locate(settings.Current).HasAny)
            return "Pasang LibreOffice atau pandoc untuk mengonversi dokumen";
        var needsFfmpeg = formats.Any(format => format.Kind is PortableMediaKind.Audio or PortableMediaKind.Video ||
            (format.Kind == PortableMediaKind.Image && !WindowsImageConverter.CanDecode(format.Extension)));
        if (needsFfmpeg && TryGetCapabilities() is null)
            return IsCheckingCapabilities
                ? "Memeriksa FFmpeg…"
                : "FFmpeg tidak ditemukan atau tidak dapat dijalankan";
        return "Format file belum didukung";
    }

    public async Task<BatchResult> ConvertAsync(
        IReadOnlyList<string> inputs,
        string target,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        PdfConversionOptions? pdfOptions = null,
        Action<string>? currentInputChanged = null)
    {
        var outputs = new List<string>();
        var failures = new List<ConversionFailure>();
        var total = Math.Max(inputs.Count, 1);

        for (var index = 0; index < inputs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                currentInputChanged?.Invoke(Path.GetFileName(inputs[index]));
                var fileOutputs = await ConvertOneAsync(inputs[index], target, cancellationToken,
                    value => progress?.Report(value < 0 ? -1 : (index + value) / total), pdfOptions);
                outputs.AddRange(fileOutputs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add(new ConversionFailure(inputs[index], error.Message));
            }
            progress?.Report((index + 1d) / total);
        }

        return new BatchResult(outputs, failures);
    }

    public bool CanCompress(IEnumerable<string> paths)
    {
        var files = paths.ToArray();
        if (files.Length == 0 || files.Any(path => !File.Exists(path))) return false;
        var formats = files.Select(PortableFileFormat.Detect).ToArray();
        if (formats.Any(format => !IsCompressible(format))) return false;
        var kind = formats[0].Kind;
        if (formats.Any(format => format.Kind != kind)) return false;
        if (kind is PortableMediaKind.Audio or PortableMediaKind.Video)
        {
            var capabilities = TryGetCapabilities();
            return capabilities is not null && formats.All(format => capabilities.Supports(format.Extension));
        }
        if (kind == PortableMediaKind.Image)
        {
            var capabilities = TryGetCapabilities();
            return files.Zip(formats).All(pair => pair.Second.Extension is "jpg" or "png" or "tiff"
                ? WindowsImageConverter.CanDecode(pair.Second.Extension) && !(pair.Second.Extension == "tiff" && WindowsImageConverter.FrameCount(pair.First, "tiff") > 1)
                : capabilities?.CanDecodeImage(pair.Second.Extension) == true && capabilities.Supports(pair.Second.Extension));
        }
        return true;
    }

    public async Task<BatchResult> CompressAsync(
        IReadOnlyList<string> inputs,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        Action<string>? currentInputChanged = null)
    {
        var outputs = new List<string>();
        var failures = new List<ConversionFailure>();
        var total = Math.Max(inputs.Count, 1);
        for (var index = 0; index < inputs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                currentInputChanged?.Invoke(Path.GetFileName(inputs[index]));
                var result = await CompressOneAsync(inputs[index], cancellationToken,
                    value => progress?.Report((index + Math.Clamp(value, 0, 1)) / total));
                outputs.Add(result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add(new ConversionFailure(inputs[index], error.Message));
            }
            progress?.Report((index + 1d) / total);
        }
        return new BatchResult(outputs, failures);
    }

    private async Task<string> CompressOneAsync(string input, CancellationToken cancellationToken, Action<double> reportProgress)
    {
        if (!File.Exists(input)) throw new FileNotFoundException("File sumber tidak ditemukan.", input);
        var source = PortableFileFormat.Detect(input);
        if (!IsCompressible(source)) throw new InvalidOperationException("Format file ini belum dapat dikompres.");
        var outputDirectory = ResolveOutputDirectory(input);
        Directory.CreateDirectory(outputDirectory);
        var outputExtension = source.Extension;
        var output = UniqueOutputPath(outputDirectory, Path.GetFileNameWithoutExtension(input) + " (terkompres)", outputExtension);
        var staging = Path.Combine(Path.GetTempPath(), "Ceyloro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var stagedOutput = Path.Combine(staging, "output." + outputExtension);
        try
        {
            switch (source.Kind)
            {
                case PortableMediaKind.Image:
                    if (WindowsImageConverter.CanDecode(source.Extension) && (source.Extension is "jpg" or "png" or "tiff"))
                    {
                        var settingsCopy = new AppSettings { JpgQuality = 0.78, HeicQuality = 0.72 };
                        await Task.Run(() => WindowsImageConverter.Convert(input, source.Extension, [stagedOutput], settingsCopy, cancellationToken, reportProgress, 0.78), cancellationToken);
                    }
                    else
                    {
                        await RunFfmpegAsync(input, stagedOutput, source.Extension, cancellationToken, reportProgress, compression: true);
                    }
                    break;
                case PortableMediaKind.Pdf:
                    await PdfConversion.CompressAsync(input, stagedOutput, cancellationToken, reportProgress);
                    break;
                case PortableMediaKind.Document:
                    await Task.Run(() => ZipDocument(input, stagedOutput, cancellationToken, reportProgress), cancellationToken);
                    break;
                case PortableMediaKind.Audio:
                case PortableMediaKind.Video:
                    await RunFfmpegAsync(input, stagedOutput, source.Extension, cancellationToken, reportProgress, compression: true);
                    break;
                default:
                    throw new InvalidOperationException("Format file ini belum dapat dikompres.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var inputInfo = new FileInfo(input);
            var outputInfo = new FileInfo(stagedOutput);
            if (outputInfo.Exists && outputInfo.Length >= inputInfo.Length)
            {
                TryDelete(stagedOutput);
                throw new InvalidOperationException("File ini sudah dalam ukuran paling optimal dan tidak dapat diperkecil lagi tanpa merusak kualitas.");
            }
            File.Move(stagedOutput, output);
            reportProgress(1);
            return output;
        }
        finally
        {
            TryDelete(stagedOutput);
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { }
        }
    }

    private static bool IsCompressible(PortableFileFormat format) => format.Kind switch
    {
        PortableMediaKind.Image => format.Extension is "jpg" or "png" or "tiff" or "gif" or "webp" or "heic" or "avif",
        PortableMediaKind.Pdf => true,
        PortableMediaKind.Document => true,
        PortableMediaKind.Audio => format.Extension is "mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus" or "aiff",
        PortableMediaKind.Video => format.Extension is "mp4" or "mov" or "mkv" or "webm" or "avi" or "wmv",
        _ => false
    };

    private static void ZipDocument(string input, string output, CancellationToken cancellationToken, Action<double> reportProgress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var staging = Path.Combine(Path.GetTempPath(), "CeyloroDocx", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            ZipFile.ExtractToDirectory(input, staging);
            var mediaDirs = Directory.GetDirectories(staging, "media", SearchOption.AllDirectories);
            foreach (var dir in mediaDirs)
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                    if (ext is "jpg" or "jpeg")
                    {
                        var tempJpg = file + ".opt";
                        using var img = Image.FromFile(file);
                        WindowsImageConverter.SaveJpeg(img, tempJpg, 0.75);
                        File.Delete(file);
                        File.Move(tempJpg, file);
                    }
                    else if (ext is "png")
                    {
                        var tempPng = file + ".opt";
                        using var img = Image.FromFile(file);
                        WindowsImageConverter.SaveQuantizedPng(img, tempPng);
                        File.Delete(file);
                        File.Move(tempPng, file);
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            ZipFile.CreateFromDirectory(staging, output, CompressionLevel.Optimal, includeBaseDirectory: false);
            reportProgress(1);
        }
        catch (InvalidDataException)
        {
            File.Copy(input, output, overwrite: true);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { }
        }
    }

    private async Task RunFfmpegAsync(string input, string output, string target, CancellationToken cancellationToken, Action<double> reportProgress, bool compression)
    {
        var ffmpeg = ResolveFfmpegPath();
        var capabilities = TryGetCapabilities();
        if (capabilities is null || !capabilities.Supports(target))
            throw new InvalidOperationException($"FFmpeg pada perangkat ini tidak memiliki encoder atau muxer untuk {target.ToUpperInvariant()}.");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in FfmpegCommandBuilder.Build(input, output, target, capabilities, settings.Current, compression))
            process.StartInfo.ArgumentList.Add(argument);
        var duration = await ProbeDurationAsync(input, cancellationToken);
        if (!process.Start()) throw new InvalidOperationException("FFmpeg tidak dapat dijalankan.");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        });
        var errorTask = process.StandardError.ReadToEndAsync();
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null) break;
            if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase) && duration is > 0 && long.TryParse(line[12..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var time))
                reportProgress(Math.Clamp(time / 1_000_000d / duration.Value, 0, 0.99));
        }
        await process.WaitForExitAsync();
        var error = await errorTask;
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "FFmpeg gagal mengompres file." : error.Trim());
        reportProgress(1);
    }

    public async Task<string> MergeImagesToPdfAsync(
        IReadOnlyList<string> inputs,
        string outputName,
        PdfConversionOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (inputs.Count == 0 || inputs.Any(path => PortableFileFormat.Detect(path).Kind != PortableMediaKind.Image))
            throw new InvalidOperationException("Pilih satu atau beberapa gambar untuk membuat PDF.");
        var outputDirectory = ResolveOutputDirectory(inputs[0]);
        Directory.CreateDirectory(outputDirectory);
        var cleanName = Path.GetFileNameWithoutExtension(outputName.Trim());
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Gabungan";
        var output = UniqueOutputPath(outputDirectory, cleanName, "pdf");
        await CreateImagePdfAsync(inputs, output, options, cancellationToken, value => progress?.Report(value));
        return output;
    }

    private async Task<IReadOnlyList<string>> ConvertOneAsync(
        string input,
        string target,
        CancellationToken cancellationToken,
        Action<double> reportProgress,
        PdfConversionOptions? pdfOptions)
    {
        if (!File.Exists(input))
            throw new FileNotFoundException("File sumber tidak ditemukan.", input);

        var outputDirectory = ResolveOutputDirectory(input);
        Directory.CreateDirectory(outputDirectory);
        var output = UniqueOutputPath(outputDirectory, Path.GetFileNameWithoutExtension(input), target);
        var source = PortableFileFormat.Detect(input);
        if (source.Kind == PortableMediaKind.Document)
        {
            await DocumentConversion.ConvertAsync(input, target, output, settings.Current, cancellationToken, reportProgress);
            return [output];
        }
        if (source.Kind == PortableMediaKind.Image && (target is "jpg" or "png" or "tiff") && WindowsImageConverter.CanDecode(source.Extension))
        {
            var frameCount = WindowsImageConverter.FrameCount(input, source.Extension);
            var outputPaths = frameCount == 1
                ? new[] { output }
                : Enumerable.Range(1, frameCount)
                    .Select(index => UniqueOutputPath(outputDirectory, $"{Path.GetFileNameWithoutExtension(input)} {index.ToString("D" + Math.Max(2, frameCount.ToString(CultureInfo.InvariantCulture).Length), CultureInfo.InvariantCulture)}", target))
                    .ToArray();
            var imageOutputs = await Task.Run(() => WindowsImageConverter.Convert(input, target, outputPaths, settings.Current, cancellationToken, reportProgress), cancellationToken);
            return imageOutputs;
        }
        if (source.Kind == PortableMediaKind.Image && (target is "jpg" or "png" or "tiff") && !WindowsImageConverter.CanDecode(source.Extension))
        {
            var ffmpegPath = ResolveFfmpegPath();
            var imageCapabilities = TryGetCapabilities();
            if (imageCapabilities is null || !imageCapabilities.CanDecodeImage(source.Extension) || !imageCapabilities.Supports("png"))
                throw new InvalidOperationException($"FFmpeg tidak dapat membaca gambar {source.Extension.ToUpperInvariant()} atau menyiapkan PNG perantara.");
            var staging = Path.Combine(Path.GetTempPath(), "Ceyloro", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var intermediate = Path.Combine(staging, "image.png");
                await ConvertImageAsync(input, intermediate, "png", ffmpegPath, imageCapabilities, cancellationToken);
                var converted = await Task.Run(() => WindowsImageConverter.Convert(intermediate, target,
                    [output], settings.Current, cancellationToken, reportProgress), cancellationToken);
                return converted;
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch { }
            }
        }
        var selectedPdfOptions = pdfOptions ?? new PdfConversionOptions(
            settings.Current.PdfPageSize,
            settings.Current.PdfMargin,
            settings.Current.PdfQuality,
            settings.Current.MultiPageOutput);
        if (source.Kind == PortableMediaKind.Subtitle)
        {
            var converted = await Task.Run(() => SubtitleConverter.Convert(input, target, cancellationToken, reportProgress), cancellationToken);
            await Task.Run(() => SubtitleConverter.Write(output, converted), cancellationToken);
            reportProgress(1);
            return [output];
        }
        if (source.Kind == PortableMediaKind.Archive)
        {
            await Task.Run(() => ArchiveConverter.Convert(input, target, output, cancellationToken, reportProgress), cancellationToken);
            return [output];
        }
        if (source.Kind == PortableMediaKind.Image && target == "pdf")
        {
            await CreateImagePdfAsync([input], output, selectedPdfOptions, cancellationToken, reportProgress);
            reportProgress(1);
            return [output];
        }
        if (source.Kind == PortableMediaKind.Pdf)
        {
            var outputs = await PdfConversion.ConvertPdfAsync(input, target, output, selectedPdfOptions, cancellationToken, reportProgress);
            return outputs.Count > 0 ? outputs : throw new InvalidOperationException("Konversi PDF tidak menghasilkan file.");
        }

        var ffmpeg = ResolveFfmpegPath();
        var capabilities = TryGetCapabilities();
        if (capabilities is null || !capabilities.Supports(target))
            throw new InvalidOperationException($"FFmpeg pada perangkat ini tidak memiliki encoder atau muxer yang diperlukan untuk {target.ToUpperInvariant()}.");

        // Tulis ke staging per-job terlebih dahulu; commit ke output final hanya setelah sukses.
        // Ini mencegah dua job dengan nama output sama saling menimpa (race condition A2).
        var jobStaging = Path.Combine(Path.GetTempPath(), "Ceyloro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobStaging);
        var extension = target.Equals("gzip", StringComparison.OrdinalIgnoreCase) ? "tar.gz" : target;
        var stagedOutput = Path.Combine(jobStaging, $"output.{extension}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in FfmpegCommandBuilder.Build(input, stagedOutput, target, capabilities, settings.Current))
            process.StartInfo.ArgumentList.Add(argument);

        var duration = await ProbeDurationAsync(input, cancellationToken);

        if (!process.Start())
            throw new InvalidOperationException("FFmpeg tidak dapat dijalankan.");

        try
        {
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // The process may already have exited between HasExited and Kill.
                }
            });
            cancellationToken.ThrowIfCancellationRequested();

            var errorTask = process.StandardError.ReadToEndAsync();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line is null)
                    break;
                if (line.Equals("progress=end", StringComparison.OrdinalIgnoreCase))
                    reportProgress(1);
                else if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
                {
                    if (duration is > 0 && long.TryParse(line[12..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var outputTime))
                        reportProgress(Math.Clamp(outputTime / 1_000_000d / duration.Value, 0, 0.99));
                    else
                        reportProgress(-1);
                }
            }

            await process.WaitForExitAsync();
            var errorOutput = await errorTask;
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorOutput)
                    ? "FFmpeg gagal mengonversi file."
                    : errorOutput.Trim());
            if (!File.Exists(stagedOutput))
                throw new InvalidOperationException("FFmpeg selesai tetapi file hasil tidak ditemukan.");

            // Commit atomik ke output final — UniqueOutputPath dipanggil tepat sebelum File.Move
            // agar jeda antara cek keberadaan file dan penulisan diminimalkan.
            var finalOutput = CommitStagedFile(stagedOutput, output);
            return [finalOutput];
        }
        finally
        {
            // Cleanup hanya menyentuh direktori staging job ini sendiri — tidak pernah menyentuh
            // file output final yang mungkin sudah berhasil di-commit oleh job ini atau job lain.
            try { if (Directory.Exists(jobStaging)) Directory.Delete(jobStaging, recursive: true); }
            catch { }
        }
    }

    private async Task CreateImagePdfAsync(
        IReadOnlyList<string> inputs,
        string output,
        PdfConversionOptions options,
        CancellationToken cancellationToken,
        Action<double> reportProgress)
    {
        var staging = Path.Combine(Path.GetTempPath(), "Ceyloro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var prepared = new List<string>(inputs.Count);
        try
        {
            var needsFfmpeg = inputs.Any(path => !PdfConversion.CanCreatePdfFromImage(PortableFileFormat.Detect(path).Extension));
            var ffmpeg = needsFfmpeg ? ResolveFfmpegPath() : null;
            var capabilities = needsFfmpeg ? TryGetCapabilities() : null;
            if (needsFfmpeg && (capabilities?.Supports("png") != true))
                throw new InvalidOperationException("FFmpeg tidak memiliki encoder PNG untuk menyiapkan gambar PDF.");

            for (var index = 0; index < inputs.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = inputs[index];
                var extension = PortableFileFormat.Detect(input).Extension;
                if (PdfConversion.CanCreatePdfFromImage(extension))
                {
                    prepared.Add(input);
                }
                else
                {
                    if (capabilities?.CanDecodeImage(extension) != true)
                        throw new InvalidOperationException($"FFmpeg tidak dapat membaca gambar {extension.ToUpperInvariant()} untuk PDF.");
                    var converted = Path.Combine(staging, $"image-{index + 1:D4}.png");
                    await ConvertImageAsync(input, converted, "png", ffmpeg!, capabilities, cancellationToken);
                    prepared.Add(converted);
                }
                reportProgress(0.35 * (index + 1d) / Math.Max(inputs.Count, 1));
            }

            await Task.Run(() => PdfConversion.CreateImagePdf(prepared, output, options, cancellationToken,
                value => reportProgress(0.35 + 0.65 * value)), cancellationToken);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { }
        }
    }

    private async Task ConvertImageAsync(
        string input,
        string output,
        string target,
        string ffmpeg,
        FfmpegCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        var arguments = FfmpegCommandBuilder.Build(input, output, target, capabilities, settings.Current);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("FFmpeg tidak dapat menyiapkan gambar untuk PDF.");
        try
        {
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
            });
            cancellationToken.ThrowIfCancellationRequested();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await stdout;
            var errorOutput = await stderr;
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorOutput)
                    ? "FFmpeg gagal membaca gambar untuk PDF."
                    : errorOutput.Trim());
            if (!File.Exists(output))
                throw new InvalidOperationException("FFmpeg tidak menghasilkan gambar sementara untuk PDF.");
        }
        catch
        {
            TryDelete(output);
            throw;
        }
    }

    private string ResolveFfmpegPath()
    {
        var configured = settings.Current.FfmpegPath.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(bundled))
            return bundled;
        throw new FileNotFoundException("FFmpeg belum tersedia. Pilih ffmpeg.exe di Pengaturan.");
    }

    private FfmpegCapabilities? TryGetCapabilities()
    {
        string path;
        try { path = ResolveFfmpegPath(); }
        catch { return null; }
        lock (capabilityGate)
        {
            if (!string.Equals(path, capabilityPath, StringComparison.OrdinalIgnoreCase))
            {
                capabilityPath = path;
                cachedCapabilities = null;
                capabilityTask = ProbeCapabilitiesAsync(path);
            }
            if (cachedCapabilities is not null)
                return cachedCapabilities;
            if (capabilityTask is not { IsCompleted: true })
                return null;
            try
            {
                cachedCapabilities = capabilityTask.GetAwaiter().GetResult();
                capabilityTask = null;
                return cachedCapabilities;
            }
            catch
            {
                capabilityTask = null;
                return null;
            }
        }
    }

    private void StartCapabilityProbe()
    {
        try
        {
            capabilityPath = ResolveFfmpegPath();
            capabilityTask = ProbeCapabilitiesAsync(capabilityPath);
        }
        catch
        {
            capabilityPath = null;
        }
    }

    private static Task<FfmpegCapabilities?> ProbeCapabilitiesAsync(string path) => Task.Run(() =>
    {
        try { return FfmpegCapabilities.Probe(path); }
        catch { return null; }
    });

    private async Task<double?> ProbeDurationAsync(string input, CancellationToken cancellationToken)
    {
        var configuredFfmpeg = settings.Current.FfmpegPath.Trim();
        var ffmpeg = !string.IsNullOrWhiteSpace(configuredFfmpeg) && File.Exists(configuredFfmpeg)
            ? configuredFfmpeg
            : Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? AppContext.BaseDirectory, "ffprobe.exe");
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
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
            });
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            await stderr;
            return process.ExitCode == 0 &&
                   double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) &&
                   double.IsFinite(duration) && duration > 0
                ? duration
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private string ResolveOutputDirectory(string input)
    {
        var configured = settings.Current.OutputDirectory.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? Path.GetDirectoryName(input) ?? Environment.CurrentDirectory
            : configured;
    }

    private static string UniqueOutputPath(string directory, string baseName, string target)
    {
        var extension = target.Equals("gzip", StringComparison.OrdinalIgnoreCase) ? "tar.gz" : target;
        var candidate = Path.Combine(directory, $"{baseName}.{extension}");
        for (var suffix = 2; File.Exists(candidate) || Directory.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{baseName} {suffix}.{extension}");
        return candidate;
    }

    /// <summary>
    /// Pindahkan file dari staging ke path final secara collision-safe.
    /// Jika path kandidat sudah ada (karena job lain commit lebih dulu), tambahkan suffix numerik.
    /// File.Move pada volume yang sama di Windows bersifat atomik.
    /// </summary>
    private static string CommitStagedFile(string stagingPath, string desiredFinalPath)
    {
        var directory = Path.GetDirectoryName(desiredFinalPath) ?? ".";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(desiredFinalPath);
        var ext = Path.GetExtension(desiredFinalPath);

        var finalPath = desiredFinalPath;
        for (var suffix = 2; File.Exists(finalPath) || Directory.Exists(finalPath); suffix++)
            finalPath = Path.Combine(directory, $"{nameWithoutExt} {suffix}{ext}");

        File.Move(stagingPath, finalPath);
        return finalPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Never hide the original conversion failure.
        }
    }
}


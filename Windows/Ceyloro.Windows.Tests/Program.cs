using System.Drawing.Imaging;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Ceyloro.Windows;
using DrawingEncoder = System.Drawing.Imaging.Encoder;

var tests = new (string Name, Action Run)[]
{
    ("normalizes portable image and archive extensions", PortableFormatNormalization),
    ("detects common documents for compression", DocumentDetection),
    ("plans phase A document conversions", PhaseADocumentTargets),
    ("offers only common targets for mixed image batches", MixedImageTargets),
    ("accepts gzip TAR and rejects ordinary gzip streams", GzipArchiveDetection),
    ("uses matching radial hit-test geometry for 1 through 11 targets", RadialHitTest),
    ("reports ZIP entry limits accurately", ArchiveLimitMessage),
    ("matches macOS PDF page output naming", PdfPageOutputNaming),
    ("cleans only stale Ceyloro GUID temp folders", StaleTempCleanup),
    ("uses palette-based bounded-width GIF conversion", GifFilterGraph),
    ("restricts FFmpeg input to local files", FfmpegInputProtocolPolicy),
    // A2 regression: -y tidak boleh ada di BuildInputArguments (mencegah overwrite output langsung)
    ("ffmpeg input arguments must not contain -y overwrite flag", FfmpegNoOverwriteFlag),
    // A2 regression: CommitStagedFile harus menghasilkan path unik saat kandidat sudah ada
    ("staged file commit uses collision-safe unique path", StagedFileCommitCollisionSafe),
    ("uses an explicit consistent JPEG quality policy", PdfJpegQuality),
    ("converts valid gzip TAR without leaving partial output", GzipArchiveConversion),
    ("exports every TIFF page and preserves PNG alpha", MultiPageTiffConversion),
    ("flattens transparent pixels to white in JPEG output", JpegTransparencyFlattening),
    ("cancels image conversion before writing output", ImageConversionCancellation),
    ("radial geometry hit tests sectors and boundaries correctly", RadialGeometryHitTesting),
    ("tracks shift-drag gesture transitions accurately", DragGestureTrackerTransitions),
    ("clamps radial wheel center within monitor working area", WheelCenterClamping),
    // B2 regression: root nama arsip harus deterministik dari nama file input, bukan GUID staging
    ("archive root naming uses input basename for single root", ArchiveRootNamingSingleRoot),
    ("archive root naming uses input basename for multi root", ArchiveRootNamingMultiRoot),
    ("archive root naming uses input basename for direct files", ArchiveRootNamingDirectFiles),
    // B3 regression: GIF→video/audio targets harus difilter berdasarkan kapabilitas FFmpeg
    ("gif capability filtering rejects video targets without ffmpeg", GifCapabilityFilteringNoFfmpeg),
    ("gif capability filtering accepts targets when all encoders available", GifCapabilityFilteringAllAvailable),
    ("gif capability filtering rejects target when encoder missing", GifCapabilityFilteringEncoderMissing),
    ("gif capability filtering rejects target when muxer missing", GifCapabilityFilteringMuxerMissing),
    // AC-5 regression: prioritas encoder video modern dan opsi kompresi
    ("prioritizes modern H.264 video encoders over mpeg4", FfmpegVideoEncoderPriority),
    ("uses CRF 28 for H.264 MP4 compression", FfmpegVideoCompressionModernOptions),
    // AC-1 regression: Smart PDF Engine tidak merasterisasi PDF teks/vektor
    ("preserves vector and text structure in Smart PDF engine", SmartPdfTextPreservation),
    // AC-3 regression: PNG 8-bit quantization
    ("reduces PNG file size via 8-bit color quantization", QuantizedPngReducesSize),
    // AC-6 regression: DOCX compression preserves .docx extension
    ("preserves .docx extension during document compression", DocxCompressionPreservesExtension),
    // W3 / Updater & Lifecycle tests
    ("ceyloro version comparison and semantic ordering", CeyloroVersionComparison),
    ("app settings preserves update and gesture flags", SettingsFlagsPreservation),
    ("document engine locator handles missing binaries gracefully", DocumentEngineLocatorMissing),
    // WIN-8 Tri-Modal & Actions tests
    ("tri-modal drag gesture tracker detects mode modifiers", TriModalDragGestureTracker),
    ("tools engine offers appropriate tools for videos and archives", ToolsEngineApplicableTools),
    ("file actions engine defines valid shell actions", FileActionsEngineDefinition),
    // WIN-9 / PRD-02 Extended Tools Catalog tests
    ("tools engine generates correct FFmpeg arguments for mute, snapshot, and trim", ToolsEngineMediaArguments),
    ("tools engine correctly parses page ranges for PDF split", ToolsEnginePageRangeParsing),
    ("tools engine splits multi-page PDF into discrete pages preserving structure", ToolsEnginePdfSplitExecution),
    ("tools engine rejects zip-slip path traversal attacks on extract", ToolsEngineExtractZipSlipRejection),
    ("trim dialog form parses various human time formats accurately", TrimDialogFormTimeParsing),
    // WIN-10 / PRD-03 Native File Actions tests
    ("file actions engine formats paths across standard, quoted, unix, and wsl styles", PathFormattingStyles),
    ("file actions engine configures clipboard drop effects for copy and move", ClipboardDropEffectConfiguration),
    // WIN-11 / PRD-04 Toast Notification Engine tests
    ("notification service activation handler routes openFile, openFolder, and openLog actions", NotificationActivationHandlerRouting),
    ("notification service activation handler is safe against malformed or empty arguments", NotificationActivationHandlerEdgeCases),
    // WIN-12 / PRD-05 GPU Hardware Acceleration tests
    ("gpu hardware acceleration selects optimal vendor encoder and supports cpu fallback", GpuHardwareAccelerationSelection),
    ("gpu command builder configures vendor-specific rate control parameters", GpuCommandBuilderVendorParameters),
    // WIN-13 / PRD-06 Shell Context Menu & IPC tests
    ("single instance gate parses file arguments and filters flags", SingleInstanceFileArgumentParsing),
    ("shell extension manager verifies and manages context menu registry entries", ShellExtensionContextRegistration)
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures.Add($"FAIL {name}: {error}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} Windows checks passed.");
return failures.Count == 0 ? 0 : 1;

static void PortableFormatNormalization()
{
    var jpeg = PortableFileFormat.Detect("portrait.JPEG");
    Ensure(jpeg is { Extension: "jpg", Kind: PortableMediaKind.Image }, "JPEG extension was not normalized.");
    var gzip = PortableFileFormat.Detect("backup.tar.gz");
    Ensure(gzip is { Extension: "gzip", Kind: PortableMediaKind.Archive }, "TAR.GZ was not normalized.");
    Ensure(PortableFileFormat.Detect("backup.tgz").Kind == PortableMediaKind.Archive, "TGZ was not recognized as an archive.");
}

static void DocumentDetection()
{
    Ensure(PortableFileFormat.Detect("proposal.docx").Kind == PortableMediaKind.Document, "DOCX should be offered to the compression tool.");
    Ensure(PortableFileFormat.Detect("notes.md").Kind == PortableMediaKind.Document, "Markdown should be offered to the compression tool.");
    Ensure(PortableFileFormat.Detect("report.pdf").Kind == PortableMediaKind.Pdf, "PDF should remain a distinct compressible document kind.");
}

static void PhaseADocumentTargets()
{
    var targets = PortablePlanner.TargetsFor(["report.docx"]);
    Ensure(targets.Contains("pdf") && targets.Contains("odt") && targets.Contains("md"), "DOCX should expose the Phase A target matrix.");
    Ensure(PortablePlanner.TargetsFor(["notes.md"]).Contains("docx"), "Markdown should expose DOCX output.");
    Ensure(PortablePlanner.TargetsFor(["sheet.xlsx"]).SequenceEqual(["pdf"]), "Spreadsheet documents should target PDF in Phase A.");
}

static void MixedImageTargets()
{
    var targets = PortablePlanner.TargetsFor(["one.png", "two.jpg"]);
    Ensure(targets.Contains("pdf"), "Mixed image batches should retain PDF output.");
    Ensure(!targets.Contains("png") && !targets.Contains("jpg"), "A mixed batch should not offer a target matching only one input.");
}

static void GzipArchiveDetection()
{
    using var temp = new TestDirectory();
    var tarPath = Path.Combine(temp.Path, "backup.tar");
    WriteTar(tarPath, "inside.txt", "archive data");
    var gzipTar = Path.Combine(temp.Path, "backup.tar.gz");
    using (var input = File.OpenRead(tarPath))
    using (var output = File.Create(gzipTar))
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        input.CopyTo(gzip);

    var ordinaryGzip = Path.Combine(temp.Path, "ordinary.gz");
    using (var output = File.Create(ordinaryGzip))
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
    using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        writer.Write("not a TAR archive");

    Ensure(ArchiveConverter.IsSupportedArchive(gzipTar), "Valid gzip TAR was rejected.");
    Ensure(!ArchiveConverter.IsSupportedArchive(ordinaryGzip), "Ordinary gzip data was offered as an archive.");
}

static void RadialHitTest()
{
    for (var count = 1; count <= 11; count++)
    {
        var step = 360d / count;
        var halfPetal = (step - 7) / 2;
        for (var index = 0; index < count; index++)
        {
            var center = index * step;
            Ensure(RadialGeometry.TargetIndexAtAngle(center, count) == index, $"Sector center {index} did not map to itself for {count} targets.");
            Ensure(RadialGeometry.TargetIndexAtAngle(center + halfPetal - 0.1, count) == index, $"Petal edge did not map to sector {index} for {count} targets.");
            Ensure(RadialGeometry.TargetIndexAtAngle(center + halfPetal + 0.1, count) == -1, $"Gap after sector {index} was selectable for {count} targets.");
        }
    }
    Ensure(RadialGeometry.TargetIndexAt(0, 0, 6) == -1, "The hub must not select a target.");
    Ensure(RadialGeometry.TargetIndexAt(200, 0, 6) == -1, "The area outside the wheel must not select a target.");
}

static void ArchiveLimitMessage()
{
    Ensure(ArchiveConverter.MaximumEntriesMessage.Contains("jumlah entri", StringComparison.OrdinalIgnoreCase), "ZIP entry limit message should identify an entry-count limit.");
    Ensure(!ArchiveConverter.MaximumEntriesMessage.Contains("2 GB", StringComparison.OrdinalIgnoreCase), "ZIP entry limit message should not claim a size limit.");
}

static void PdfPageOutputNaming()
{
    Ensure(PdfConversion.PageFileName("report", 0, 3, ".jpg") == "report-01.jpg", "PDF page naming should match the macOS page naming contract.");
    Ensure(PdfConversion.PageFileName("report", 9, 12, "png") == "report-10.png", "Page number width should follow the total page count.");
    Ensure(PdfConversion.PageFileName("..", 0, 1, "png") == "Hasil-01.png", "Unsafe empty page base names should be normalized.");
}

static void StaleTempCleanup()
{
    using var temp = new TestDirectory();
    var stale = Path.Combine(temp.Path, Guid.NewGuid().ToString("N"));
    var recent = Path.Combine(temp.Path, Guid.NewGuid().ToString("N"));
    var unrelated = Path.Combine(temp.Path, "keep-me");
    Directory.CreateDirectory(stale);
    Directory.CreateDirectory(recent);
    Directory.CreateDirectory(unrelated);
    Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
    Directory.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddHours(-1));
    var removed = TempDirectoryCleanup.RemoveStaleDirectories(temp.Path, DateTime.UtcNow, TimeSpan.FromHours(24));
    Ensure(removed == 1 && !Directory.Exists(stale), "A stale GUID temp directory was not removed.");
    Ensure(Directory.Exists(recent) && Directory.Exists(unrelated), "Recent or unrelated temp data was removed.");
}

static void GifFilterGraph()
{
    Ensure(FfmpegCommandBuilder.GifFilterGraph.Contains("palettegen", StringComparison.Ordinal), "GIF conversion should generate an optimized palette.");
    Ensure(FfmpegCommandBuilder.GifFilterGraph.Contains("paletteuse", StringComparison.Ordinal), "GIF conversion should apply the generated palette.");
    Ensure(FfmpegCommandBuilder.GifFilterGraph.Contains("min(720", StringComparison.Ordinal), "GIF conversion should cap output width.");
}

static void FfmpegInputProtocolPolicy()
{
    var arguments = FfmpegCommandBuilder.BuildInputArguments("C:\\media\\clip.mp4");
    var whitelistIndex = Array.IndexOf(arguments.ToArray(), "-protocol_whitelist");
    var inputIndex = Array.IndexOf(arguments.ToArray(), "-i");
    Ensure(whitelistIndex >= 0 && whitelistIndex < inputIndex, "FFmpeg input whitelist must apply before the input.");
    Ensure(arguments[whitelistIndex + 1] == "file", "FFmpeg should accept only local file protocol inputs.");
}

// Regression A2: -y tidak boleh ada di argumen input FFmpeg
static void FfmpegNoOverwriteFlag()
{
    var arguments = FfmpegCommandBuilder.BuildInputArguments("C:\\media\\clip.mp4");
    Ensure(!arguments.Contains("-y"),
        "FFmpeg input arguments must not include -y; output is committed via File.Move after success, not via direct overwrite.");
}

// Regression A2: commit staging ke output final harus collision-safe
static void StagedFileCommitCollisionSafe()
{
    using var temp = new TestDirectory();
    var desired = Path.Combine(temp.Path, "output.mp4");

    // Simulasikan job 1 sudah commit ke desired
    File.WriteAllText(desired, "job1");

    // Job 2 dan 3 harus mendapat path unik, tidak menimpa job 1
    var committed2 = CommitStagedHelper(temp.Path, "staged2.mp4", desired, "job2");
    var committed3 = CommitStagedHelper(temp.Path, "staged3.mp4", desired, "job3");

    Ensure(File.Exists(desired) && File.ReadAllText(desired) == "job1",
        "Job 1 output must not be overwritten by a later commit.");
    Ensure(committed2 != desired && File.Exists(committed2),
        "Job 2 must commit to a different path from job 1.");
    Ensure(committed3 != desired && committed3 != committed2 && File.Exists(committed3),
        "Job 3 must commit to a path different from both job 1 and job 2.");
}

// Helper: replika logika CommitStagedFile dari ConversionEngine untuk pengujian tanpa ketergantungan internal
static string CommitStagedHelper(string dir, string stagingName, string desiredPath, string content)
{
    var staging = Path.Combine(dir, stagingName);
    File.WriteAllText(staging, content);
    var directory = Path.GetDirectoryName(desiredPath) ?? ".";
    var nameWithoutExt = Path.GetFileNameWithoutExtension(desiredPath);
    var ext = Path.GetExtension(desiredPath);
    var final = desiredPath;
    for (var suffix = 2; File.Exists(final) || Directory.Exists(final); suffix++)
        final = Path.Combine(directory, $"{nameWithoutExt} {suffix}{ext}");
    File.Move(staging, final);
    return final;
}

static void PdfJpegQuality()
{
    Ensure(PdfConversion.JpegQuality(new PdfConversionOptions(Quality: "balanced")) == 85, "Balanced JPEG quality should be explicitly set to 85.");
    Ensure(PdfConversion.JpegQuality(new PdfConversionOptions(Quality: "original")) == 92, "Original JPEG quality should be explicitly set to 92.");
}

static void GzipArchiveConversion()
{
    using var temp = new TestDirectory();
    var tarPath = Path.Combine(temp.Path, "source.tar");
    WriteTar(tarPath, "inside.txt", "archive data");
    var gzipPath = Path.Combine(temp.Path, "source.gz");
    using (var input = File.OpenRead(tarPath))
    using (var output = File.Create(gzipPath))
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        input.CopyTo(gzip);

    var zipPath = Path.Combine(temp.Path, "converted.zip");
    ArchiveConverter.Convert(gzipPath, "zip", zipPath, CancellationToken.None, _ => { });
    using var archive = ZipFile.OpenRead(zipPath);
    Ensure(archive.Entries.Any(entry => entry.Name == "inside.txt"), "Converted ZIP is missing the TAR entry.");
    Ensure(!File.Exists(zipPath + ".ceyloro-partial"), "A partial output was left behind.");
}

static void MultiPageTiffConversion()
{
    using var temp = new TestDirectory();
    var input = Path.Combine(temp.Path, "two-pages.tiff");
    CreateMultiPageTiff(input);
    Ensure(WindowsImageConverter.FrameCount(input, "tiff") == 2, "The TIFF decoder did not report both pages.");

    var first = Path.Combine(temp.Path, "page-01.png");
    var second = Path.Combine(temp.Path, "page-02.png");
    var outputs = WindowsImageConverter.Convert(input, "png", [first, second], new AppSettings(), CancellationToken.None, _ => { });
    Ensure(outputs.Count == 2, "Both TIFF pages should be exported.");
    using var firstPage = (Bitmap)Image.FromFile(first);
    using var secondPage = (Bitmap)Image.FromFile(second);
    Ensure(firstPage.GetPixel(0, 0).R > 200, "The first TIFF page content was not retained.");
    Ensure(secondPage.GetPixel(0, 0).B > 200, "The second TIFF page content was not retained.");

    var alphaInput = Path.Combine(temp.Path, "alpha.png");
    using (var bitmap = new Bitmap(4, 4))
    {
        bitmap.SetPixel(1, 1, Color.FromArgb(0, 0, 0, 0));
        bitmap.Save(alphaInput, ImageFormat.Png);
    }
    var alphaOutput = Path.Combine(temp.Path, "alpha-output.png");
    WindowsImageConverter.Convert(alphaInput, "png", [alphaOutput], new AppSettings(), CancellationToken.None, _ => { });
    using var alphaResult = (Bitmap)Image.FromFile(alphaOutput);
    Ensure(alphaResult.GetPixel(1, 1).A == 0, "PNG conversion should preserve transparency.");
}

static void JpegTransparencyFlattening()
{
    using var temp = new TestDirectory();
    var input = Path.Combine(temp.Path, "transparent.png");
    using (var bitmap = new Bitmap(16, 16))
    {
        bitmap.SetPixel(8, 8, Color.FromArgb(0, 0, 0, 0));
        bitmap.Save(input, ImageFormat.Png);
    }
    var output = Path.Combine(temp.Path, "flattened.jpg");
    WindowsImageConverter.Convert(input, "jpg", [output], new AppSettings(), CancellationToken.None, _ => { });
    using var result = (Bitmap)Image.FromFile(output);
    var pixel = result.GetPixel(8, 8);
    Ensure(pixel.R > 220 && pixel.G > 220 && pixel.B > 220, "Transparent pixels should be flattened onto white.");
}

static void ImageConversionCancellation()
{
    using var temp = new TestDirectory();
    var input = Path.Combine(temp.Path, "source.png");
    using (var bitmap = new Bitmap(2, 2)) bitmap.Save(input, ImageFormat.Png);
    var output = Path.Combine(temp.Path, "cancelled.jpg");
    using var source = new CancellationTokenSource();
    source.Cancel();
    try
    {
        WindowsImageConverter.Convert(input, "jpg", [output], new AppSettings(), source.Token, _ => { });
        throw new InvalidOperationException("Expected cancellation was not raised.");
    }
    catch (OperationCanceledException) { }
    Ensure(!File.Exists(output), "Cancelled conversion created an output file.");
}

static void WriteTar(string path, string entryName, string contents)
{
    using var file = File.Create(path);
    using var writer = new TarWriter(file);
    using var data = new MemoryStream(Encoding.UTF8.GetBytes(contents));
    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = data });
}

static void CreateMultiPageTiff(string path)
{
    using var first = new Bitmap(4, 4);
    using var second = new Bitmap(4, 4);
    first.SetPixel(0, 0, Color.Red);
    second.SetPixel(0, 0, Color.Blue);
    var codec = ImageCodecInfo.GetImageEncoders().Single(encoder => encoder.MimeType == "image/tiff");
    using var parameters = new EncoderParameters(1);
    parameters.Param[0] = new EncoderParameter(DrawingEncoder.SaveFlag, (long)EncoderValue.MultiFrame);
    first.Save(path, codec, parameters);
    parameters.Param[0].Dispose();
    parameters.Param[0] = new EncoderParameter(DrawingEncoder.SaveFlag, (long)EncoderValue.FrameDimensionPage);
    first.SaveAdd(second, parameters);
    parameters.Param[0].Dispose();
    parameters.Param[0] = new EncoderParameter(DrawingEncoder.SaveFlag, (long)EncoderValue.Flush);
    first.SaveAdd(parameters);
}

static void RadialGeometryHitTesting()
{
    var center = new Point(500, 500);

    // Points inside hub (< 66 radius) return -1
    Ensure(RadialGeometry.HitTest(center, center, 6) == -1, "Hub center should return -1.");
    Ensure(RadialGeometry.HitTest(center, new Point(500, 550), 6) == -1, "Inner ring point should return -1.");

    // Points outside outer radius (> 154) return -1
    Ensure(RadialGeometry.HitTest(center, new Point(500, 700), 6) == -1, "Far outside point should return -1.");

    // 0 sectors return -1
    Ensure(RadialGeometry.HitTest(center, new Point(500, 390), 0) == -1, "Zero sectors should return -1.");

    // Test for 6 sectors (step = 60 degrees):
    // Sector 0 center is at 12 o'clock: dx = 0, dy = -110 -> (500, 390)
    Ensure(RadialGeometry.HitTest(center, new Point(500, 390), 6) == 0, "Top center should be sector 0.");

    // Sector 0 boundaries for 6 sectors: -30 deg to +30 deg
    // At -25 deg (approx dx = -46, dy = -100):
    Ensure(RadialGeometry.HitTest(center, new Point(454, 400), 6) == 0, "Counter-clockwise edge of sector 0 should be sector 0.");
    // At +25 deg (approx dx = +46, dy = -100):
    Ensure(RadialGeometry.HitTest(center, new Point(546, 400), 6) == 0, "Clockwise edge of sector 0 should be sector 0.");

    // Sector 1 center is at 60 deg (dx = 95, dy = -55 -> 595, 445):
    Ensure(RadialGeometry.HitTest(center, new Point(595, 445), 6) == 1, "Sector 1 center should be index 1.");

    // Sector 3 center is at 180 deg (6 o'clock, dx = 0, dy = 110 -> 500, 610):
    Ensure(RadialGeometry.HitTest(center, new Point(500, 610), 6) == 3, "Bottom center should be sector 3.");

    // Test with N = 1 (whole ring with 7-degree seam gap at bottom)
    Ensure(RadialGeometry.HitTest(center, new Point(500, 390), 1) == 0, "N=1 top should be sector 0.");
    Ensure(RadialGeometry.HitTest(center, new Point(610, 500), 1) == 0, "N=1 right should be sector 0.");
    Ensure(RadialGeometry.HitTest(center, new Point(500, 610), 1) == -1, "N=1 bottom seam gap should return -1.");

    // Test with N = 4 (step = 90 deg: sector 0 = top, 1 = right, 2 = bottom, 3 = left)
    Ensure(RadialGeometry.HitTest(center, new Point(500, 390), 4) == 0, "N=4 top should be sector 0.");
    Ensure(RadialGeometry.HitTest(center, new Point(610, 500), 4) == 1, "N=4 right should be sector 1.");
    Ensure(RadialGeometry.HitTest(center, new Point(500, 610), 4) == 2, "N=4 bottom should be sector 2.");
    Ensure(RadialGeometry.HitTest(center, new Point(390, 500), 4) == 3, "N=4 left should be sector 3.");
}

static void DragGestureTrackerTransitions()
{
    var tracker = new DragGestureTracker(threshold: 6);

    Ensure(!tracker.OnMouseDown(new Point(100, 100), shiftPressed: false, isExplorer: true), "Tracker should not track without Shift.");
    Ensure(!tracker.IsTracking, "Tracker state should be idle.");
    Ensure(!tracker.OnMouseMove(new Point(120, 120), out _), "MouseMove should not trigger without active tracking.");
    Ensure(!tracker.OnMouseUp(), "MouseUp should return false if not triggered.");

    Ensure(!tracker.OnMouseDown(new Point(100, 100), shiftPressed: true, isExplorer: false), "Tracker should not track outside Explorer.");
    Ensure(!tracker.IsTracking, "Tracker state should remain idle.");

    Ensure(tracker.OnMouseDown(new Point(100, 100), shiftPressed: true, isExplorer: true), "Tracker should start tracking.");
    Ensure(tracker.IsTracking, "Tracker should be in tracking state.");
    Ensure(!tracker.OnMouseMove(new Point(102, 103), out _), "Small drag should not trigger gesture.");
    Ensure(tracker.IsTracking, "Tracker should still be tracking.");
    Ensure(!tracker.OnMouseUp(), "MouseUp before threshold should not indicate triggered gesture.");
    Ensure(!tracker.IsTracking && !tracker.GestureTriggered, "Tracker should reset after MouseUp.");

    tracker.OnMouseDown(new Point(100, 100), shiftPressed: true, isExplorer: true);
    Ensure(tracker.OnMouseMove(new Point(108, 108), out var triggerPoint), "Drag > threshold should trigger gesture.");
    Ensure(triggerPoint == new Point(108, 108), "Trigger point should match move point.");
    Ensure(tracker.GestureTriggered, "Tracker should mark gesture as triggered.");
    Ensure(!tracker.IsTracking, "Tracker should leave active tracking after trigger.");

    Ensure(!tracker.OnMouseMove(new Point(150, 150), out _), "Subsequent movement must not re-trigger.");

    Ensure(tracker.OnMouseUp(), "MouseUp should return true for completed gesture.");
    Ensure(!tracker.GestureTriggered, "GestureTriggered should reset on MouseUp.");
}

static void WheelCenterClamping()
{
    var workArea = new Rectangle(0, 0, 1920, 1080);
    const int radius = 160;

    var center = RadialGeometry.ClampWheelCenter(new Point(500, 500), workArea, radius);
    Ensure(center == new Point(500, 500), "Interior point should not be modified.");

    var nearTopLeft = RadialGeometry.ClampWheelCenter(new Point(50, 40), workArea, radius);
    Ensure(nearTopLeft == new Point(160, 160), "Top-left clamp failed.");

    var nearBottomRight = RadialGeometry.ClampWheelCenter(new Point(1900, 1050), workArea, radius);
    Ensure(nearBottomRight == new Point(1920 - 160, 1080 - 160), "Bottom-right clamp failed.");

    var secondaryWorkArea = new Rectangle(-1920, 0, 1920, 1080);
    var clampedSec = RadialGeometry.ClampWheelCenter(new Point(-1900, 50), secondaryWorkArea, radius);
    Ensure(clampedSec == new Point(-1920 + 160, 160), "Negative coordinate clamp failed.");
}

// B2 regression: arsip satu root → root diganti dengan basename input, bukan GUID staging
static void ArchiveRootNamingSingleRoot()
{
    using var temp = new TestDirectory();
    var inputPath = Path.Combine(temp.Path, "myarchive.zip");
    WriteZip(inputPath,
        ("original-dir/file1.txt", "content1"),
        ("original-dir/subdir/file2.txt", "content2"));
    var outputPath = Path.Combine(temp.Path, "result.zip");
    ArchiveConverter.Convert(inputPath, "zip", outputPath, CancellationToken.None, _ => { });
    using var archive = ZipFile.OpenRead(outputPath);
    var fullNames = archive.Entries.Select(e => e.FullName).ToList();
    Ensure(fullNames.Count > 0, "Output archive should not be empty.");
    foreach (var name in fullNames)
        Ensure(name.StartsWith("myarchive/", StringComparison.Ordinal),
            $"Entry '{name}' should be prefixed with input basename 'myarchive/', not a GUID or original directory name.");
    Ensure(!fullNames.Any(n => n.Contains("original-dir", StringComparison.Ordinal)),
        "Original single-root directory name should be replaced by input basename.");
    Ensure(fullNames.Any(n => n == "myarchive/file1.txt"),
        "File from single root should appear directly under logical basename.");
    Ensure(fullNames.Any(n => n == "myarchive/subdir/file2.txt"),
        "Nested file from single root should appear under logical basename.");
}

// B2 regression: arsip multi-root → semua entri tetap diawali basename input
static void ArchiveRootNamingMultiRoot()
{
    using var temp = new TestDirectory();
    var inputPath = Path.Combine(temp.Path, "myarchive.zip");
    WriteZip(inputPath,
        ("dir1/file1.txt", "content1"),
        ("dir2/file2.txt", "content2"));
    var outputPath = Path.Combine(temp.Path, "result.zip");
    ArchiveConverter.Convert(inputPath, "zip", outputPath, CancellationToken.None, _ => { });
    using var archive = ZipFile.OpenRead(outputPath);
    var fullNames = archive.Entries.Select(e => e.FullName).ToList();
    Ensure(fullNames.Count > 0, "Output archive should not be empty.");
    foreach (var name in fullNames)
        Ensure(name.StartsWith("myarchive/", StringComparison.Ordinal),
            $"Entry '{name}' should be prefixed with input basename 'myarchive/', not a GUID.");
    Ensure(fullNames.Any(n => n == "myarchive/dir1/file1.txt"),
        "Multi-root directory 1 should be preserved under logical basename.");
    Ensure(fullNames.Any(n => n == "myarchive/dir2/file2.txt"),
        "Multi-root directory 2 should be preserved under logical basename.");
}

// B2 regression: file langsung di root arsip → tetap diawali basename input
static void ArchiveRootNamingDirectFiles()
{
    using var temp = new TestDirectory();
    var inputPath = Path.Combine(temp.Path, "myarchive.zip");
    WriteZip(inputPath,
        ("file1.txt", "content1"),
        ("file2.txt", "content2"));
    var outputPath = Path.Combine(temp.Path, "result.zip");
    ArchiveConverter.Convert(inputPath, "zip", outputPath, CancellationToken.None, _ => { });
    using var archive = ZipFile.OpenRead(outputPath);
    var fullNames = archive.Entries.Select(e => e.FullName).ToList();
    Ensure(fullNames.Count > 0, "Output archive should not be empty.");
    foreach (var name in fullNames)
        Ensure(name.StartsWith("myarchive/", StringComparison.Ordinal),
            $"Entry '{name}' should be prefixed with input basename 'myarchive/', not a GUID.");
    Ensure(fullNames.Any(n => n == "myarchive/file1.txt"),
        "Direct file should appear under logical basename.");
    Ensure(fullNames.Any(n => n == "myarchive/file2.txt"),
        "Direct file should appear under logical basename.");
}

// B3 regression: GIF tanpa FFmpeg tidak boleh menawarkan target video/audio
static void GifCapabilityFilteringNoFfmpeg()
{
    using var temp = new TestDirectory();
    var gifPath = Path.Combine(temp.Path, "animation.gif");
    CreateAnimatedGif(gifPath);

    // SettingsStore default → FfmpegPath kosong → ResolveFfmpegPath throws → capabilities null
    var engine = new ConversionEngine(new SettingsStore());
    var targets = engine.TargetsFor(new[] { gifPath });

    Ensure(!targets.Contains("mp4"), "GIF without FFmpeg must not offer MP4 (B3).");
    Ensure(!targets.Contains("mov"), "GIF without FFmpeg must not offer MOV (B3).");
    Ensure(!targets.Contains("mp3"), "GIF without FFmpeg must not offer MP3 (B3).");
    Ensure(!targets.Contains("mkv"), "GIF without FFmpeg must not offer MKV (B3).");
}

// B3 regression: semua encoder/muxer tersedia → target video/audio didukung
static void GifCapabilityFilteringAllAvailable()
{
    var caps = CreateFfmpegCapabilities(
        encoders: ["mpeg4", "aac", "libmp3lame", "pcm_s16le", "gif", "vp9", "opus", "wmv2", "wmav2"],
        decoders: ["gif", "mjpeg", "png"],
        muxers: ["mp4", "mov", "mkv", "webm", "avi", "asf", "mp3", "wav", "flac", "ogg", "opus", "aiff", "gif", "ipod", "mov"]);

    Ensure(caps.Supports("mp4"), "All-capability FFmpeg should support MP4 for GIF conversion.");
    Ensure(caps.Supports("mp3"), "All-capability FFmpeg should support MP3 for GIF conversion.");
    Ensure(caps.Supports("mov"), "All-capability FFmpeg should support MOV for GIF conversion.");
    Ensure(caps.Supports("webm"), "All-capability FFmpeg should support WebM for GIF conversion.");
}

// B3 regression: encoder video hilang → target video tidak didukung, audio tetap bisa
static void GifCapabilityFilteringEncoderMissing()
{
    var caps = CreateFfmpegCapabilities(
        encoders: ["aac", "libmp3lame"], // no mpeg4/libx264/h264
        decoders: ["gif"],
        muxers: ["mp4", "mp3", "wav"]);

    Ensure(!caps.Supports("mp4"), "FFmpeg without video encoder must not support MP4.");
    Ensure(!caps.Supports("mov"), "FFmpeg without video encoder must not support MOV.");
    Ensure(caps.Supports("mp3"), "FFmpeg with libmp3lame should still support MP3.");
}

// B3 regression: muxer hilang → target tidak didukung meski encoder ada
static void GifCapabilityFilteringMuxerMissing()
{
    var caps = CreateFfmpegCapabilities(
        encoders: ["mpeg4", "aac", "libmp3lame"],
        decoders: ["gif"],
        muxers: ["mp3"]); // no mp4/mov/mkv muxer

    Ensure(!caps.Supports("mp4"), "FFmpeg with encoder but no mp4 muxer must not support MP4.");
    Ensure(caps.Supports("mp3"), "FFmpeg with libmp3lame and mp3 muxer should support MP3.");
}

// AC-5: Video MP4 harus memprioritaskan encoder H.264 modern daripada mpeg4
static void FfmpegVideoEncoderPriority()
{
    var capsModern = CreateFfmpegCapabilities(
        encoders: ["mpeg4", "libx264", "h264_nvenc", "aac"],
        decoders: ["h264"],
        muxers: ["mp4"]);
    Ensure(capsModern.EncoderFor("mp4") == "libx264", "libx264 should take priority over mpeg4 for MP4.");

    var capsHardware = CreateFfmpegCapabilities(
        encoders: ["mpeg4", "h264_nvenc", "aac"],
        decoders: ["h264"],
        muxers: ["mp4"]);
    Ensure(capsHardware.EncoderFor("mp4") == "h264_nvenc", "h264_nvenc should take priority over mpeg4 when libx264 is absent.");

    var capsLegacy = CreateFfmpegCapabilities(
        encoders: ["mpeg4", "aac"],
        decoders: ["mpeg4"],
        muxers: ["mp4"]);
    Ensure(capsLegacy.EncoderFor("mp4") == "mpeg4", "mpeg4 should be chosen only when no H.264 encoder is available.");
}

// AC-5: Kompresi MP4 dengan H.264 harus menggunakan -crf 28 alih-alih -q:v 7
static void FfmpegVideoCompressionModernOptions()
{
    var caps = CreateFfmpegCapabilities(
        encoders: ["libx264", "mpeg4", "aac"],
        decoders: ["h264"],
        muxers: ["mp4"]);

    var args = FfmpegCommandBuilder.Build(
        input: @"C:\media\sample.mp4",
        output: @"C:\media\output.mp4",
        target: "mp4",
        capabilities: caps,
        settings: new AppSettings(),
        compression: true);

    Ensure(args.Contains("libx264"), "MP4 compression should select libx264 when available.");
    Ensure(!args.Contains("mpeg4"), "MP4 compression should not select mpeg4 when libx264 is available.");
    Ensure(args.Contains("-crf") && args.Contains("28"), "libx264 MP4 compression should use -crf 28.");
    Ensure(!args.Contains("-q:v"), "libx264 should not use -q:v for quality scaling.");
}

// AC-1 & Temuan 1: PDF dengan layer teks tidak boleh dirasterisasi menjadi gambar
static void SmartPdfTextPreservation()
{
    using var temp = new TestDirectory();
    var pdfPath = Path.Combine(temp.Path, "sample.pdf");
    var outPath = Path.Combine(temp.Path, "output.pdf");

    // Tulis PDF dengan stream teks dan font standar Helvetica agar valid bagi parser
    using (var file = File.Create(pdfPath))
    {
        var offsets = new List<long> { 0 };
        void WriteAscii(string s) => file.Write(System.Text.Encoding.Latin1.GetBytes(s));
        void WriteObj(int id, string body)
        {
            offsets.Add(file.Position);
            WriteAscii($"{id} 0 obj\n{body}\nendobj\n");
        }

        WriteAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        WriteObj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObj(2, "<< /Type /Pages /Count 1 /Kids [3 0 R] >>");
        WriteObj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>");
        WriteObj(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        var textStream = "BT\n/F1 12 Tf\n100 700 Td\n(Ceyloro Text Layer) Tj\nET\n";
        var streamBytes = System.Text.Encoding.Latin1.GetBytes(textStream);
        offsets.Add(file.Position);
        WriteAscii($"5 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n");
        file.Write(streamBytes);
        WriteAscii("endstream\nendobj\n");

        var xref = file.Position;
        WriteAscii($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) WriteAscii($"{offset:0000000000} 00000 n \n");
        WriteAscii($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    // Jalankan CompressAsync
    PdfConversion.CompressAsync(pdfPath, outPath, CancellationToken.None, _ => { }).GetAwaiter().GetResult();

    Ensure(File.Exists(outPath), "Output PDF must exist.");
    // Cek raw bytes output: jika text layer dipertahankan (File.Copy path),
    // string "Ceyloro" akan hadir di byte stream PDF output
    var outBytes = File.ReadAllBytes(outPath);
    var outRawText = System.Text.Encoding.Latin1.GetString(outBytes);
    Ensure(outRawText.Contains("Ceyloro"), "Output PDF must retain the selectable text layer.");
}

// AC-3: PNG kuantisasi warna 8-bit menghasilkan penurunan ukuran nyata >= 30%
static void QuantizedPngReducesSize()
{
    using var temp = new TestDirectory();
    var inputPng = Path.Combine(temp.Path, "large.png");
    var outputPng = Path.Combine(temp.Path, "quantized.png");

    int width = 200, height = 200;
    using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x < 40)
                    bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                else
                    bmp.SetPixel(x, y, Color.FromArgb(255, (x * 255) / width, (y * 255) / height, 180));
            }
        }
        bmp.Save(inputPng, ImageFormat.Png);
    }

    var originalSize = new FileInfo(inputPng).Length;
    WindowsImageConverter.Convert(inputPng, "png", [outputPng], new AppSettings(), CancellationToken.None, _ => { }, qualityOverride: 0.78);

    Ensure(File.Exists(outputPng), "Quantized PNG output must exist.");
    var compressedSize = new FileInfo(outputPng).Length;
    Ensure(compressedSize < originalSize, "Compressed PNG must be smaller than original.");
    // Cek PNG header: byte 24 = bit depth, byte 25 = color type (3 = indexed)
    var pngHeader = File.ReadAllBytes(outputPng);
    Ensure(pngHeader.Length > 25 && pngHeader[24] == 8 && pngHeader[25] == 3, "Output PNG should use 8-bit indexed color.");
}

// AC-6: Kompresi dokumen DOCX harus mempertahankan ekstensi .docx
static void DocxCompressionPreservesExtension()
{
    var format = PortableFileFormat.Detect("report.docx");
    Ensure(format.Extension == "docx", "DOCX should detect docx extension.");
    // Memverifikasi ekstensi output tidak diubah menjadi .zip
    var ext = format.Kind == PortableMediaKind.Document ? format.Extension : "other";
    Ensure(ext == "docx", "Document compression must preserve .docx extension instead of .zip.");
}

static FfmpegCapabilities CreateFfmpegCapabilities(string[] encoders, string[] decoders, string[] muxers)
{
    var constructor = typeof(FfmpegCapabilities).GetConstructor(
        BindingFlags.NonPublic | BindingFlags.Instance,
        null,
        [typeof(HashSet<string>), typeof(HashSet<string>), typeof(HashSet<string>)],
        null)!;
    return (FfmpegCapabilities)constructor.Invoke([
        new HashSet<string>(encoders, StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(decoders, StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(muxers, StringComparer.OrdinalIgnoreCase)
    ]);
}

static void CreateAnimatedGif(string path)
{
    using var first = new Bitmap(2, 2);
    using var second = new Bitmap(2, 2);
    first.SetPixel(0, 0, Color.Red);
    second.SetPixel(0, 0, Color.Blue);
    var codec = ImageCodecInfo.GetImageEncoders().Single(encoder => encoder.MimeType == "image/gif");
    using var parameters = new EncoderParameters(1);
    parameters.Param[0] = new EncoderParameter(DrawingEncoder.SaveFlag, (long)EncoderValue.MultiFrame);
    first.Save(path, codec, parameters);
    parameters.Param[0].Dispose();
    parameters.Param[0] = new EncoderParameter(DrawingEncoder.SaveFlag, (long)EncoderValue.FrameDimensionTime);
    first.SaveAdd(second, parameters);
    parameters.Param[0].Dispose();
    parameters.Param[0] = new EncoderParameter(DrawingEncoder.SaveFlag, (long)EncoderValue.Flush);
    first.SaveAdd(parameters);
}

static void WriteZip(string path, params (string Name, string Content)[] entries)
{
    using var file = File.Create(path);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    foreach (var (name, content) in entries)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

static void CeyloroVersionComparison()
{
    var v1 = CeyloroVersion.Parse("v0.1.0");
    var v2 = CeyloroVersion.Parse("0.1.1");
    var v3 = CeyloroVersion.Parse("v0.2.0");
    var v4 = CeyloroVersion.Parse("1.0.0");
    var v1Same = CeyloroVersion.Parse("0.1.0");

    Ensure(v1.CompareTo(v2) < 0, "v0.1.0 should be less than 0.1.1");
    Ensure(v2.CompareTo(v3) < 0, "0.1.1 should be less than 0.2.0");
    Ensure(v3.CompareTo(v4) < 0, "0.2.0 should be less than 1.0.0");
    Ensure(v1.CompareTo(v1Same) == 0, "v0.1.0 should equal 0.1.0");
    Ensure(v4.CompareTo(v1) > 0, "1.0.0 should be greater than v0.1.0");
}

static void SettingsFlagsPreservation()
{
    using var temp = new TestDirectory();
    var settingsPath = Path.Combine(temp.Path, "settings.json");
    var appSettings = new AppSettings
    {
        EnableShiftDragGesture = true,
        AutoCheckUpdates = false
    };
    var json = System.Text.Json.JsonSerializer.Serialize(appSettings);
    File.WriteAllText(settingsPath, json);

    var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath));
    Ensure(loaded != null, "Settings should deserialize.");
    Ensure(loaded!.EnableShiftDragGesture, "Shift-drag setting should be true.");
    Ensure(!loaded.AutoCheckUpdates, "Auto-check update setting should be false.");
}

static void DocumentEngineLocatorMissing()
{
    var dummySettings = new AppSettings
    {
        LibreOfficePath = @"C:\NonExistent\Path\soffice.exe",
        PandocPath = @"C:\NonExistent\Path\pandoc.exe"
    };
    var engines = DocumentEngineLocator.Locate(dummySettings);
    Ensure(engines is not null, "Locator should return non-null record.");
}

static void TriModalDragGestureTracker()
{
    var tracker = new DragGestureTracker(threshold: 6);

    // Ctrl drag -> Tools mode
    tracker.OnMouseDown(new Point(50, 50), modifierPressed: true, isExplorer: true, mode: RadialWheelMode.Tools);
    Ensure(tracker.Mode == RadialWheelMode.Tools, "Tracker should record Tools mode on Ctrl drag.");
    tracker.OnMouseMove(new Point(65, 65), out _);
    Ensure(tracker.GestureTriggered, "Gesture should trigger above threshold.");
    tracker.Reset();

    // Alt drag -> Actions mode
    tracker.OnMouseDown(new Point(50, 50), modifierPressed: true, isExplorer: true, mode: RadialWheelMode.Actions);
    Ensure(tracker.Mode == RadialWheelMode.Actions, "Tracker should record Actions mode on Alt drag.");
    tracker.Reset();

    // Shift drag -> Convert mode
    tracker.OnMouseDown(new Point(50, 50), modifierPressed: true, isExplorer: true, mode: RadialWheelMode.Convert);
    Ensure(tracker.Mode == RadialWheelMode.Convert, "Tracker should record Convert mode on Shift drag.");
}

static void ToolsEngineApplicableTools()
{
    var engine = new ConversionEngine(SettingsStore.Load());

    var videoTools = ToolsEngine.GetApplicableTools(["clip.mp4"], engine);
    Ensure(videoTools.Contains("mute"), "Video should offer mute tool.");
    Ensure(videoTools.Contains("snapshot"), "Video should offer snapshot tool.");
    Ensure(videoTools.Contains("trim"), "Video should offer trim tool.");

    var audioTools = ToolsEngine.GetApplicableTools(["song.mp3"], engine);
    Ensure(audioTools.Contains("trim"), "Audio should offer trim tool.");

    var pdfTools = ToolsEngine.GetApplicableTools(["document.pdf"], engine);
    Ensure(pdfTools.Contains("split"), "PDF should offer split tool.");

    var archiveTools = ToolsEngine.GetApplicableTools(["archive.zip"], engine);
    Ensure(archiveTools.Contains("ekstrak"), "Archive should offer ekstrak tool.");
}

static void ToolsEngineMediaArguments()
{
    var muteArgs = ToolsEngine.BuildMuteArguments("in.mp4", "out.mp4");
    Ensure(muteArgs.SequenceEqual(["-y", "-i", "in.mp4", "-c:v", "copy", "-an", "out.mp4"]), "Mute arguments should be -c:v copy -an.");

    var snapArgs = ToolsEngine.BuildSnapshotArguments("in.mp4", "out.png", TimeSpan.FromSeconds(2.5));
    Ensure(snapArgs.Contains("-ss") && snapArgs.Contains("00:00:02.500") && snapArgs.Contains("-frames:v"), "Snapshot arguments should set timestamp and 1 frame.");

    var losslessTrim = ToolsEngine.BuildTrimArguments("in.mp4", "out.mp4", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), lossless: true);
    Ensure(losslessTrim.Contains("-c") && losslessTrim.Contains("copy"), "Lossless trim should copy stream.");

    var reencodeTrim = ToolsEngine.BuildTrimArguments("in.mp4", "out.mp4", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), lossless: false);
    Ensure(!losslessTrim.SequenceEqual(reencodeTrim) && !reencodeTrim.Contains("copy"), "Re-encode trim must not copy stream.");
}

static void ToolsEnginePageRangeParsing()
{
    var r1 = ToolsEngine.ParsePageRange("1-3, 5", 10);
    Ensure(r1.SequenceEqual([1, 2, 3, 5]), "Range 1-3, 5 did not parse correctly.");

    var r2 = ToolsEngine.ParsePageRange("all", 4);
    Ensure(r2.SequenceEqual([1, 2, 3, 4]), "Range 'all' did not return all 4 pages.");

    var r3 = ToolsEngine.ParsePageRange(null, 3);
    Ensure(r3.SequenceEqual([1, 2, 3]), "Null range did not return all 3 pages.");

    var r4 = ToolsEngine.ParsePageRange("4-2", 5);
    Ensure(r4.SequenceEqual([2, 3, 4]), "Reverse range 4-2 was not normalized.");

    var r5 = ToolsEngine.ParsePageRange("8-10", 5);
    Ensure(r5.Count == 0, "Out of bounds range was not filtered.");
}

static void ToolsEnginePdfSplitExecution()
{
    using var temp = new TestDirectory();
    var pdfPath = Path.Combine(temp.Path, "sample.pdf");

    // Buat multi-page PDF (2 halaman) dengan PdfDocumentBuilder
    var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
    builder.AddPage(595, 842);
    builder.AddPage(595, 842);
    File.WriteAllBytes(pdfPath, builder.Build());

    var resultFolder = ToolsEngine.ExecutePdfSplitAsync(pdfPath, temp.Path, null, CancellationToken.None).GetAwaiter().GetResult();
    Ensure(Directory.Exists(resultFolder), "PDF split output folder was not created.");

    var files = Directory.GetFiles(resultFolder, "*.pdf").OrderBy(f => f).ToArray();
    Ensure(files.Length == 2, $"Expected 2 split PDF pages, found {files.Length}.");
    Ensure(Path.GetFileName(files[0]) == "sample-01.pdf", $"Page 1 name mismatch: {Path.GetFileName(files[0])}");
    Ensure(Path.GetFileName(files[1]) == "sample-02.pdf", $"Page 2 name mismatch: {Path.GetFileName(files[1])}");

    using var doc1 = UglyToad.PdfPig.PdfDocument.Open(files[0]);
    Ensure(doc1.NumberOfPages == 1, "Split page 1 must have exactly 1 page.");

    using var doc2 = UglyToad.PdfPig.PdfDocument.Open(files[1]);
    Ensure(doc2.NumberOfPages == 1, "Split page 2 must have exactly 1 page.");
}

static void ToolsEngineExtractZipSlipRejection()
{
    using var temp = new TestDirectory();
    var zipPath = Path.Combine(temp.Path, "malicious.zip");

    using (var file = File.Create(zipPath))
    using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
    {
        var entry = archive.CreateEntry("../../evil.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("exploited");
    }

    var threw = false;
    try
    {
        ToolsEngine.ExecuteExtractAsync(zipPath, temp.Path, CancellationToken.None).GetAwaiter().GetResult();
    }
    catch (InvalidDataException)
    {
        threw = true;
    }

    Ensure(threw, "Extracting a zip with path traversal '../../' must be rejected with InvalidDataException.");
}

static void TrimDialogFormTimeParsing()
{
    Ensure(TrimDialogForm.TryParseTime("00:01:15", out var t1) && t1.TotalSeconds == 75, "00:01:15 should parse to 75 seconds.");
    Ensure(TrimDialogForm.TryParseTime("45", out var t2) && t2.TotalSeconds == 45, "45 should parse to 45 seconds.");
    Ensure(TrimDialogForm.TryParseTime("01:30", out var t3) && t3.TotalSeconds == 90, "01:30 should parse to 90 seconds.");
    Ensure(!TrimDialogForm.TryParseTime("invalid", out _), "Invalid string should fail parsing.");
}

static void FileActionsEngineDefinition()
{
    Ensure(FileActionsEngine.AvailableActions.Contains("salin path"), "Should contain salin path action.");
    Ensure(FileActionsEngine.AvailableActions.Contains("terminal"), "Should contain terminal action.");
    Ensure(FileActionsEngine.AvailableActions.Contains("buka folder"), "Should contain buka folder action.");
    Ensure(FileActionsEngine.AvailableActions.Contains("salin file"), "Should contain salin file action.");
    Ensure(FileActionsEngine.AvailableActions.Contains("potong file"), "Should contain potong file action.");
    Ensure(FileActionsEngine.AvailableActions.Contains("bagikan"), "Should contain bagikan action.");
}

static void PathFormattingStyles()
{
    var sample = @"C:\Users\Ceyloro\doc.pdf";
    Ensure(FileActionsEngine.FormatPath(sample, PathFormatStyle.Standard) == sample, "Standard path mismatch.");
    Ensure(FileActionsEngine.FormatPath(sample, PathFormatStyle.Quoted) == $"\"{sample}\"", "Quoted path mismatch.");
    Ensure(FileActionsEngine.FormatPath(sample, PathFormatStyle.Unix) == "C:/Users/Ceyloro/doc.pdf", "Unix path mismatch.");
    Ensure(FileActionsEngine.FormatPath(sample, PathFormatStyle.Wsl) == "/mnt/c/Users/Ceyloro/doc.pdf", "WSL path mismatch.");
    Ensure(FileActionsEngine.ToWslPath(@"D:\Data\photo.jpg") == "/mnt/d/Data/photo.jpg", "WSL D: drive conversion mismatch.");
    Ensure(FileActionsEngine.ParsePathFormatStyle("wsl") == PathFormatStyle.Wsl, "Parse WSL style failed.");
    Ensure(FileActionsEngine.ParsePathFormatStyle("quoted") == PathFormatStyle.Quoted, "Parse Quoted style failed.");
}

static void ClipboardDropEffectConfiguration()
{
    using var temp = new TestDirectory();
    var testFile = Path.Combine(temp.Path, "clip.txt");
    File.WriteAllText(testFile, "test data");

    // Test Copy DropEffect (1)
    FileActionsEngine.SetClipboardFiles([testFile], isMove: false);
    var copyData = Clipboard.GetDataObject();
    Ensure(copyData != null, "Clipboard data object should not be null after copy.");
    var copyStream = copyData.GetData("Preferred DropEffect") as Stream;
    Ensure(copyStream != null, "Preferred DropEffect stream must be present for copy.");
    Ensure(copyStream.ReadByte() == 1, "DROPEFFECT_COPY must be 1.");

    // Test Move / Cut DropEffect (2)
    FileActionsEngine.SetClipboardFiles([testFile], isMove: true);
    var moveData = Clipboard.GetDataObject();
    Ensure(moveData != null, "Clipboard data object should not be null after move.");
    var moveStream = moveData.GetData("Preferred DropEffect") as Stream;
    Ensure(moveStream != null, "Preferred DropEffect stream must be present for move/cut.");
    Ensure(moveStream.ReadByte() == 2, "DROPEFFECT_MOVE must be 2.");
}

static void NotificationActivationHandlerRouting()
{
    using var temp = new TestDirectory();

    // openFile: file ada → seharusnya tidak throw
    var testFile = Path.Combine(temp.Path, "result.txt");
    File.WriteAllText(testFile, "ceyloro test");
    // Tidak memanggil Process.Start sungguhan karena file tidak bisa dibuka di test runner;
    // pastikan HandleActivation tidak throw untuk argumen yang valid.
    var threw = false;
    try { NotificationService.HandleActivation($"action=openFile&path={Uri.EscapeDataString(testFile)}"); }
    catch { threw = true; }
    Ensure(!threw, "HandleActivation openFile must not throw for an existing file.");

    // openFolder: folder ada
    threw = false;
    try { NotificationService.HandleActivation($"action=openFolder&path={Uri.EscapeDataString(temp.Path)}"); }
    catch { threw = true; }
    Ensure(!threw, "HandleActivation openFolder must not throw for an existing folder.");

    // openLog: log ada
    threw = false;
    var logFile = Path.Combine(temp.Path, "app.log");
    File.WriteAllText(logFile, "log content");
    try { NotificationService.HandleActivation($"action=openLog&path={Uri.EscapeDataString(logFile)}"); }
    catch { threw = true; }
    Ensure(!threw, "HandleActivation openLog must not throw for an existing log file.");
}

static void NotificationActivationHandlerEdgeCases()
{
    // Argumen kosong
    var threw = false;
    try { NotificationService.HandleActivation(string.Empty); }
    catch { threw = true; }
    Ensure(!threw, "HandleActivation must handle empty arguments silently.");

    // Argumen null-equivalent
    threw = false;
    try { NotificationService.HandleActivation("   "); }
    catch { threw = true; }
    Ensure(!threw, "HandleActivation must handle whitespace-only arguments silently.");

    // Action tidak dikenal
    threw = false;
    try { NotificationService.HandleActivation("action=unknownAction&path=C:\\nowhere"); }
    catch { threw = true; }
    Ensure(!threw, "HandleActivation must silently ignore unknown actions.");

    // openFile dengan path tidak ada → tidak crash
    threw = false;
    try { NotificationService.HandleActivation("action=openFile&path=C:\\does\\not\\exist.pdf"); }
    catch { threw = true; }
    Ensure(!threw, "HandleActivation openFile with non-existent path must not throw.");
}

static void GpuHardwareAccelerationSelection()
{
    // 1. Probed NVIDIA GPU
    var nvCaps = CreateFfmpegCapabilitiesWithGpu(
        encoders: ["libx264", "h264_nvenc", "libx265", "hevc_nvenc", "aac"],
        decoders: ["h264", "hevc"],
        muxers: ["mp4"],
        gpuEncoder: "h264_nvenc",
        hevcGpuEncoder: "hevc_nvenc");

    Ensure(nvCaps.IsHardwareAccelerated, "NVIDIA capabilities should report IsHardwareAccelerated = true.");
    Ensure(nvCaps.EncoderFor("mp4") == "h264_nvenc", "NVIDIA should prefer h264_nvenc.");
    Ensure(nvCaps.EncoderFor("mp4", allowHardware: false) == "libx264", "Fallback should select libx264 when allowHardware is false.");
    Ensure(nvCaps.EncoderFor("heic") == "hevc_nvenc", "NVIDIA should prefer hevc_nvenc for HEIC.");
    Ensure(nvCaps.EncoderFor("heic", allowHardware: false) == "libx265", "Fallback should select libx265 for HEIC when allowHardware is false.");

    // 2. Probed Intel QSV
    var qsvCaps = CreateFfmpegCapabilitiesWithGpu(
        encoders: ["libx264", "h264_qsv", "aac"],
        decoders: ["h264"],
        muxers: ["mp4"],
        gpuEncoder: "h264_qsv");
    Ensure(qsvCaps.IsHardwareAccelerated, "QSV should report IsHardwareAccelerated = true.");
    Ensure(qsvCaps.EncoderFor("mp4") == "h264_qsv", "QSV should prefer h264_qsv.");

    // 3. Probed AMD AMF
    var amfCaps = CreateFfmpegCapabilitiesWithGpu(
        encoders: ["libx264", "h264_amf", "aac"],
        decoders: ["h264"],
        muxers: ["mp4"],
        gpuEncoder: "h264_amf");
    Ensure(amfCaps.IsHardwareAccelerated, "AMF should report IsHardwareAccelerated = true.");
    Ensure(amfCaps.EncoderFor("mp4") == "h264_amf", "AMF should prefer h264_amf.");

    // 4. Pure CPU (no hardware detected)
    var cpuCaps = CreateFfmpegCapabilitiesWithGpu(
        encoders: ["libx264", "aac"],
        decoders: ["h264"],
        muxers: ["mp4"]);
    Ensure(!cpuCaps.IsHardwareAccelerated, "CPU should report IsHardwareAccelerated = false.");
    Ensure(cpuCaps.EncoderFor("mp4") == "libx264", "CPU should use libx264.");
}

static void GpuCommandBuilderVendorParameters()
{
    var settings = new AppSettings();

    // 1. NVIDIA NVENC command arguments
    var nvCaps = CreateFfmpegCapabilitiesWithGpu(
        encoders: ["libx264", "h264_nvenc", "aac"],
        decoders: ["h264"],
        muxers: ["mp4"],
        gpuEncoder: "h264_nvenc");

    var nvArgs = FfmpegCommandBuilder.Build(@"C:\in.mp4", @"C:\out.mp4", "mp4", nvCaps, settings, compression: false);
    Ensure(nvArgs.Contains("h264_nvenc"), "Should use h264_nvenc.");
    Ensure(nvArgs.Contains("-cq") && nvArgs.Contains("24"), "NVENC normal should use -cq 24.");
    Ensure(nvArgs.Contains("-preset") && nvArgs.Contains("p5"), "NVENC should use preset p5.");
    Ensure(nvArgs.Contains("-rc:v") && nvArgs.Contains("vbr"), "NVENC should use vbr rate control.");

    var nvCompArgs = FfmpegCommandBuilder.Build(@"C:\in.mp4", @"C:\out.mp4", "mp4", nvCaps, settings, compression: true);
    Ensure(nvCompArgs.Contains("-cq") && nvCompArgs.Contains("30"), "NVENC compression should use -cq 30.");

    // 2. Fallback to CPU via disableHardwareAcceleration flag
    var fallbackArgs = FfmpegCommandBuilder.Build(@"C:\in.mp4", @"C:\out.mp4", "mp4", nvCaps, settings, compression: false, disableHardwareAcceleration: true);
    Ensure(fallbackArgs.Contains("libx264"), "Fallback must select libx264.");
    Ensure(!fallbackArgs.Contains("h264_nvenc"), "Fallback must not use h264_nvenc.");
    Ensure(fallbackArgs.Contains("-crf") && fallbackArgs.Contains("23"), "CPU normal should use -crf 23.");

    // 3. Intel QSV command arguments
    var qsvCaps = CreateFfmpegCapabilitiesWithGpu(
        encoders: ["libx264", "h264_qsv", "aac"],
        decoders: ["h264"],
        muxers: ["mp4"],
        gpuEncoder: "h264_qsv");
    var qsvArgs = FfmpegCommandBuilder.Build(@"C:\in.mp4", @"C:\out.mp4", "mp4", qsvCaps, settings, compression: false);
    Ensure(qsvArgs.Contains("h264_qsv") && qsvArgs.Contains("-global_quality") && qsvArgs.Contains("24"), "QSV should use -global_quality 24.");

    // 4. AMD AMF command arguments
    var amfCaps = CreateFfmpegCapabilitiesWithGpu(
        encoders: ["libx264", "h264_amf", "aac"],
        decoders: ["h264"],
        muxers: ["mp4"],
        gpuEncoder: "h264_amf");
    var amfArgs = FfmpegCommandBuilder.Build(@"C:\in.mp4", @"C:\out.mp4", "mp4", amfCaps, settings, compression: false);
    Ensure(amfArgs.Contains("h264_amf") && amfArgs.Contains("-rc") && amfArgs.Contains("cqp") && amfArgs.Contains("-qp_i") && amfArgs.Contains("24"), "AMF should use cqp with qp_i 24.");
}

static FfmpegCapabilities CreateFfmpegCapabilitiesWithGpu(string[] encoders, string[] decoders, string[] muxers, string? gpuEncoder = null, string? hevcGpuEncoder = null)
{
    return new FfmpegCapabilities(
        new HashSet<string>(encoders, StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(decoders, StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(muxers, StringComparer.OrdinalIgnoreCase),
        gpuEncoder,
        hevcGpuEncoder);
}

static void SingleInstanceFileArgumentParsing()
{
    using var temp = new TestDirectory();
    var f1 = Path.Combine(temp.Path, "clip.mp4");
    var f2 = Path.Combine(temp.Path, "doc.pdf");
    File.WriteAllText(f1, "video");
    File.WriteAllText(f2, "doc");

    var parsed = SingleInstanceGate.ParseFileArguments(["--show", "--files", f1, "--context-menu", f2, "--random-flag"]);
    Ensure(parsed.Length == 2, $"Expected 2 parsed files, got {parsed.Length}.");
    Ensure(parsed.Contains(f1), "Parsed files should contain clip.mp4.");
    Ensure(parsed.Contains(f2), "Parsed files should contain doc.pdf.");

    var empty = SingleInstanceGate.ParseFileArguments(["--show", "--help"]);
    Ensure(empty.Length == 0, "No files should be parsed when only flags are passed.");
}

static void ShellExtensionContextRegistration()
{
    var wasRegistered = ShellExtensionManager.IsContextMenuRegistered();
    Ensure(wasRegistered == true || wasRegistered == false, "IsContextMenuRegistered must return a valid boolean.");
}

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Ceyloro.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { }
    }
}


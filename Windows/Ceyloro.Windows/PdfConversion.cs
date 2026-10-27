using System.Drawing.Imaging;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Ceyloro.Windows;

public sealed record PdfConversionOptions(string PageSize = "a4", string Margin = "none", string Quality = "balanced", string MultiPageOutput = "folder");

internal static class PdfConversion
{
    private sealed record JpegImage(byte[] Bytes, int Width, int Height);

    public static bool CanCreatePdfFromImage(string extension) => extension is "jpg" or "png" or "tiff" or "bmp" or "gif";

    public static async Task<IReadOnlyList<string>> ConvertPdfAsync(
        string input,
        string target,
        string outputPath,
        PdfConversionOptions options,
        CancellationToken cancellationToken,
        Action<double> progress)
    {
        if (target == "txt")
            return [ExtractText(input, outputPath, cancellationToken, progress)];
        if (target is not ("jpg" or "png"))
            throw new InvalidOperationException("Target PDF ini tidak didukung.");

        var storageFile = await StorageFile.GetFileFromPathAsync(input);
        var document = await PdfDocument.LoadFromFileAsync(storageFile);
        if (document.IsPasswordProtected)
            throw new InvalidDataException("PDF dilindungi kata sandi. Buka kunci file terlebih dahulu.");
        if (document.PageCount == 0)
            throw new InvalidDataException("PDF tidak memiliki halaman.");

        var pageCount = checked((int)document.PageCount);
        var baseName = Path.GetFileNameWithoutExtension(input);
        var directory = Path.GetDirectoryName(outputPath)!;
        var pageFolder = UniqueDirectory(directory, baseName);
        var useZip = pageCount > 1 && options.MultiPageOutput.Equals("zip", StringComparison.OrdinalIgnoreCase);
        var useFolder = pageCount > 1 && !useZip;
        var zipPath = UniquePath(Path.ChangeExtension(outputPath, ".zip"));
        var pagePaths = new List<string>();
        if (useFolder || useZip)
            Directory.CreateDirectory(pageFolder);
        var partialZip = zipPath + ".ceyloro-partial";
        try
        {
            for (var index = 0; index < pageCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var page = document.GetPage((uint)index);
                using var stream = new InMemoryRandomAccessStream();
                var maxDip = 4_000u;
                var factor = Math.Min(maxDip / Math.Max(page.Size.Width, page.Size.Height), 1d);
                var renderOptions = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * factor)),
                    DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * factor))
                };
                await page.RenderToStreamAsync(stream, renderOptions);
                var bytes = await ReadAllAsync(stream);
                var output = pageCount == 1
                    ? outputPath
                    : Path.Combine(pageFolder, PageFileName(baseName, index, pageCount, target));
                WriteRaster(bytes, output, target, options);
                pagePaths.Add(output);
                progress((index + 1d) / pageCount);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (useZip)
            {
                using (var file = new FileStream(partialZip, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    foreach (var pagePath in pagePaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        archive.CreateEntryFromFile(pagePath, $"{baseName}/{Path.GetFileName(pagePath)}", CompressionLevel.Optimal);
                    }
                }
                File.Move(partialZip, zipPath);
                foreach (var pagePath in pagePaths) TryDelete(pagePath);
                TryDeleteDirectory(pageFolder);
                return [zipPath];
            }
            if (useFolder)
                return [pageFolder];
            return pagePaths;
        }
        catch
        {
            TryDelete(partialZip);
            foreach (var pagePath in pagePaths) TryDelete(pagePath);
            TryDelete(outputPath);
            TryDeleteDirectory(pageFolder);
            throw;
        }
    }

    public static void CreateImagePdf(
        IReadOnlyList<string> images,
        string outputPath,
        PdfConversionOptions options,
        CancellationToken cancellationToken,
        Action<double> progress)
    {
        if (images.Count == 0)
            throw new InvalidDataException("Pilih setidaknya satu gambar untuk membuat PDF.");
        var partial = outputPath + ".ceyloro-partial";
        try
        {
            WritePdf(images, partial, options, cancellationToken, progress);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, outputPath);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    internal static async Task CompressAsync(
        string input,
        string output,
        CancellationToken cancellationToken,
        Action<double> progress)
    {
        // 1. Inspeksi tipe PDF: Text/Vector vs Scanned/Photo (Temuan 1 & Bagian 4.2 PRD)
        bool hasText = false;
        try
        {
            using var pigDoc = PdfPigDocument.Open(input);
            hasText = pigDoc.GetPages().Any(p => !string.IsNullOrWhiteSpace(p.Text));
        }
        catch { }

        if (!hasText)
        {
            try
            {
                // Fallback deteksi stream teks/vektor sederhana jika PdfPig gagal memetakan font
                var rawBytes = File.ReadAllBytes(input);
                var rawString = Encoding.Latin1.GetString(rawBytes);
                if (rawString.Contains("BT") && (rawString.Contains("Tj") || rawString.Contains("TJ")))
                {
                    hasText = true;
                }
            }
            catch { }
        }

        if (hasText)
        {
            // Text/Vector PDF: DILARANG MERASTERISASI HALAMAN MENJADI BITMAP (AC-1).
            // Pertahankan struktur vektor dan text layer.
            cancellationToken.ThrowIfCancellationRequested();
            progress(0.5);
            File.Copy(input, output, overwrite: true);
            progress(1.0);
            return;
        }

        // 2. Scanned/Photo PDF: Halaman didominasi raster bitmap. Lakukan downsampling & JPEG kompresi.
        var storageFile = await StorageFile.GetFileFromPathAsync(input);
        var document = await PdfDocument.LoadFromFileAsync(storageFile);
        if (document.IsPasswordProtected)
            throw new InvalidDataException("PDF dilindungi kata sandi. Buka kunci file terlebih dahulu.");
        if (document.PageCount == 0)
            throw new InvalidDataException("PDF tidak memiliki halaman.");

        var staging = Path.Combine(Path.GetTempPath(), "Ceyloro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var pages = new List<string>();
        try
        {
            for (var index = 0; index < document.PageCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var page = document.GetPage((uint)index))
                using (var stream = new InMemoryRandomAccessStream())
                {
                var maxDip = 2_000u;
                var factor = Math.Min(maxDip / Math.Max(page.Size.Width, page.Size.Height), 1d);
                var renderOptions = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * factor)),
                    DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * factor))
                };
                await page.RenderToStreamAsync(stream, renderOptions);
                var bytes = await ReadAllAsync(stream);
                var pagePath = Path.Combine(staging, $"page-{index + 1:D4}.jpg");
                WriteRaster(bytes, pagePath, "jpg", new PdfConversionOptions(Quality: "balanced"));
                pages.Add(pagePath);
                progress(0.5 * (index + 1d) / document.PageCount);
            }
        }

            CreateImagePdf(pages, output, new PdfConversionOptions(PageSize: "matchImage", Quality: "balanced"), cancellationToken, value => progress(0.5 + value * 0.5));
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static string ExtractText(string input, string output, CancellationToken cancellationToken, Action<double> progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = PdfPigDocument.Open(input);
        var pageTexts = new List<string>();
        var pages = document.GetPages().ToArray();
        for (var index = 0; index < pages.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageTexts.Add(pages[index].Text);
            progress((index + 1d) / Math.Max(pages.Length, 1));
        }
        var text = string.Join(Environment.NewLine, pageTexts).Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("PDF tidak berisi teks yang dapat diekstrak. PDF hasil scan memerlukan OCR.");
        var partial = output + ".ceyloro-partial";
        try
        {
            File.WriteAllText(partial, text + Environment.NewLine, new UTF8Encoding(false));
            File.Move(partial, output);
            return output;
        }
        finally { TryDelete(partial); }
    }

    private static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(checked((uint)stream.Size));
        var bytes = new byte[checked((int)stream.Size)];
        reader.ReadBytes(bytes);
        return bytes;
    }

    internal static string PageFileName(string baseName, int index, int total, string fileExtension)
    {
        var width = Math.Max(total, 1).ToString(CultureInfo.InvariantCulture).Length;
        width = Math.Max(width, 2);
        var safeBaseName = string.IsNullOrWhiteSpace(baseName) || baseName is "." or ".." ? "Hasil" : baseName;
        foreach (var invalid in Path.GetInvalidFileNameChars()) safeBaseName = safeBaseName.Replace(invalid, '-');
        return $"{safeBaseName}-{(index + 1).ToString($"D{width}", CultureInfo.InvariantCulture)}.{fileExtension.TrimStart('.') }";
    }

    private static void WriteRaster(byte[] bytes, string path, string target, PdfConversionOptions options)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(stream);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (target != "jpg")
        {
            image.Save(file, ImageFormat.Png);
            return;
        }

        using var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.DrawImage(image, 0, 0, image.Width, image.Height);
        }
        var codec = ImageCodecInfo.GetImageEncoders().First(encoder => encoder.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality(options));
        bitmap.Save(file, codec, parameters);
    }

    private static JpegImage PrepareJpeg(string path, PdfConversionOptions options)
    {
        using var source = Image.FromFile(path);
        ApplyExifOrientation(source);
        var maxSide = options.Quality.Equals("original", StringComparison.OrdinalIgnoreCase) ? int.MaxValue : 2480;
        var scale = Math.Min(1d, maxSide / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, width, height);
        }
        using var memory = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(encoder => encoder.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality(options));
        bitmap.Save(memory, codec, parameters);
        return new JpegImage(memory.ToArray(), width, height);
    }

    internal static long JpegQuality(PdfConversionOptions options) =>
        options.Quality.Equals("original", StringComparison.OrdinalIgnoreCase) ? 92L : 85L;

    internal static void ApplyExifOrientation(Image image)
    {
        const int orientationId = 0x0112;
        try
        {
            if (!image.PropertyIdList.Contains(orientationId)) return;
            var orientationValue = image.GetPropertyItem(orientationId)?.Value;
            if (orientationValue is null || orientationValue.Length < 2) return;
            var orientation = BitConverter.ToUInt16(orientationValue, 0);
            var rotate = orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };
            if (rotate != RotateFlipType.RotateNoneFlipNone) image.RotateFlip(rotate);
            image.RemovePropertyItem(orientationId);
        }
        catch (ArgumentException) { }
    }

    private static void WritePdf(IReadOnlyList<string> imagePaths, string path, PdfConversionOptions options, CancellationToken cancellationToken, Action<double> progress)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var offsets = new List<long> { 0 };
        WriteAscii(file, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var pageIds = Enumerable.Range(0, imagePaths.Count).Select(index => 3 + index * 3).ToArray();
        WriteObject(file, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(file, offsets, 2, $"<< /Type /Pages /Count {imagePaths.Count} /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] >>");

        for (var index = 0; index < imagePaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = PrepareJpeg(imagePaths[index], options);
            var (pageWidth, pageHeight) = GetPageSize(image.Width, image.Height, options.PageSize);
            var margin = options.Margin.Equals("medium", StringComparison.OrdinalIgnoreCase) ? 36d : 0d;
            var availableWidth = Math.Max(1, pageWidth - margin * 2);
            var availableHeight = Math.Max(1, pageHeight - margin * 2);
            var factor = Math.Min(availableWidth / image.Width, availableHeight / image.Height);
            var drawWidth = image.Width * factor;
            var drawHeight = image.Height * factor;
            var x = (pageWidth - drawWidth) / 2;
            var y = (pageHeight - drawHeight) / 2;
            var pageId = 3 + index * 3;
            var imageId = pageId + 1;
            var contentId = pageId + 2;
            WriteObject(file, offsets, pageId, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageWidth)} {F(pageHeight)}] /Resources << /XObject << /Im0 {imageId} 0 R >> >> /Contents {contentId} 0 R >>");
            offsets.Add(file.Position);
            WriteAscii(file, $"{imageId} 0 obj\n<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {image.Bytes.Length} >>\nstream\n");
            file.Write(image.Bytes);
            WriteAscii(file, "\nendstream\nendobj\n");
            var content = Encoding.ASCII.GetBytes($"q\n{F(drawWidth)} 0 0 {F(drawHeight)} {F(x)} {F(y)} cm\n/Im0 Do\nQ\n");
            offsets.Add(file.Position);
            WriteAscii(file, $"{contentId} 0 obj\n<< /Length {content.Length} >>\nstream\n");
            file.Write(content);
            WriteAscii(file, "endstream\nendobj\n");
            progress((index + 1d) / imagePaths.Count);
        }

        var xref = file.Position;
        WriteAscii(file, $"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) WriteAscii(file, $"{offset:0000000000} 00000 n \n");
        WriteAscii(file, $"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static (double Width, double Height) GetPageSize(int imageWidth, int imageHeight, string option)
    {
        if (option.Equals("matchImage", StringComparison.OrdinalIgnoreCase) || option.Equals("match", StringComparison.OrdinalIgnoreCase))
        {
            var scale = Math.Min(842d / Math.Max(imageWidth, imageHeight), 1d);
            return (imageWidth * scale, imageHeight * scale);
        }
        var (width, height) = option.Equals("letter", StringComparison.OrdinalIgnoreCase) ? (612d, 792d) : (595.28d, 841.89d);
        return imageWidth > imageHeight ? (height, width) : (width, height);
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string UniquePath(string candidate)
    {
        var directory = Path.GetDirectoryName(candidate)!;
        var baseName = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        for (var suffix = 2; File.Exists(candidate) || Directory.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{baseName} {suffix}{extension}");
        return candidate;
    }

    private static string UniqueDirectory(string parent, string baseName)
    {
        var candidate = Path.Combine(parent, baseName);
        for (var suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
            candidate = Path.Combine(parent, $"{baseName} {suffix}");
        return candidate;
    }

    private static void WriteObject(Stream stream, List<long> offsets, int id, string body)
    {
        offsets.Add(stream.Position);
        WriteAscii(stream, $"{id} 0 obj\n{body}\nendobj\n");
    }

    private static void WriteAscii(Stream stream, string text) => stream.Write(Encoding.Latin1.GetBytes(text));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}


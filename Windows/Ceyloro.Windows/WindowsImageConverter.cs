using System.Drawing.Imaging;

namespace Ceyloro.Windows;

internal static class WindowsImageConverter
{
    public static bool CanDecode(string extension) => extension is "jpg" or "png" or "tiff" or "bmp" or "gif";

    public static int FrameCount(string input, string extension)
    {
        if (extension != "tiff") return 1;
        using var image = Image.FromFile(input);
        return Math.Max(1, image.GetFrameCount(FrameDimension.Page));
    }

    public static IReadOnlyList<string> Convert(
        string input,
        string target,
        IReadOnlyList<string> outputs,
        AppSettings settings,
        CancellationToken cancellationToken,
        Action<double> progress,
        double? qualityOverride = null)
    {
        if (!CanDecode(Path.GetExtension(input).TrimStart('.').ToLowerInvariant()) || target is not ("jpg" or "png" or "tiff"))
            throw new InvalidOperationException("Format gambar ini tidak didukung oleh konverter gambar Windows.");
        var extension = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        var count = FrameCount(input, extension);
        if (outputs.Count != count)
            throw new InvalidOperationException("Jumlah file hasil tidak sesuai dengan jumlah halaman gambar.");

        var completed = new List<string>();
        try
        {
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var source = Image.FromFile(input);
                if (extension == "tiff") source.SelectActiveFrame(FrameDimension.Page, index);
                PdfConversion.ApplyExifOrientation(source);
                var partial = outputs[index] + ".ceyloro-partial";
                try
                {
                    if (target == "jpg")
                        SaveJpeg(source, partial, qualityOverride ?? settings.JpgQuality);
                    else if (target == "png" && qualityOverride.HasValue)
                        SaveQuantizedPng(source, partial);
                    else
                        source.Save(partial, target == "png" ? ImageFormat.Png : ImageFormat.Tiff);
                    File.Move(partial, outputs[index]);
                    completed.Add(outputs[index]);
                }
                finally
                {
                    try { if (File.Exists(partial)) File.Delete(partial); }
                    catch { }
                }
                progress((index + 1d) / count);
            }
            return completed;
        }
        catch
        {
            foreach (var output in completed)
            {
                try { if (File.Exists(output)) File.Delete(output); }
                catch { }
            }
            throw;
        }
    }

    internal static void SaveJpeg(Image source, string path, double quality)
    {
        using var flattened = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(flattened))
        {
            graphics.Clear(Color.White);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }
        var codec = ImageCodecInfo.GetImageEncoders().First(encoder => encoder.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        var value = Math.Clamp((long)Math.Round(quality * 100), 10, 100);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, value);
        flattened.Save(path, codec, parameters);
    }

    internal static void SaveQuantizedPng(Image source, string path)
    {
        var width = source.Width;
        var height = source.Height;
        using var original32 = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(original32))
        {
            g.DrawImage(source, 0, 0, width, height);
        }

        using var quantized = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
        var palette = quantized.Palette;

        var rect = new Rectangle(0, 0, width, height);
        var srcData = original32.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dstData = quantized.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

        try
        {
            var totalPixels = width * height;
            var srcPixels = new int[totalPixels];
            System.Runtime.InteropServices.Marshal.Copy(srcData.Scan0, srcPixels, 0, totalPixels);

            var counts = new Dictionary<int, int>();
            bool hasTransparency = false;

            for (int i = 0; i < totalPixels; i++)
            {
                var pixel = srcPixels[i];
                var a = (pixel >> 24) & 0xFF;
                if (a < 32)
                {
                    hasTransparency = true;
                    continue;
                }
                var r = (pixel >> 16) & 0xFF;
                var g = (pixel >> 8) & 0xFF;
                var b = pixel & 0xFF;
                var key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

            var entries = palette.Entries;
            int paletteIndex = 0;
            if (hasTransparency)
            {
                entries[0] = Color.FromArgb(0, 0, 0, 0);
                paletteIndex = 1;
            }

            var topColors = counts.OrderByDescending(kv => kv.Value).Take(256 - paletteIndex).ToList();
            foreach (var kv in topColors)
            {
                var r = ((kv.Key >> 10) & 0x1F) << 3;
                var g = ((kv.Key >> 5) & 0x1F) << 3;
                var b = (kv.Key & 0x1F) << 3;
                entries[paletteIndex++] = Color.FromArgb(255, r, g, b);
            }
            while (paletteIndex < 256)
            {
                entries[paletteIndex++] = Color.Black;
            }
            quantized.Palette = palette;

            var dstPixels = new byte[totalPixels];
            int startIndex = hasTransparency ? 1 : 0;

            for (int i = 0; i < totalPixels; i++)
            {
                var pixel = srcPixels[i];
                var a = (pixel >> 24) & 0xFF;
                if (hasTransparency && a < 32)
                {
                    dstPixels[i] = 0;
                    continue;
                }
                var r = (pixel >> 16) & 0xFF;
                var g = (pixel >> 8) & 0xFF;
                var b = pixel & 0xFF;

                int bestIdx = startIndex;
                int bestDist = int.MaxValue;
                for (int p = startIndex; p < paletteIndex; p++)
                {
                    var pr = entries[p].R;
                    var pg = entries[p].G;
                    var pb = entries[p].B;
                    var dr = r - pr;
                    var dg = g - pg;
                    var db = b - pb;
                    var dist = dr * dr * 2 + dg * dg * 4 + db * db * 3;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = p;
                        if (dist == 0) break;
                    }
                }
                dstPixels[i] = (byte)bestIdx;
            }

            System.Runtime.InteropServices.Marshal.Copy(dstPixels, 0, dstData.Scan0, totalPixels);
        }
        finally
        {
            original32.UnlockBits(srcData);
            quantized.UnlockBits(dstData);
        }

        quantized.Save(path, ImageFormat.Png);
    }
}


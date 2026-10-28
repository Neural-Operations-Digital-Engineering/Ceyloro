using System.Globalization;

namespace Ceyloro.Windows;

internal static class FfmpegCommandBuilder
{
    internal const string GifFilterGraph = "fps=12,scale=w=min(720\\,iw):h=-2:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=sierra2_4a[v]";
    internal const string InputProtocolWhitelist = "file";

    internal static IReadOnlyList<string> BuildInputArguments(string input) =>
        // Tidak menyertakan -y: FFmpeg tidak boleh menimpa file output tanpa eksklusivitas;
        // commit ke output final dilakukan melalui File.Move atomik setelah job selesai.
        ["-hide_banner", "-loglevel", "error", "-nostdin", "-protocol_whitelist", InputProtocolWhitelist, "-i", input];

    public static IReadOnlyList<string> Build(
        string input,
        string output,
        string target,
        FfmpegCapabilities capabilities,
        AppSettings settings,
        bool compression = false,
        bool disableHardwareAcceleration = false)
    {
        var arguments = new List<string>(BuildInputArguments(input));
        if (target is "jpg" or "png" or "heic" or "tiff" or "webp" or "avif")
        {
            var encoder = RequiredEncoder(capabilities, target, !disableHardwareAcceleration);
            arguments.AddRange(["-frames:v", "1", "-an", "-c:v", encoder]);
            if (target == "jpg")
            {
                var quality = Math.Clamp(settings.JpgQuality, 0.1, 1.0);
                var qualityValue = compression ? Math.Max(10, quality * 0.78) : quality;
                arguments.AddRange(["-q:v", Math.Clamp((int)Math.Round(31 - qualityValue * 29), 2, 31).ToString(CultureInfo.InvariantCulture)]);
            }
            else if (target == "heic")
            {
                var crf = Math.Clamp((int)Math.Round(51 * (1 - settings.HeicQuality)), 0, 51);
                if (compression) crf = Math.Max(crf, 28);
                if (encoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
                    arguments.AddRange(["-cq", crf.ToString(CultureInfo.InvariantCulture), "-tag:v", "hvc1"]);
                else
                    arguments.AddRange(["-crf", crf.ToString(CultureInfo.InvariantCulture), "-tag:v", "hvc1"]);
            }
        }
        else if (target is "mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus" or "aiff")
        {
            arguments.AddRange(["-vn", "-c:a", RequiredEncoder(capabilities, target, allowHardware: false)]);
            if (target is "mp3" or "m4a") arguments.AddRange(["-b:a", compression ? "96k" : "192k"]);
            if (target == "ogg") arguments.AddRange(["-ac", "2", "-q:a", compression ? "3" : "5"]);
            if (target == "opus") arguments.AddRange(["-b:a", compression ? "64k" : "128k"]);
        }
        else
        {
            var encoder = RequiredEncoder(capabilities, target, !disableHardwareAcceleration);
            arguments.AddRange(["-c:v", encoder]);
            if (target == "webm") arguments.AddRange(["-crf", compression ? "42" : "32", "-b:v", "0"]);
            else if (target is "mp4" or "mov" or "mkv" or "avi")
            {
                if (encoder is "libx264" or "h264")
                {
                    arguments.AddRange(["-crf", compression ? "28" : "23", "-preset", "medium"]);
                }
                else if (encoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
                {
                    arguments.AddRange(["-cq", compression ? "30" : "24", "-preset", "p5", "-rc:v", "vbr"]);
                }
                else if (encoder.Contains("qsv", StringComparison.OrdinalIgnoreCase))
                {
                    arguments.AddRange(["-global_quality", compression ? "28" : "24", "-preset", "medium"]);
                }
                else if (encoder.Contains("amf", StringComparison.OrdinalIgnoreCase))
                {
                    arguments.AddRange(["-rc", "cqp", "-qp_i", compression ? "28" : "24", "-qp_p", compression ? "28" : "24", "-quality", "balanced"]);
                }
                else
                {
                    arguments.AddRange(["-q:v", compression ? "7" : "3"]);
                }
            }
            if (target == "gif")
            {
                arguments.AddRange([
                    "-an",
                    "-filter_complex",
                    GifFilterGraph,
                    "-map", "[v]"
                ]);
            }
            else if (target == "webm")
                arguments.AddRange(["-c:a", capabilities.AudioEncoderFor("opus") ?? "copy"]);
            else if (target == "avi")
                arguments.AddRange(["-c:a", capabilities.AudioEncoderFor("mp3") ?? "copy"]);
            else if (target == "wmv")
                arguments.AddRange(["-c:a", "wmav2"]);
            else
                arguments.AddRange(["-c:a", capabilities.AudioEncoderFor("m4a") ?? "copy"]);
            if (target == "mp4") arguments.AddRange(["-movflags", "+faststart"]);
        }
        arguments.AddRange(["-progress", "pipe:1", "-nostats", output]);
        return arguments;
    }

    private static string RequiredEncoder(FfmpegCapabilities capabilities, string target, bool allowHardware = true) =>
        capabilities.EncoderFor(target, allowHardware) ?? throw new InvalidOperationException($"Encoder FFmpeg untuk {target.ToUpperInvariant()} tidak tersedia.");
}

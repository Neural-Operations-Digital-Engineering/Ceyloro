using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Ceyloro.Windows;

internal sealed class FfmpegCapabilities
{
    private static readonly IReadOnlyDictionary<string, string[]> encoders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = ["mjpeg"], ["png"] = ["png"], ["heic"] = ["libx265", "hevc"], ["tiff"] = ["tiff"],
        ["mp3"] = ["libmp3lame", "libshine"], ["m4a"] = ["aac"], ["wav"] = ["pcm_s16le"],
        ["flac"] = ["flac"], ["ogg"] = ["libvorbis", "vorbis"], ["opus"] = ["libopus", "opus"], ["aiff"] = ["pcm_s16be"],
        ["mp4"] = ["libx264", "h264", "h264_nvenc", "h264_qsv", "h264_amf", "mpeg4"],
        ["mov"] = ["libx264", "h264", "h264_nvenc", "h264_qsv", "h264_amf", "mpeg4"],
        ["mkv"] = ["libx264", "h264", "h264_nvenc", "h264_qsv", "h264_amf", "mpeg4"],
        ["webm"] = ["libvpx-vp9", "libvpx", "vp9"],
        ["avi"] = ["libx264", "h264", "mpeg4"], ["wmv"] = ["wmv2"], ["gif"] = ["gif"]
    };

    private static readonly IReadOnlyDictionary<string, string[]> muxers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = ["image2"], ["png"] = ["image2"], ["heic"] = ["heic", "heif"], ["tiff"] = ["image2"],
        ["mp3"] = ["mp3"], ["m4a"] = ["ipod", "mov"], ["wav"] = ["wav"], ["flac"] = ["flac"],
        ["ogg"] = ["ogg"], ["opus"] = ["opus"], ["aiff"] = ["aiff"], ["mp4"] = ["mp4"], ["mov"] = ["mov"],
        ["mkv"] = ["matroska"], ["webm"] = ["webm"], ["avi"] = ["avi"], ["wmv"] = ["asf"], ["gif"] = ["gif"]
    };

    private static readonly IReadOnlyDictionary<string, string[]> imageDecoders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = ["mjpeg"], ["png"] = ["png"], ["webp"] = ["webp"], ["heic"] = ["hevc"],
        ["tiff"] = ["tiff"], ["bmp"] = ["bmp"], ["gif"] = ["gif"], ["avif"] = ["av1"]
    };

    private readonly HashSet<string> availableEncoders;
    private readonly HashSet<string> availableDecoders;
    private readonly HashSet<string> availableMuxers;

    private FfmpegCapabilities(HashSet<string> availableEncoders, HashSet<string> availableDecoders, HashSet<string> availableMuxers)
    {
        this.availableEncoders = availableEncoders;
        this.availableDecoders = availableDecoders;
        this.availableMuxers = availableMuxers;
    }

    public static FfmpegCapabilities Probe(string ffmpegPath)
    {
        var encoderOutput = Run(ffmpegPath, "-hide_banner", "-encoders");
        var decoderOutput = Run(ffmpegPath, "-hide_banner", "-decoders");
        var formatOutput = Run(ffmpegPath, "-hide_banner", "-formats");
        var encoderSet = ParseCodecNames(encoderOutput);
        var decoderSet = ParseCodecNames(decoderOutput);
        var muxerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in formatOutput.SplitLines())
        {
            if (line.Length < 4 || (line[1] != 'E' && line[2] != 'E')) continue;
            var names = line[3..].Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (names.Length > 0)
                foreach (var name in names[0].Split(',', StringSplitOptions.RemoveEmptyEntries)) muxerSet.Add(name);
        }
        return new FfmpegCapabilities(encoderSet, decoderSet, muxerSet);
    }

    private static HashSet<string> ParseCodecNames(string output) => Regex.Matches(output, @"^\s*[A-Z\.]{6}\s+(\S+)", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool Supports(string target)
    {
        if (EncoderFor(target) is null || !muxers.TryGetValue(target, out var muxerNames) || !muxerNames.Any(availableMuxers.Contains))
            return false;
        var audioTarget = target switch
        {
            "webm" => "opus",
            "avi" => "mp3",
            "wmv" => "wmv-audio",
            "gif" => null,
            "jpg" or "png" or "heic" or "tiff" => null,
            "mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus" or "aiff" => null,
            _ => "m4a"
        };
        return audioTarget is null || (audioTarget == "wmv-audio"
            ? availableEncoders.Contains("wmav2")
            : AudioEncoderFor(audioTarget) is not null);
    }

    public string? EncoderFor(string target) =>
        encoders.TryGetValue(target, out var encoderNames) ? encoderNames.FirstOrDefault(availableEncoders.Contains) : null;

    public string? AudioEncoderFor(string target) => EncoderFor(target);

    public bool CanDecodeImage(string extension) =>
        imageDecoders.TryGetValue(extension, out var names) && names.Any(availableDecoders.Contains);

    private static string Run(string executable, params string[] arguments)
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
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("FFmpeg tidak dapat dijalankan untuk memeriksa kemampuan format.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("FFmpeg gagal melaporkan kemampuan encoder dan muxer.");
        return stdout.Result + Environment.NewLine + stderr.Result;
    }
}

internal static class TextLineExtensions
{
    public static IEnumerable<string> SplitLines(this string value) => value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
}


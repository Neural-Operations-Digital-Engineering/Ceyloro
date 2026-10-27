using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ceyloro.Windows;

public sealed record CeyloroVersion(IReadOnlyList<int> Components) : IComparable<CeyloroVersion>
{
    public static CeyloroVersion Parse(string raw)
    {
        var normalized = raw.Trim().TrimStart('v', 'V');
        var parts = normalized.Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var components = new List<int>();
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var num))
                components.Add(num);
        }
        return new CeyloroVersion(components.Count == 0 ? [0] : components);
    }

    public int CompareTo(CeyloroVersion? other)
    {
        if (other is null) return 1;
        var count = Math.Max(Components.Count, other.Components.Count);
        for (var i = 0; i < count; i++)
        {
            var left = i < Components.Count ? Components[i] : 0;
            var right = i < other.Components.Count ? other.Components[i] : 0;
            if (left != right)
                return left.CompareTo(right);
        }
        return 0;
    }

    public override string ToString() => string.Join(".", Components);
}

public sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

public sealed record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseNotes,
    long PackageSize,
    string? DownloadUrl,
    string? ChecksumUrl
);

public sealed class UpdateService
{
    private const string DefaultRepo = "ceyloro/ceyloro";
    private readonly HttpClient httpClient;
    private readonly string repository;

    public UpdateService(string? repository = null)
    {
        this.repository = repository ?? Environment.GetEnvironmentVariable("CEYLORO_REPO") ?? DefaultRepo;
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Ceyloro-Windows-Updater", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public static string CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public static string SystemArchitecture =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var current = CeyloroVersion.Parse(CurrentVersion);
        var url = $"https://api.github.com/repos/{repository}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Pemeriksaan pembaruan gagal (HTTP {response.StatusCode}).");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var release = JsonSerializer.Deserialize<GitHubRelease>(json)
            ?? throw new InvalidOperationException("Data release GitHub tidak valid.");

        var latestVersion = CeyloroVersion.Parse(release.TagName);
        var hasUpdate = latestVersion.CompareTo(current) > 0;

        var arch = SystemArchitecture;
        var packageAsset = release.Assets.FirstOrDefault(a => a.Name.Equals($"Ceyloro-Windows-{arch}.zip", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        var checksumAsset = release.Assets.FirstOrDefault(a => a.Name.Equals($"Ceyloro-Windows-{arch}.zip.sha256", StringComparison.OrdinalIgnoreCase));

        return new UpdateCheckResult(
            HasUpdate: hasUpdate,
            CurrentVersion: CurrentVersion,
            LatestVersion: release.TagName.TrimStart('v', 'V'),
            ReleaseNotes: release.Body,
            PackageSize: packageAsset?.Size ?? 0,
            DownloadUrl: packageAsset?.BrowserDownloadUrl,
            ChecksumUrl: checksumAsset?.BrowserDownloadUrl
        );
    }

    public async Task<string> DownloadAndVerifyAsync(
        UpdateCheckResult updateInfo,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
            throw new InvalidOperationException("URL unduhan paket pembaruan tidak ditemukan.");

        var stagingDir = Path.Combine(Path.GetTempPath(), "Ceyloro", "UpdateStaging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);

        var zipPath = Path.Combine(stagingDir, "update.zip");

        // Unduh checksum jika tersedia
        string? expectedChecksum = null;
        if (!string.IsNullOrWhiteSpace(updateInfo.ChecksumUrl))
        {
            try
            {
                var sha256Content = await httpClient.GetStringAsync(updateInfo.ChecksumUrl, cancellationToken);
                expectedChecksum = sha256Content.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            }
            catch (Exception ex)
            {
                AppLog.Write($"Gagal mengunduh file checksum: {ex.Message}");
            }
        }

        // Unduh zip
        using (var response = await httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.PackageSize;
            using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = File.Create(zipPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                totalRead += read;
                if (totalBytes > 0 && progress != null)
                {
                    progress.Report(Math.Min(0.95, (double)totalRead / totalBytes));
                }
            }
        }

        // Validasi SHA256 jika ada
        if (!string.IsNullOrWhiteSpace(expectedChecksum))
        {
            using var stream = File.OpenRead(zipPath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
            var computedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            if (!string.Equals(computedHash, expectedChecksum.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(stagingDir, true); } catch { }
                throw new InvalidOperationException($"Integritas paket tidak valid. Checksum diharapkan {expectedChecksum}, ditemukan {computedHash}.");
            }
        }

        progress?.Report(1.0);
        return zipPath;
    }

    public static void ApplyUpdateAndRestart(string zipPath)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var processId = Environment.ProcessId;
        var helperScriptPath = Path.Combine(Path.GetTempPath(), "Ceyloro", $"apply-update-{Guid.NewGuid():N}.cmd");

        var scriptDir = Path.GetDirectoryName(helperScriptPath)!;
        Directory.CreateDirectory(scriptDir);

        // Buat batch script helper untuk menunggu proses saat ini keluar, backup, ekstrak zip, dan restart
        var script = $@"@echo off
setlocal
echo Menunggu Ceyloro keluar...
:wait_proc
tasklist /fi ""pid eq {processId}"" | find ""{processId}"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait_proc
)

echo Memasang pembaruan Ceyloro...
powershell -NoProfile -ExecutionPolicy Bypass -Command ""Expand-Archive -LiteralPath '{zipPath}' -DestinationPath '{appDir}' -Force""
if errorlevel 1 goto failed

echo Meluncurkan Ceyloro...
start """" ""{Path.Combine(appDir, "Ceyloro.exe")}""
goto cleanup

:failed
echo Pembaruan gagal dipasang.

:cleanup
del ""{zipPath}"" 2>nul
del ""%~f0"" 2>nul
";

        File.WriteAllText(helperScriptPath, script);

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{helperScriptPath}\"",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(startInfo);
        Environment.Exit(0);
    }
}


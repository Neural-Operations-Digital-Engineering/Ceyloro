namespace Ceyloro.Windows;

internal static class TempDirectoryCleanup
{
    internal static int RemoveStaleDirectories(string root, DateTime utcNow, TimeSpan minimumAge)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return 0;

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var name = Path.GetFileName(directory);
                    if (!Guid.TryParseExact(name, "N", out _) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                        continue;

                    if (utcNow - Directory.GetLastWriteTimeUtc(directory) < minimumAge)
                        continue;

                    Directory.Delete(directory, recursive: true);
                    removed++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    AppLog.Write($"Pembersihan temp lama dilewati ({error.GetType().Name}).");
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"Pembersihan temp saat startup dilewati ({error.GetType().Name}).");
        }
        return removed;
    }
}


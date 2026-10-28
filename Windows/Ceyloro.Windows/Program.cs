using System.IO;
using System.Windows.Forms;

namespace Ceyloro.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var instance = SingleInstanceGate.TryAcquire(args);
        if (instance is null)
            return;

        TempDirectoryCleanup.RemoveStaleDirectories(
            Path.Combine(Path.GetTempPath(), "Ceyloro"), DateTime.UtcNow, TimeSpan.FromHours(24));

        var initialFiles = SingleInstanceGate.ParseFileArguments(args);
        var openOverlayOnStartup = args.Any(argument => string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase)) || initialFiles.Length > 0;
        Application.Run(new TrayApplicationContext(openOverlayOnStartup, initialFiles));
    }
}

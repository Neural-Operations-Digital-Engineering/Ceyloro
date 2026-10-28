using System.Formats.Tar;
using System.IO.Compression;

namespace Ceyloro.Windows;

internal static class ArchiveConverter
{
    private const long MaximumExpandedBytes = 2_000_000_000;
    private const int MaximumEntries = 100_000;
    internal const string MaximumEntriesMessage = "Arsip melebihi batas jumlah entri (100.000).";

    public static bool IsSupportedArchive(string input)
    {
        var extension = PortableFileFormat.Detect(input).Extension;
        try
        {
            if (extension == "zip")
            {
                using var file = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (file.Length < 4) return false;
                Span<byte> magic = stackalloc byte[4];
                return file.Read(magic) == 4 && magic[0] == 0x50 && magic[1] == 0x4B;
            }
            if (extension == "tar")
            {
                using var file = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (file.Length < 512) return false;
                Span<byte> header = stackalloc byte[512];
                if (file.Read(header) < 512) return false;
                return (header[257] == (byte)'u' && header[258] == (byte)'s' && header[259] == (byte)'t' && header[260] == (byte)'a' && header[261] == (byte)'r')
                    || header.Slice(0, 100).IndexOfAnyExcept((byte)0) >= 0;
            }
            if (extension == "gzip")
            {
                using var file = File.OpenRead(input);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new TarReader(gzip);
                return reader.GetNextEntry() is not null;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    public static void Convert(string input, string target, string output, CancellationToken cancellationToken, Action<double> progress)
    {
        var source = PortableFileFormat.Detect(input).Extension;
        if (source is "rar" or "7z" || target is not ("zip" or "tar" or "gzip"))
            throw new InvalidOperationException("Format arsip ini tidak didukung.");

        // logicalBaseName berasal dari nama file input — bukan dari direktori staging GUID.
        // Ini mencegah arsip output berisi prefix GUID sebagai root (temuan B2).
        var logicalBaseName = Path.GetFileNameWithoutExtension(input);
        if (string.IsNullOrWhiteSpace(logicalBaseName)) logicalBaseName = "archive";

        var partial = output + ".ceyloro-partial";
        var staging = Path.Combine(Path.GetTempPath(), "Ceyloro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Extract(input, source, staging, cancellationToken);
            progress(0.55);
            cancellationToken.ThrowIfCancellationRequested();
            var content = SelectContentRoot(staging);
            Create(target, content, logicalBaseName, partial, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, output);
            progress(1);
        }
        finally
        {
            TryDelete(partial);
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { }
        }
    }

    internal static void Extract(string input, string extension, string destination, CancellationToken cancellationToken)
    {
        if (extension == "zip")
        {
            using var archive = ZipFile.OpenRead(input);
            if (archive.Entries.Count > MaximumEntries)
                throw new InvalidDataException(MaximumEntriesMessage);
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            long zipExpanded = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsZipLink(entry))
                    throw new InvalidDataException("Arsip yang berisi symbolic link tidak didukung.");
                var entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                if (entryPath.Contains(':'))
                    throw new InvalidDataException("Arsip berisi nama jalur yang tidak aman.");
                var target = Path.GetFullPath(Path.Combine(destination, entryPath));
                var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    !(isDirectory && target.Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Arsip berisi jalur file yang tidak aman.");
                if (isDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var source = entry.Open();
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                zipExpanded += CopyWithLimit(source, output, cancellationToken, MaximumExpandedBytes - zipExpanded);
            }
            return;
        }

        using var file = File.OpenRead(input);
        using Stream archiveStream = extension is "gzip" or "gz" or "tgz"
            ? new GZipStream(file, CompressionMode.Decompress)
            : file;
        using var reader = new TarReader(archiveStream, leaveOpen: true);
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        long tarExpanded = 0;
        var entries = 0;
        TarEntry? tarEntry;
        while ((tarEntry = reader.GetNextEntry()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entries > MaximumEntries)
                throw new InvalidDataException(MaximumEntriesMessage);
            var relative = tarEntry.Name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (relative.Contains(':'))
                throw new InvalidDataException("Arsip berisi nama jalur yang tidak aman.");
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            var isRootDirectory = tarEntry.EntryType == TarEntryType.Directory &&
                target.Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase) && !isRootDirectory)
                throw new InvalidDataException("Arsip berisi jalur file yang tidak aman.");
            if (tarEntry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            if (tarEntry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                throw new InvalidDataException("Arsip berisi tipe file khusus yang tidak didukung.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            tarExpanded += CopyWithLimit(tarEntry.DataStream ?? Stream.Null, output, cancellationToken, MaximumExpandedBytes - tarExpanded);
        }
    }

    private static void Create(string target, string sourceDirectory, string logicalBaseName, string output, CancellationToken cancellationToken)
    {
        if (target == "zip")
        {
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
            // Gunakan logicalBaseName (dari nama file input) bukan Path.GetFileName(staging GUID)
            var baseName = logicalBaseName;
            foreach (var path in Directory.EnumerateFileSystemEntries(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    continue;
                var relative = Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/');
                var entryName = $"{baseName}/{relative}";
                if (Directory.Exists(path))
                {
                    archive.CreateEntry(entryName.TrimEnd('/') + "/");
                    continue;
                }
                archive.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
            }
            return;
        }

        using var outputFile = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Stream tarStream = target == "gzip" ? new GZipStream(outputFile, CompressionLevel.Optimal, leaveOpen: true) : outputFile;
        using (tarStream)
        using (var writer = new TarWriter(tarStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            // Gunakan logicalBaseName (dari nama file input) bukan Path.GetFileName(staging GUID)
            var baseName = logicalBaseName;
            var entries = Directory.EnumerateFileSystemEntries(sourceDirectory, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            foreach (var path in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    continue;
                var relative = Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/');
                var name = $"{baseName}/{relative}";
                if (Directory.Exists(path))
                {
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name));
                    continue;
                }
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = File.OpenRead(path)
                };
                using (entry.DataStream)
                    writer.WriteEntry(entry);
            }
        }
    }

    private static string SelectContentRoot(string staging)
    {
        var children = Directory.GetFileSystemEntries(staging);
        return children.Length == 1 && Directory.Exists(children[0]) ? children[0] : staging;
    }

    private static bool IsZipLink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static long CopyWithLimit(Stream source, Stream destination, CancellationToken cancellationToken, long remainingBytes)
    {
        var buffer = new byte[64 * 1024];
        long copied = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            copied += read;
            if (copied > remainingBytes)
                throw new InvalidDataException("Arsip melebihi batas ukuran ekstraksi 2 GB.");
            destination.Write(buffer, 0, read);
        }
        return copied;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}


using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace MikaNote.App;

public sealed class LocalBackupService
{
    private const string BackupPrefix = "mikanote-backup-";
    private const string BackupExtension = ".zip";

    public IReadOnlyList<LocalBackupItem> ListBackups(string backupFolderPath)
    {
        if (string.IsNullOrWhiteSpace(backupFolderPath) || !Directory.Exists(backupFolderPath))
        {
            return Array.Empty<LocalBackupItem>();
        }

        return Directory
            .EnumerateFiles(backupFolderPath, $"{BackupPrefix}*{BackupExtension}", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Select(file => new LocalBackupItem(file.FullName, file.Name, file.CreationTime, file.Length))
            .ToArray();
    }

    public LocalBackupItem CreateBackup(string notesDirectory, string backupFolderPath)
    {
        if (string.IsNullOrWhiteSpace(notesDirectory) || !Directory.Exists(notesDirectory))
        {
            throw new InvalidOperationException("The notes folder could not be found.");
        }

        if (string.IsNullOrWhiteSpace(backupFolderPath))
        {
            throw new InvalidOperationException("The backup folder is not linked.");
        }

        Directory.CreateDirectory(backupFolderPath);

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string backupPath = Path.Combine(backupFolderPath, $"{BackupPrefix}{timestamp}{BackupExtension}");

        using FileStream zipStream = File.Create(backupPath);
        using ZipArchive archive = new(zipStream, ZipArchiveMode.Create);

        AddManifest(archive, notesDirectory);
        AddDirectoryToArchive(archive, notesDirectory, notesDirectory, backupFolderPath);

        FileInfo file = new(backupPath);
        return new LocalBackupItem(file.FullName, file.Name, file.CreationTime, file.Length);
    }

    public void RestoreBackup(string backupZipPath, string notesDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupZipPath) || !File.Exists(backupZipPath))
        {
            throw new InvalidOperationException("Select a backup file to restore.");
        }

        if (string.IsNullOrWhiteSpace(notesDirectory) || !Directory.Exists(notesDirectory))
        {
            throw new InvalidOperationException("The notes folder could not be found.");
        }

        string restoreTempDirectory = Path.Combine(Path.GetTempPath(), $"MikaNoteRestore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(restoreTempDirectory);

        try
        {
            ZipFile.ExtractToDirectory(backupZipPath, restoreTempDirectory);

            string safetyBackupPath = Path.Combine(
                Path.GetDirectoryName(notesDirectory) ?? notesDirectory,
                $"MikaNote-before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(notesDirectory, safetyBackupPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            ClearDirectory(notesDirectory);
            CopyDirectory(restoreTempDirectory, notesDirectory);
        }
        finally
        {
            if (Directory.Exists(restoreTempDirectory))
            {
                Directory.Delete(restoreTempDirectory, recursive: true);
            }
        }
    }

    private static void AddManifest(ZipArchive archive, string notesDirectory)
    {
        var manifest = new
        {
            app = "MikaNote",
            backupVersion = 1,
            createdAt = DateTimeOffset.Now,
            notesDirectory = notesDirectory,
            noteCount = Directory.EnumerateFiles(notesDirectory, "*.txt", SearchOption.AllDirectories).Count()
        };

        ZipArchiveEntry entry = archive.CreateEntry("mikanote-backup-manifest.json", CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static void AddDirectoryToArchive(
        ZipArchive archive,
        string rootDirectory,
        string currentDirectory,
        string backupFolderPath)
    {
        foreach (string filePath in Directory.EnumerateFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            string relativePath = Path.GetRelativePath(rootDirectory, filePath);
            if (relativePath.Equals("mikanote-backup-manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
        }

        foreach (string directoryPath in Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsSameOrChildPath(directoryPath, backupFolderPath))
            {
                continue;
            }

            AddDirectoryToArchive(archive, rootDirectory, directoryPath, backupFolderPath);
        }
    }

    private static void ClearDirectory(string directoryPath)
    {
        foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(filePath);
        }

        foreach (string childDirectory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            Directory.Delete(childDirectory, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            if (relativePath.Equals("mikanote-backup-manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetDirectory);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static bool IsSameOrChildPath(string path, string possibleParentPath)
    {
        if (string.IsNullOrWhiteSpace(possibleParentPath))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string parentPath = Path.GetFullPath(possibleParentPath).TrimEnd(Path.DirectorySeparatorChar);

        return fullPath.Equals(parentPath, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(parentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record LocalBackupItem(string FullPath, string Name, DateTime CreatedAt, long SizeBytes)
{
    public string DisplayName => $"{CreatedAt:yyyy-MM-dd HH:mm}  ({FormatSize(SizeBytes)})";

    private static string FormatSize(long sizeBytes)
    {
        if (sizeBytes >= 1024 * 1024)
        {
            return $"{sizeBytes / 1024d / 1024d:0.0} MB";
        }

        return $"{Math.Max(1, sizeBytes / 1024d):0} KB";
    }
}

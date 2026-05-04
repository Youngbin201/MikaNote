using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MikaNote.App;

public sealed class GoogleDriveDesktopService
{
    private const string BackupFolderName = "MikaNote Backups";
    private static readonly string[] KnownSyncFolderNames =
    {
        "My Drive",
        "Google Drive",
        "내 드라이브"
    };

    public bool IsInstalled()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] candidates =
        {
            Path.Combine(localAppData, "Google", "DriveFS"),
            Path.Combine(programFiles, "Google", "Drive File Stream"),
            Path.Combine(programFiles, "Google", "DriveFS"),
            Path.Combine(programFilesX86, "Google", "Drive File Stream"),
            Path.Combine(programFilesX86, "Google", "DriveFS")
        };

        return candidates.Any(Directory.Exists);
    }

    public string? TryDetectRootFolder()
    {
        List<string> rankedCandidates = GetRankedCandidates();
        foreach (string candidate in rankedCandidates)
        {
            string? resolved = ResolveUsableRoot(candidate);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    public string EnsureBackupFolder(string selectedBaseFolder)
    {
        if (string.IsNullOrWhiteSpace(selectedBaseFolder))
        {
            throw new InvalidOperationException("A folder is required.");
        }

        string normalized = Path.GetFullPath(selectedBaseFolder.Trim());
        string folderPath = Path.GetFileName(normalized).Equals(BackupFolderName, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : Path.Combine(normalized, BackupFolderName);

        Directory.CreateDirectory(folderPath);
        return folderPath;
    }

    public void OpenInstallPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.google.com/drive/download/",
            UseShellExecute = true
        });
    }

    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folderPath}\"",
            UseShellExecute = true
        });
    }

    private static List<string> GetRankedCandidates()
    {
        List<string> candidates = new();
        candidates.AddRange(GetConfiguredPathCandidates());
        candidates.AddRange(GetCommonPathCandidates());
        candidates.AddRange(GetDriveCandidates());

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ScoreCandidate)
            .ToList();
    }

    private static IEnumerable<string> GetCommonPathCandidates()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        foreach (string name in KnownSyncFolderNames)
        {
            yield return Path.Combine(userProfile, name);
            yield return Path.Combine(documents, name);
        }
    }

    private static IEnumerable<string> GetDriveCandidates()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(static drive => drive.IsReady))
        {
            yield return drive.RootDirectory.FullName;

            foreach (string name in KnownSyncFolderNames)
            {
                yield return Path.Combine(drive.RootDirectory.FullName, name);
            }

            string label = SafeGetVolumeLabel(drive);
            if (!string.IsNullOrWhiteSpace(label) &&
                label.Contains("Google Drive", StringComparison.OrdinalIgnoreCase))
            {
                yield return drive.RootDirectory.FullName;

                IEnumerable<DirectoryInfo> childDirectories = Array.Empty<DirectoryInfo>();
                try
                {
                    childDirectories = drive.RootDirectory.EnumerateDirectories().ToArray();
                }
                catch
                {
                    // Ignore inaccessible mounted roots and keep trying other candidates.
                }

                foreach (DirectoryInfo directory in childDirectories)
                {
                    yield return directory.FullName;
                }
            }
        }
    }

    private static IEnumerable<string> GetConfiguredPathCandidates()
    {
        foreach (string configRoot in GetConfigRoots())
        {
            if (!Directory.Exists(configRoot))
            {
                continue;
            }

            foreach (string accountDirectory in Directory.EnumerateDirectories(configRoot))
            {
                string directoryName = Path.GetFileName(accountDirectory);
                if (string.Equals(directoryName, "Crashpad", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directoryName, "Logs", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directoryName, "webview2_user_data", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (string path in ExtractDirectoryPaths(accountDirectory))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> GetConfigRoots()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        yield return Path.Combine(localAppData, "Google", "DriveFS");
        yield return Path.Combine(roamingAppData, "Google", "DriveFS");
    }

    private static IEnumerable<string> ExtractDirectoryPaths(string accountDirectory)
    {
        string[] knownFiles =
        {
            "user_settings",
            "account_settings",
            "global_feature_config",
            "core_feature_config"
        };

        foreach (string fileName in knownFiles)
        {
            string filePath = Path.Combine(accountDirectory, fileName);
            if (!File.Exists(filePath))
            {
                continue;
            }

            foreach (string candidate in ExtractPathsFromFile(filePath))
            {
                yield return candidate;
            }
        }

        string localFoldersPath = Path.Combine(accountDirectory, "local_folders");
        if (!Directory.Exists(localFoldersPath))
        {
            yield break;
        }

        foreach (string folder in Directory.EnumerateDirectories(localFoldersPath))
        {
            yield return folder;
        }
    }

    private static IEnumerable<string> ExtractPathsFromFile(string filePath)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filePath);
        }
        catch
        {
            yield break;
        }

        if (bytes.Length == 0)
        {
            yield break;
        }

        string text = Encoding.UTF8.GetString(bytes);
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        Regex pathRegex = new(@"[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\?)*", RegexOptions.Compiled);
        foreach (Match match in pathRegex.Matches(text))
        {
            string candidate = NormalizePath(match.Value.Replace(@"\\", @"\"));
            if (Directory.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string? ResolveUsableRoot(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
        {
            return null;
        }

        string normalized = NormalizePath(candidate);
        string leafName = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar));
        if (KnownSyncFolderNames.Any(name => string.Equals(name, leafName, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        try
        {
            foreach (DirectoryInfo directory in new DirectoryInfo(normalized).EnumerateDirectories())
            {
                if (KnownSyncFolderNames.Any(name => string.Equals(name, directory.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return directory.FullName;
                }
            }
        }
        catch
        {
            // Ignore unreadable candidates.
        }

        if (normalized.Contains("Google Drive", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return null;
    }

    private static int ScoreCandidate(string candidate)
    {
        int score = 0;

        if (KnownSyncFolderNames.Any(name => candidate.EndsWith(name, StringComparison.OrdinalIgnoreCase)))
        {
            score += 100;
        }

        if (candidate.Contains("Google Drive", StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (candidate.Contains("DriveFS", StringComparison.OrdinalIgnoreCase))
        {
            score -= 40;
        }

        return score;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string SafeGetVolumeLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

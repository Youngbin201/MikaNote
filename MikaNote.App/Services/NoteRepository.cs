using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MikaNote.App;

public sealed class NoteRepository
{
    public const string DefaultNotesDirectory = @"C:\Users\Admin\Documents\MikaNote";
    private const string BackupDirectoryName = "trash";
    private const string HiddenDirectoryName = "hide";
    private const string LegacyBackupDirectoryName = "backup";

    private const double DefaultWidth = 320;
    private const double DefaultHeight = 220;
    private const double DefaultContentFontSize = 14;
    private const double BaseLeft = 60;
    private const double BaseTop = 60;
    private const double Offset = 36;
    private const string DefaultBackgroundColor = "#FFF4A0";

    private readonly string _notesDirectory;

    public NoteRepository(string notesDirectory)
    {
        _notesDirectory = notesDirectory;
        Directory.CreateDirectory(_notesDirectory);
        MigrateLegacyBackupDirectory();
    }

    public string NotesDirectory => _notesDirectory;

    public static string ResolveWritableDirectory()
    {
        string documentsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MikaNote");

        string localFallbackPath = Path.Combine(AppContext.BaseDirectory, "MikaNoteData");

        string[] candidates =
        {
            DefaultNotesDirectory,
            documentsPath,
            localFallbackPath
        };

        HashSet<string> uniqueCandidates = new(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !uniqueCandidates.Add(candidate))
            {
                continue;
            }

            if (TryEnsureWritableDirectory(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Writable storage path could not be created.");
    }

    public IReadOnlyList<NoteDocument> LoadAllNotes()
    {
        return LoadNotesFromDirectory(_notesDirectory, isBackup: false);
    }

    public IReadOnlyList<NoteDocument> LoadBackupNotes()
    {
        return LoadNotesFromDirectory(GetBackupDirectoryPath(), isBackup: true);
    }

    public IReadOnlyList<NoteDocument> LoadHiddenNotes()
    {
        return LoadNotesFromDirectory(GetHiddenDirectoryPath(), isBackup: false, isHidden: true);
    }

    public AppSettings LoadAppSettings()
    {
        string settingsPath = GetSettingsPath();
        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveAppSettings(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(GetSettingsPath(), json);
    }

    public NoteDocument CreateNewNote(AppSettings settings, NoteKind kind = NoteKind.Standard)
    {
        Directory.CreateDirectory(_notesDirectory);

        string uniquePath = BuildUniquePathForNewNote(kind);

        File.WriteAllText(uniquePath, string.Empty);

        int noteCount = Directory.GetFiles(_notesDirectory, "*.txt", SearchOption.TopDirectoryOnly).Length;
        (double left, double top) = BuildDefaultPosition(noteCount);

        NoteDocument note = new()
        {
            FilePath = uniquePath,
            Title = Path.GetFileNameWithoutExtension(uniquePath),
            Content = string.Empty,
            Kind = kind,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Left = left,
            Top = top,
            Width = DefaultWidth,
            Height = DefaultHeight,
            ContentFontSize = DefaultContentFontSize,
            BackgroundColor = string.IsNullOrWhiteSpace(settings.DefaultBackgroundColor)
                ? DefaultBackgroundColor
                : settings.DefaultBackgroundColor
        };

        SaveLayout(note, note.Left, note.Top, note.Width, note.Height);
        return note;
    }

    public void SaveNote(NoteDocument note, string requestedTitle, string content)
    {
        string finalTitle = NormalizeTitle(requestedTitle);
        if (string.IsNullOrWhiteSpace(finalTitle))
        {
            finalTitle = note.Title;
        }

        if (!string.Equals(finalTitle, note.Title, StringComparison.OrdinalIgnoreCase))
        {
            string newPath = BuildUniquePathForTitle(finalTitle, note.FilePath);
            string oldMetaPath = GetMetaPath(note.FilePath);
            string newMetaPath = GetMetaPath(newPath);

            if (!string.Equals(note.FilePath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(note.FilePath, newPath);
                if (File.Exists(oldMetaPath))
                {
                    File.Move(oldMetaPath, newMetaPath, overwrite: true);
                }
            }

            note.FilePath = newPath;
            note.Title = Path.GetFileNameWithoutExtension(newPath);
        }

        File.WriteAllText(note.FilePath, content);
        note.Content = content;
        note.ModifiedAt = DateTimeOffset.UtcNow;
        WriteLayout(note);
    }

    public void SaveLayout(NoteDocument note, double left, double top, double width, double height)
    {
        note.Left = left;
        note.Top = top;
        note.Width = width;
        note.Height = height;

        WriteLayout(note);
    }

    public void SaveAppearance(NoteDocument note, double contentFontSize, string backgroundColor)
    {
        note.ContentFontSize = contentFontSize;
        note.BackgroundColor = string.IsNullOrWhiteSpace(backgroundColor)
            ? DefaultBackgroundColor
            : backgroundColor;
        note.ModifiedAt = DateTimeOffset.UtcNow;

        WriteLayout(note);
    }

    public void DeleteNote(NoteDocument note)
    {
        string backupDirectory = GetBackupDirectoryPath();
        Directory.CreateDirectory(backupDirectory);
        note.FilePath = MoveNoteToDirectory(note, backupDirectory);
        note.IsBackup = true;
        note.IsHidden = false;
    }

    public NoteDocument RestoreBackupNote(NoteDocument note)
    {
        note.FilePath = MoveNoteToDirectory(note, _notesDirectory);
        note.IsBackup = false;
        note.IsHidden = false;
        return note;
    }

    public void HideNote(NoteDocument note)
    {
        string hiddenDirectory = GetHiddenDirectoryPath();
        Directory.CreateDirectory(hiddenDirectory);
        note.FilePath = MoveNoteToDirectory(note, hiddenDirectory);
        note.IsHidden = true;
        note.IsBackup = false;
    }

    public NoteDocument ShowHiddenNote(NoteDocument note)
    {
        note.FilePath = MoveNoteToDirectory(note, _notesDirectory);
        note.IsHidden = false;
        note.IsBackup = false;
        return note;
    }

    public void PermanentlyDeleteNote(NoteDocument note)
    {
        if (File.Exists(note.FilePath))
        {
            File.Delete(note.FilePath);
        }

        string metaPath = GetMetaPath(note.FilePath);
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }
    }

    private void WriteLayout(NoteDocument note)
    {
        NoteLayout layout = new()
        {
            Left = note.Left,
            Top = note.Top,
            Width = note.Width,
            Height = note.Height,
            CreatedAt = note.CreatedAt,
            ModifiedAt = note.ModifiedAt,
            ContentFontSize = note.ContentFontSize,
            BackgroundColor = note.BackgroundColor,
            Kind = note.Kind
        };

        string json = JsonSerializer.Serialize(layout, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(GetMetaPath(note.FilePath), json);
    }

    private IReadOnlyList<NoteDocument> LoadNotesFromDirectory(string directoryPath, bool isBackup, bool isHidden = false)
    {
        if (!Directory.Exists(directoryPath))
        {
            return Array.Empty<NoteDocument>();
        }

        string[] paths = Directory.GetFiles(directoryPath, "*.txt", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);

        List<NoteDocument> notes = new(paths.Length);
        for (int index = 0; index < paths.Length; index++)
        {
            notes.Add(LoadFromPath(paths[index], index, isBackup, isHidden));
        }

        return notes;
    }

    private NoteDocument LoadFromPath(string path, int index, bool isBackup, bool isHidden = false)
    {
        string content = string.Empty;
        try
        {
            content = File.ReadAllText(path);
        }
        catch
        {
            // Keep loading other notes even if one file has read issues.
        }

        NoteLayout? layout = null;
        string metaPath = GetMetaPath(path);
        if (File.Exists(metaPath))
        {
            try
            {
                string json = File.ReadAllText(metaPath);
                layout = JsonSerializer.Deserialize<NoteLayout>(json);
            }
            catch
            {
                layout = null;
            }
        }

        (double left, double top) = BuildDefaultPosition(index);
        DateTimeOffset createdAt = layout?.CreatedAt
            ?? new DateTimeOffset(File.GetCreationTimeUtc(path), TimeSpan.Zero);
        DateTimeOffset modifiedAt = layout?.ModifiedAt
            ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

        return new NoteDocument
        {
            FilePath = path,
            Title = Path.GetFileNameWithoutExtension(path),
            Content = content,
            Kind = layout?.Kind ?? NoteKind.Standard,
            IsBackup = isBackup,
            IsHidden = isHidden,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            Left = layout?.Left ?? left,
            Top = layout?.Top ?? top,
            Width = layout?.Width ?? DefaultWidth,
            Height = layout?.Height ?? DefaultHeight,
            ContentFontSize = layout?.ContentFontSize > 0 ? layout.ContentFontSize : DefaultContentFontSize,
            BackgroundColor = string.IsNullOrWhiteSpace(layout?.BackgroundColor) ? DefaultBackgroundColor : layout.BackgroundColor
        };
    }

    private static string NormalizeTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string title = value.Trim();
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            title = title.Replace(invalidCharacter.ToString(CultureInfo.InvariantCulture), string.Empty, StringComparison.Ordinal);
        }

        return title.Trim();
    }

    private string BuildUniquePathForTitle(string title, string? currentPath)
    {
        string normalized = NormalizeTitle(title);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "MikaMemo";
        }

        string candidatePath = Path.Combine(_notesDirectory, $"{normalized}.txt");
        if (IsAvailablePath(candidatePath, currentPath))
        {
            return candidatePath;
        }

        int counter = 2;
        while (true)
        {
            string path = Path.Combine(_notesDirectory, $"{normalized} ({counter}).txt");
            if (IsAvailablePath(path, currentPath))
            {
                return path;
            }

            counter++;
        }
    }

    private string BuildUniquePathForNewNote(NoteKind kind)
    {
        string baseName = kind == NoteKind.Todo ? "New Todo Memo" : "New Sticky Memo";
        string firstPath = Path.Combine(_notesDirectory, $"{baseName}.txt");
        if (!File.Exists(firstPath))
        {
            return firstPath;
        }

        int counter = 1;
        while (true)
        {
            string candidatePath = Path.Combine(_notesDirectory, $"{baseName} ({counter}).txt");
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }

            counter++;
        }
    }

    private static bool IsAvailablePath(string candidatePath, string? currentPath)
    {
        if (string.Equals(candidatePath, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !File.Exists(candidatePath);
    }

    private static string GetMetaPath(string txtPath)
    {
        return Path.ChangeExtension(txtPath, ".meta.json");
    }

    private string GetBackupDirectoryPath()
    {
        return Path.Combine(_notesDirectory, BackupDirectoryName);
    }

    private string GetHiddenDirectoryPath()
    {
        return Path.Combine(_notesDirectory, HiddenDirectoryName);
    }

    private string MoveNoteToDirectory(NoteDocument note, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        string originalNotePath = note.FilePath;
        string originalMetaPath = GetMetaPath(originalNotePath);
        string targetNotePath = BuildUniquePathInDirectory(Path.Combine(targetDirectory, Path.GetFileName(originalNotePath)));

        if (File.Exists(originalNotePath))
        {
            File.Move(originalNotePath, targetNotePath);
        }

        if (File.Exists(originalMetaPath))
        {
            File.Move(originalMetaPath, GetMetaPath(targetNotePath));
        }

        return targetNotePath;
    }

    private string GetLegacyBackupDirectoryPath()
    {
        return Path.Combine(_notesDirectory, LegacyBackupDirectoryName);
    }

    private string BuildUniqueBackupPath(string sourcePath)
    {
        string fileName = Path.GetFileName(sourcePath);
        string backupDirectory = GetBackupDirectoryPath();
        string candidatePath = Path.Combine(backupDirectory, fileName);
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath);
        string extension = Path.GetExtension(sourcePath);
        int counter = 1;
        while (true)
        {
            string numberedPath = Path.Combine(backupDirectory, $"{fileNameWithoutExtension} ({counter}){extension}");
            if (!File.Exists(numberedPath))
            {
                return numberedPath;
            }

            counter++;
        }
    }

    private void MigrateLegacyBackupDirectory()
    {
        string legacyDirectory = GetLegacyBackupDirectoryPath();
        if (!Directory.Exists(legacyDirectory))
        {
            return;
        }

        string trashDirectory = GetBackupDirectoryPath();
        Directory.CreateDirectory(trashDirectory);

        foreach (string legacyNotePath in Directory.GetFiles(legacyDirectory, "*.txt", SearchOption.TopDirectoryOnly))
        {
            string targetNotePath = BuildUniquePathInDirectory(Path.Combine(trashDirectory, Path.GetFileName(legacyNotePath)));
            File.Move(legacyNotePath, targetNotePath);

            string legacyMetaPath = GetMetaPath(legacyNotePath);
            if (File.Exists(legacyMetaPath))
            {
                File.Move(legacyMetaPath, GetMetaPath(targetNotePath));
            }
        }

        foreach (string legacyMetaPath in Directory.GetFiles(legacyDirectory, "*.meta.json", SearchOption.TopDirectoryOnly))
        {
            string targetMetaPath = BuildUniquePathInDirectory(Path.Combine(trashDirectory, Path.GetFileName(legacyMetaPath)));
            File.Move(legacyMetaPath, targetMetaPath);
        }

        if (!Directory.EnumerateFileSystemEntries(legacyDirectory).Any())
        {
            Directory.Delete(legacyDirectory, recursive: false);
        }
    }

    private static string BuildUniquePathInDirectory(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);
        int counter = 1;

        while (true)
        {
            string numberedPath = Path.Combine(directory, $"{fileNameWithoutExtension} ({counter}){extension}");
            if (!File.Exists(numberedPath))
            {
                return numberedPath;
            }

            counter++;
        }
    }

    private string GetSettingsPath()
    {
        return Path.Combine(_notesDirectory, "MikaNote.settings.json");
    }

    private static bool TryEnsureWritableDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            string probePath = Path.Combine(directoryPath, ".mikanote_probe");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (double left, double top) BuildDefaultPosition(int index)
    {
        double left = BaseLeft + (index % 10) * Offset;
        double top = BaseTop + (index % 8) * Offset;
        return (left, top);
    }

    private sealed class NoteLayout
    {
        public double Left { get; init; }
        public double Top { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public DateTimeOffset? CreatedAt { get; init; }
        public DateTimeOffset? ModifiedAt { get; init; }
        public double ContentFontSize { get; init; }
        public string? BackgroundColor { get; init; }
        public NoteKind? Kind { get; init; }
    }
}

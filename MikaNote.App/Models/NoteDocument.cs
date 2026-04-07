namespace MikaNote.App;

public enum NoteKind
{
    Standard,
    Todo
}

public sealed class NoteDocument
{
    public required string FilePath { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public NoteKind Kind { get; set; }
    public bool IsBackup { get; set; }
    public bool IsHidden { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double ContentFontSize { get; set; }
    public string BackgroundColor { get; set; } = "#FFF4A0";
}

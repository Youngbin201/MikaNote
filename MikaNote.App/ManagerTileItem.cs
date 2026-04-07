using System.Windows;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace MikaNote.App;

public enum ManagerTileActionKind
{
    None,
    CreateSticky,
    CreateTodo,
    EmptyTrash
}

public sealed class ManagerTileItem
{
    public string Key { get; init; } = string.Empty;
    public NoteDocument? Note { get; init; }
    public ManagerTileActionKind ActionKind { get; init; }
    public bool IsSelected { get; init; }
    public bool IsAction => ActionKind != ManagerTileActionKind.None;
    public string Title { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public string RelativeTimeText { get; init; } = string.Empty;
    public string Badge { get; init; } = string.Empty;
    public MediaBrush BackgroundBrush { get; init; } = MediaBrushes.White;
    public MediaBrush TitleBrush { get; init; } = MediaBrushes.Black;
    public MediaBrush ContentBrush { get; init; } = MediaBrushes.Black;
    public CornerRadius CornerRadius { get; init; }
    public double TitleFontSize { get; init; }
    public double ContentFontSize { get; init; }
    public double TitleLineHeight { get; init; }
    public double ContentLineHeight { get; init; }
    public string ActionGlyph { get; init; } = "+";
    public string ActionGlyphFontFamily { get; init; } = "Segoe UI";
    public double ActionGlyphFontSize { get; init; } = 48;
    public string ActionLabel { get; init; } = string.Empty;
}

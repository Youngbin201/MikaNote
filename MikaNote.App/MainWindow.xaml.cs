using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace MikaNote.App;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly ObservableCollection<ManagerTileItem> _todoTiles = new();
    private readonly ObservableCollection<ManagerTileItem> _memoTiles = new();
    private readonly ObservableCollection<ManagerTileItem> _backupTiles = new();
    private bool _isRefreshingUi;
    private bool _showGlobalSettings;
    private string? _selectedTileKey;

    private static readonly PresetOption[] FontSizePresets =
    {
        new("12", 12d),
        new("14", 14d),
        new("16", 16d),
        new("18", 18d),
        new("22", 22d)
    };

    private static readonly PresetOption[] LineSpacingPresets =
    {
        new("1.0", 1.0d),
        new("1.2", 1.2d),
        new("1.3", 1.3d),
        new("1.5", 1.5d),
        new("1.8", 1.8d)
    };

    private static readonly PresetOption[] BackgroundPresets =
    {
        new("Warm Yellow", "#FFF4A0"),
        new("Peach Cream", "#F9DCC4"),
        new("Soft Apricot", "#FEC89A"),
        new("Blush Sand", "#FBC4AB"),
        new("Apricot", "#FFD6A5"),
        new("Rose", "#FFB4A2"),
        new("Butter", "#FAEDCB"),
        new("Mint Fog", "#C9E4DE"),
        new("Sage Paper", "#D8E2DC"),
        new("Mint", "#CDEAC0"),
        new("Powder Blue", "#BDE0FE"),
        new("Sky", "#A9DEF9"),
        new("Lavender", "#E4C1F9"),
        new("Graphite", "#3A3A3A"),
        new("White", "#FFFFFF")
    };

    public MainWindow(App app)
    {
        InitializeComponent();

        _app = app;
        _app.NotesChanged += App_NotesChanged;
        _app.SettingsChanged += App_SettingsChanged;

        GlobalTitleFontSizeComboBox.ItemsSource = FontSizePresets;
        GlobalContentFontSizeComboBox.ItemsSource = FontSizePresets;
        GlobalTitleLineSpacingComboBox.ItemsSource = LineSpacingPresets;
        GlobalContentLineSpacingComboBox.ItemsSource = LineSpacingPresets;
        GlobalDefaultBackgroundColorComboBox.ItemsSource = BackgroundPresets;

        TodoTilesItemsControl.ItemsSource = _todoTiles;
        MemoTilesItemsControl.ItemsSource = _memoTiles;
        BackupTilesItemsControl.ItemsSource = _backupTiles;

        BuildSelectedBackgroundPresetButtons();
        LoadSettingsIntoUi();
        RebuildTiles(selectKey: null);
        UpdatePanelModeUi();
        RefreshSelectionUi();
    }

    protected override void OnClosed(EventArgs e)
    {
        _app.NotesChanged -= App_NotesChanged;
        _app.SettingsChanged -= App_SettingsChanged;
        base.OnClosed(e);
    }

    private void App_NotesChanged(object? sender, EventArgs e)
    {
        RebuildTiles(_selectedTileKey);
        RefreshSelectionUi();
    }

    private void App_SettingsChanged(object? sender, EventArgs e)
    {
        LoadSettingsIntoUi();
        RebuildTiles(_selectedTileKey);
        UpdatePanelModeUi();
        RefreshSelectionUi();
    }

    private void LoadSettingsIntoUi()
    {
        _isRefreshingUi = true;
        CornerRadiusSlider.Value = _app.Settings.NoteCornerRadius;
        CornerRadiusValueTextBlock.Text = Math.Round(_app.Settings.NoteCornerRadius).ToString("0");
        GlobalTitleFontSizeComboBox.SelectedValue = _app.Settings.TitleFontSize;
        GlobalContentFontSizeComboBox.SelectedValue = _app.Settings.ContentFontSize;
        GlobalTitleLineSpacingComboBox.SelectedValue = _app.Settings.TitleLineSpacing;
        GlobalContentLineSpacingComboBox.SelectedValue = _app.Settings.ContentLineSpacing;
        GlobalDefaultBackgroundColorComboBox.SelectedValue = _app.Settings.DefaultBackgroundColor;
        _isRefreshingUi = false;
    }

    private void RebuildTiles(string? selectKey)
    {
        _isRefreshingUi = true;

        _todoTiles.Clear();
        _memoTiles.Clear();
        _backupTiles.Clear();

        foreach (NoteDocument note in _app.Notes
                     .Where(note => note.Kind == NoteKind.Todo)
                     .OrderBy(note => note.Title, StringComparer.OrdinalIgnoreCase))
        {
            _todoTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (NoteDocument note in _app.HiddenNotes
                     .Where(note => note.Kind == NoteKind.Todo)
                     .OrderBy(note => note.Title, StringComparer.OrdinalIgnoreCase))
        {
            _todoTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
        }

        _todoTiles.Add(BuildActionTile(ManagerTileActionKind.CreateTodo));

        foreach (NoteDocument note in _app.Notes
                     .Where(note => note.Kind == NoteKind.Standard)
                     .OrderBy(note => note.Title, StringComparer.OrdinalIgnoreCase))
        {
            _memoTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (NoteDocument note in _app.HiddenNotes
                     .Where(note => note.Kind == NoteKind.Standard)
                     .OrderBy(note => note.Title, StringComparer.OrdinalIgnoreCase))
        {
            _memoTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
        }

        _memoTiles.Add(BuildActionTile(ManagerTileActionKind.CreateSticky));

        foreach (NoteDocument note in _app.BackupNotes
                     .OrderBy(note => note.Kind == NoteKind.Todo ? 0 : 1)
                     .ThenBy(note => note.Title, StringComparer.OrdinalIgnoreCase))
        {
            _backupTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
        }

        if (_app.BackupNotes.Count > 0)
        {
            _backupTiles.Add(BuildActionTile(ManagerTileActionKind.EmptyTrash));
        }

        _selectedTileKey = GetSelectableTiles()
            .Select(tile => tile.Key)
            .FirstOrDefault(key => string.Equals(key, selectKey, StringComparison.OrdinalIgnoreCase))
            ?? GetSelectableTiles().FirstOrDefault()?.Key;

        _isRefreshingUi = false;
    }

    private ManagerTileItem BuildActionTile(ManagerTileActionKind actionKind)
    {
        string label = actionKind switch
        {
            ManagerTileActionKind.CreateTodo => "New Todo",
            ManagerTileActionKind.CreateSticky => "New Note",
            ManagerTileActionKind.EmptyTrash => "Empty Trash",
            _ => string.Empty
        };

        string glyph = actionKind == ManagerTileActionKind.EmptyTrash ? "\uE74D" : "+";
        string glyphFontFamily = actionKind == ManagerTileActionKind.EmptyTrash ? "Segoe Fluent Icons" : "Segoe UI";
        double glyphFontSize = actionKind == ManagerTileActionKind.EmptyTrash ? 38 : 48;
        return new ManagerTileItem
        {
            Key = $"action::{actionKind}",
            ActionKind = actionKind,
            IsSelected = false,
            Title = label,
            ActionLabel = label,
            ActionGlyph = glyph,
            ActionGlyphFontFamily = glyphFontFamily,
            ActionGlyphFontSize = glyphFontSize,
            Badge = string.Empty,
            RelativeTimeText = string.Empty,
            BackgroundBrush = new MediaSolidColorBrush(MediaColor.FromRgb(248, 244, 236)),
            TitleBrush = new MediaSolidColorBrush(MediaColor.FromRgb(46, 42, 36)),
            ContentBrush = new MediaSolidColorBrush(MediaColor.FromRgb(107, 98, 85)),
            CornerRadius = new CornerRadius(Math.Max(8, _app.Settings.NoteCornerRadius)),
            TitleFontSize = _app.Settings.TitleFontSize,
            ContentFontSize = _app.Settings.ContentFontSize,
            TitleLineHeight = _app.Settings.TitleFontSize * _app.Settings.TitleLineSpacing,
            ContentLineHeight = _app.Settings.ContentFontSize * _app.Settings.ContentLineSpacing
        };
    }

    private ManagerTileItem BuildNoteTile(NoteDocument note, bool isSelected)
    {
        MediaColor backgroundColor = ParseBackgroundColor(note.BackgroundColor);
        if (note.IsHidden)
        {
            backgroundColor = DarkenColor(backgroundColor, 0.92);
        }

        MediaBrush foregroundBrush = CloneBrushWithOpacity(BuildForegroundBrush(backgroundColor), note.IsHidden ? 0.82 : 1.0);
        MediaBrush previewBrush = CloneBrushWithOpacity(foregroundBrush, note.IsHidden ? 0.68 : 0.88);

        return new ManagerTileItem
        {
            Key = BuildNoteKey(note),
            Note = note,
            IsSelected = isSelected,
            Title = note.Title,
            PreviewText = BuildPreviewText(note),
            RelativeTimeText = FormatRelativeAge(note.ModifiedAt),
            Badge = string.Empty,
            BackgroundBrush = new MediaSolidColorBrush(backgroundColor),
            TitleBrush = foregroundBrush,
            ContentBrush = previewBrush,
            CornerRadius = new CornerRadius(Math.Max(4, _app.Settings.NoteCornerRadius)),
            TitleFontSize = _app.Settings.TitleFontSize,
            ContentFontSize = _app.Settings.ContentFontSize,
            TitleLineHeight = _app.Settings.TitleFontSize * _app.Settings.TitleLineSpacing,
            ContentLineHeight = _app.Settings.ContentFontSize * _app.Settings.ContentLineSpacing
        };
    }

    private static string BuildNoteKey(NoteDocument note)
    {
        return $"note::{note.FilePath}";
    }

    private IEnumerable<ManagerTileItem> GetSelectableTiles()
    {
        foreach (ManagerTileItem tile in _todoTiles.Where(tile => !tile.IsAction))
        {
            yield return tile;
        }

        foreach (ManagerTileItem tile in _memoTiles.Where(tile => !tile.IsAction))
        {
            yield return tile;
        }

        foreach (ManagerTileItem tile in _backupTiles.Where(tile => !tile.IsAction))
        {
            yield return tile;
        }
    }

    private NoteDocument? GetSelectedNote()
    {
        return GetSelectableTiles()
            .FirstOrDefault(tile => string.Equals(tile.Key, _selectedTileKey, StringComparison.OrdinalIgnoreCase))
            ?.Note;
    }

    private void RefreshSelectionUi()
    {
        _isRefreshingUi = true;

        NoteDocument? selected = GetSelectedNote();
        bool hasSelection = selected is not null;
        bool isBackup = selected?.IsBackup == true;
        bool isHidden = selected?.IsHidden == true;

        ToggleHiddenButton.Visibility = hasSelection && !isBackup ? Visibility.Visible : Visibility.Collapsed;
        DeleteNoteButton.Visibility = hasSelection && !isBackup ? Visibility.Visible : Visibility.Collapsed;
        RestoreBackupButton.Visibility = hasSelection && isBackup ? Visibility.Visible : Visibility.Collapsed;
        DeleteBackupPermanentlyButton.Visibility = hasSelection && isBackup ? Visibility.Visible : Visibility.Collapsed;

        if (selected is null)
        {
            SelectedMetaTextBlock.Text = "Select a note tile.";
            SelectedTitleTextBlock.Text = "-";
            SelectedModifiedTextBlock.Text = "-";
            SelectedCreatedTextBlock.Text = "-";
            SelectedPreviewTextBlock.Text = string.Empty;
            SetBackgroundPresetButtonsEnabled(false, null);
            RestoreBackupButton.Visibility = Visibility.Collapsed;
            ToggleHiddenButton.Content = "Hide Note";
            _isRefreshingUi = false;
            return;
        }

        SelectedMetaTextBlock.Text = selected.IsBackup
            ? $"Trash {(selected.Kind == NoteKind.Todo ? "todo" : "sticky")} memo"
            : selected.IsHidden
                ? $"Hidden {(selected.Kind == NoteKind.Todo ? "todo" : "sticky")} memo"
            : $"{(selected.Kind == NoteKind.Todo ? "Todo" : "Sticky")} memo";
        SelectedTitleTextBlock.Text = selected.Title;
        SelectedModifiedTextBlock.Text = FormatRelativeAge(selected.ModifiedAt);
        SelectedCreatedTextBlock.Text = FormatRelativeAge(selected.CreatedAt);
        ApplySelectedPreviewSizing();
        SelectedPreviewTextBlock.Text = BuildSelectedPreviewText(selected);
        SelectedPreviewScrollViewer.ScrollToTop();
        SetBackgroundPresetButtonsEnabled(!isBackup, selected.BackgroundColor);
        ToggleHiddenButton.Content = isHidden ? "Show Note" : "Hide Note";

        _isRefreshingUi = false;
    }

    private void UpdatePanelModeUi()
    {
        SelectedNotePanel.Visibility = _showGlobalSettings ? Visibility.Collapsed : Visibility.Visible;
        GlobalSettingsPanel.Visibility = _showGlobalSettings ? Visibility.Visible : Visibility.Collapsed;

        UpdateTabButtonVisual(SelectedNoteTabButton, !_showGlobalSettings, isLeft: true);
        UpdateTabButtonVisual(GlobalSettingsTabButton, _showGlobalSettings, isLeft: false);
    }

    private void TileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingUi || sender is not System.Windows.Controls.Button button || button.Tag is not ManagerTileItem tile)
        {
            return;
        }

        if (tile.IsAction)
        {
            if (tile.ActionKind == ManagerTileActionKind.EmptyTrash)
            {
                EmptyTrashButton_Click(sender, e);
                return;
            }

            NoteDocument created = tile.ActionKind == ManagerTileActionKind.CreateTodo
                ? _app.CreateAndOpenNewTodoNote()
                : _app.CreateAndOpenNewNote();

            RebuildTiles(BuildNoteKey(created));
            RefreshSelectionUi();
            return;
        }

        _selectedTileKey = tile.Key;
        RebuildTiles(_selectedTileKey);
        RefreshSelectionUi();
    }

    private void SelectedNoteTabButton_Click(object sender, RoutedEventArgs e)
    {
        _showGlobalSettings = false;
        UpdatePanelModeUi();
    }

    private void GlobalSettingsTabButton_Click(object sender, RoutedEventArgs e)
    {
        _showGlobalSettings = true;
        UpdatePanelModeUi();
    }

    private void OpenNotesFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _app.OpenNotesDirectory();
    }

    private void ToggleHiddenButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNote() is not NoteDocument selected || selected.IsBackup)
        {
            return;
        }

        if (selected.IsHidden)
        {
            NoteDocument? shown = _app.ShowHiddenNote(selected);
            if (shown is not null)
            {
                RebuildTiles(BuildNoteKey(shown));
                RefreshSelectionUi();
            }

            return;
        }

        _app.HideNote(selected);
        RebuildTiles(BuildNoteKey(selected));
        RefreshSelectionUi();
    }

    private void SelectedBackgroundPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingUi ||
            sender is not WpfButton button ||
            button.Tag is not string backgroundColor ||
            GetSelectedNote() is not NoteDocument selected ||
            selected.IsBackup)
        {
            return;
        }

        _app.UpdateNoteBackgroundFromManager(selected, backgroundColor);
        RebuildTiles(BuildNoteKey(selected));
        RefreshSelectionUi();
    }

    private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNote() is not NoteDocument selected || selected.IsBackup)
        {
            return;
        }

        _app.DeleteNote(selected);
        RebuildTiles(BuildNoteKey(selected));
        RefreshSelectionUi();
    }

    private void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNote() is not NoteDocument selected || !selected.IsBackup)
        {
            return;
        }

        NoteDocument? restored = _app.RestoreBackupNote(selected);
        if (restored is null)
        {
            return;
        }

        RebuildTiles(BuildNoteKey(restored));
        RefreshSelectionUi();
    }

    private void DeleteBackupPermanentlyButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNote() is not NoteDocument selected || !selected.IsBackup)
        {
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"Permanently delete trash item '{selected.Title}'?",
            "Delete Trash Item",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _app.PermanentlyDeleteBackupNote(selected);
        RebuildTiles(selectKey: null);
        RefreshSelectionUi();
    }

    private void EmptyTrashButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_app.BackupNotes.Count == 0)
        {
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            "Permanently delete every item in Trash?",
            "Empty Trash",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _app.EmptyTrash();
        RebuildTiles(selectKey: null);
        RefreshSelectionUi();
    }

    private void GlobalFontSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingUi)
        {
            return;
        }

        ApplyGlobalSettingsFromUi();
    }

    private void CornerRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        CornerRadiusValueTextBlock.Text = Math.Round(e.NewValue).ToString("0");

        if (_isRefreshingUi)
        {
            return;
        }

        ApplyGlobalSettingsFromUi();
    }

    private void ApplyGlobalSettingsFromUi()
    {
        double titleFontSize = GlobalTitleFontSizeComboBox.SelectedValue is double titleValue
            ? titleValue
            : _app.Settings.TitleFontSize;

        double contentFontSize = GlobalContentFontSizeComboBox.SelectedValue is double contentValue
            ? contentValue
            : _app.Settings.ContentFontSize;

        double titleLineSpacing = GlobalTitleLineSpacingComboBox.SelectedValue is double titleLineValue
            ? titleLineValue
            : _app.Settings.TitleLineSpacing;

        double contentLineSpacing = GlobalContentLineSpacingComboBox.SelectedValue is double contentLineValue
            ? contentLineValue
            : _app.Settings.ContentLineSpacing;

        string defaultBackgroundColor = GlobalDefaultBackgroundColorComboBox.SelectedValue as string
            ?? _app.Settings.DefaultBackgroundColor;

        _app.UpdateGlobalSettings(
            Math.Round(CornerRadiusSlider.Value),
            titleFontSize,
            contentFontSize,
            titleLineSpacing,
            contentLineSpacing,
            defaultBackgroundColor);
    }

    private static string BuildPreviewText(NoteDocument note)
    {
        if (string.IsNullOrWhiteSpace(note.Content))
        {
            return note.Kind == NoteKind.Todo ? "[x] Add todo items" : "(empty)";
        }

        if (note.Kind == NoteKind.Todo)
        {
            string[] todoLines = note.Content
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Select(line =>
                {
                    bool isChecked = line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase);
                    if (line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase) || line.StartsWith("[ ]", StringComparison.OrdinalIgnoreCase))
                    {
                        line = line[3..].TrimStart();
                    }

                    return isChecked ? $"[o] {line}" : $"[x] {line}";
                })
                .Take(4)
                .ToArray();

            return string.Join(Environment.NewLine, todoLines);
        }

        string normalized = note.Content.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        string[] lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(4)
            .ToArray();

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildSelectedPreviewText(NoteDocument note)
    {
        if (string.IsNullOrWhiteSpace(note.Content))
        {
            return note.Kind == NoteKind.Todo ? "No todo items yet." : "(empty)";
        }

        string normalized = note.Content.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (note.Kind != NoteKind.Todo)
        {
            return normalized;
        }

        string[] todoLines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line =>
            {
                bool isChecked = line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase);
                if (line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase) || line.StartsWith("[ ]", StringComparison.OrdinalIgnoreCase))
                {
                    line = line[3..].TrimStart();
                }

                return isChecked ? $"[o] {line}" : $"[x] {line}";
            })
            .ToArray();

        return string.Join(Environment.NewLine, todoLines);
    }

    private void BuildSelectedBackgroundPresetButtons()
    {
        SelectedBackgroundPresetPanel.Children.Clear();

        foreach (PresetOption preset in BackgroundPresets)
        {
            WpfButton button = new()
            {
                Style = (Style)FindResource("SelectedColorChipButtonStyle"),
                Tag = preset.Value,
                ToolTip = preset.Label,
                Background = new MediaSolidColorBrush(ParseBackgroundColor(preset.Value.ToString() ?? "#FFF4A0"))
            };
            button.Click += SelectedBackgroundPresetButton_Click;
            SelectedBackgroundPresetPanel.Children.Add(button);
        }
    }

    private void SetBackgroundPresetButtonsEnabled(bool isEnabled, string? selectedColor)
    {
        foreach (WpfButton button in SelectedBackgroundPresetPanel.Children.OfType<WpfButton>())
        {
            string? buttonColor = button.Tag as string;
            bool isSelected = !string.IsNullOrWhiteSpace(selectedColor) &&
                              string.Equals(buttonColor, selectedColor, StringComparison.OrdinalIgnoreCase);

            button.IsEnabled = isEnabled;
            button.Opacity = isEnabled ? 1.0 : 0.4;
            button.BorderBrush = isSelected
                ? new MediaSolidColorBrush(MediaColor.FromRgb(46, 42, 36))
                : new MediaSolidColorBrush(MediaColor.FromRgb(205, 197, 184));
            button.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
        }
    }

    private void ApplySelectedPreviewSizing()
    {
        double contentFontSize = _app.Settings.ContentFontSize;
        double lineHeight = contentFontSize * _app.Settings.ContentLineSpacing;

        SelectedPreviewTextBlock.FontSize = contentFontSize;
        SelectedPreviewTextBlock.LineHeight = lineHeight;

        double verticalPadding = SelectedPreviewScrollViewer.Padding.Top + SelectedPreviewScrollViewer.Padding.Bottom;
        double borderThickness = SelectedPreviewBorder.BorderThickness.Top + SelectedPreviewBorder.BorderThickness.Bottom;
        SelectedPreviewBorder.Height = (lineHeight * 5) + verticalPadding + borderThickness + 4;
    }

    private static MediaColor ParseBackgroundColor(string colorValue)
    {
        try
        {
            return (MediaColor)MediaColorConverter.ConvertFromString(colorValue);
        }
        catch
        {
            return (MediaColor)MediaColorConverter.ConvertFromString("#FFF4A0");
        }
    }

    private static MediaBrush BuildForegroundBrush(MediaColor backgroundColor)
    {
        return IsDarkColor(backgroundColor) ? MediaBrushes.White : MediaBrushes.Black;
    }

    private static bool IsDarkColor(MediaColor color)
    {
        double brightness = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return brightness < 145;
    }

    private static MediaBrush CloneBrushWithOpacity(MediaBrush source, double opacity)
    {
        MediaBrush clone = source.CloneCurrentValue();
        clone.Opacity = opacity;
        if (clone.CanFreeze)
        {
            clone.Freeze();
        }

        return clone;
    }

    private static MediaColor DarkenColor(MediaColor color, double factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        return MediaColor.FromArgb(
            color.A,
            (byte)Math.Clamp((int)Math.Round(color.R * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.G * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.B * factor), 0, 255));
    }

    private static string FormatRelativeAge(DateTimeOffset createdAt)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - createdAt.ToUniversalTime();
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }

        if (elapsed.TotalHours < 1)
        {
            int minutes = Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes));
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed.TotalDays < 1)
        {
            int hours = Math.Max(1, (int)Math.Floor(elapsed.TotalHours));
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        if (elapsed.TotalDays < 30)
        {
            int days = Math.Max(1, (int)Math.Floor(elapsed.TotalDays));
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }

        if (elapsed.TotalDays < 365)
        {
            int months = Math.Max(1, (int)Math.Floor(elapsed.TotalDays / 30));
            return months == 1 ? "1 month ago" : $"{months} months ago";
        }

        int years = Math.Max(1, (int)Math.Floor(elapsed.TotalDays / 365));
        return years == 1 ? "1 year ago" : $"{years} years ago";
    }

    private static void UpdateTabButtonVisual(System.Windows.Controls.Button button, bool isActive, bool isLeft)
    {
        button.Background = isActive
            ? new MediaSolidColorBrush(MediaColor.FromRgb(255, 253, 247))
            : new MediaSolidColorBrush(MediaColor.FromRgb(221, 214, 200));
        button.Foreground = isActive
            ? new MediaSolidColorBrush(MediaColor.FromRgb(46, 42, 36))
            : new MediaSolidColorBrush(MediaColor.FromRgb(110, 101, 89));
        button.BorderBrush = MediaBrushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(0);
        button.Opacity = 1.0;
        button.Margin = isLeft
            ? new Thickness(0, 0, 0, 0)
            : new Thickness(0, 0, 0, 0);
    }

    private sealed class PresetOption
    {
        public PresetOption(string label, object value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }
        public object Value { get; }
    }
}


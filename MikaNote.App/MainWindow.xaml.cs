using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    private enum NoteFilterMode
    {
        All,
        Active,
        Hidden,
        Trash
    }

    private enum NoteSortMode
    {
        ModifiedAt,
        CreatedAt,
        Title
    }

    private readonly App _app;
    private readonly ObservableCollection<ManagerTileItem> _todoTiles = new();
    private readonly ObservableCollection<ManagerTileItem> _memoTiles = new();
    private readonly ObservableCollection<ManagerTileItem> _backupTiles = new();
    private readonly LocalBackupService _localBackupService = new();
    private readonly DispatcherTimer _quickBackupStatusTimer;
    private bool _isRefreshingUi;
    private bool _isSelectedNoteEditing;
    private bool _showGlobalSettings;
    private NoteFilterMode _noteFilterMode = NoteFilterMode.All;
    private string? _selectedTileKey;
    private NoteSortMode _noteSortMode = NoteSortMode.ModifiedAt;

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
        new("Warm Yellow", "#FFF4A0", "#FFF4A0"),
        new("Peach Cream", "#F9DCC4", "#F9DCC4"),
        new("Soft Apricot", "#FEC89A", "#FEC89A"),
        new("Blush Sand", "#FBC4AB", "#FBC4AB"),
        new("Apricot", "#FFD6A5", "#FFD6A5"),
        new("Rose", "#FFB4A2", "#FFB4A2"),
        new("Butter", "#FAEDCB", "#FAEDCB"),
        new("Mint Fog", "#C9E4DE", "#C9E4DE"),
        new("Sage Paper", "#D8E2DC", "#D8E2DC"),
        new("Mint", "#CDEAC0", "#CDEAC0"),
        new("Powder Blue", "#BDE0FE", "#BDE0FE"),
        new("Sky", "#A9DEF9", "#A9DEF9"),
        new("Lavender", "#E4C1F9", "#E4C1F9"),
        new("Graphite", "#3A3A3A", "#3A3A3A"),
        new("White", "#FFFFFF", "#FFFFFF")
    };

    private static readonly DarknessPreset[] HiddenNoteDarknessPresets =
    {
        new("Barely Dimmed", 0.98, "#F5F6F7"),
        new("Soft Dim", 0.94, "#DDE2E6"),
        new("Balanced", 0.90, "#C3CAD1"),
        new("Deep Dim", 0.84, "#A5AEB8"),
        new("Muted", 0.76, "#7D8792")
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

        _quickBackupStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.1)
        };
        _quickBackupStatusTimer.Tick += (_, _) =>
        {
            _quickBackupStatusTimer.Stop();
            UpdateQuickBackupButtonUi();
        };

        TodoTilesItemsControl.ItemsSource = _todoTiles;
        MemoTilesItemsControl.ItemsSource = _memoTiles;
        BackupTilesItemsControl.ItemsSource = _backupTiles;

        BuildSelectedBackgroundPresetButtons();
        BuildHiddenNoteDarknessPresetButtons();
        LoadSettingsIntoUi();
        RebuildTiles(selectKey: null);
        UpdatePanelModeUi();
        UpdateSortModeButtons();
        UpdateFilterButtons();
        UpdateQuickBackupButtonUi();
        RefreshSelectionUi();
        Deactivated += (_, _) => CommitSelectedNoteEditIfNeeded();
    }

    protected override void OnClosed(EventArgs e)
    {
        _app.NotesChanged -= App_NotesChanged;
        _app.SettingsChanged -= App_SettingsChanged;
        _quickBackupStatusTimer.Stop();
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
        UpdateQuickBackupButtonUi();
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
        SetHiddenNoteDarknessPresetSelection(_app.Settings.HiddenNoteDarknessFactor);
        _isRefreshingUi = false;
    }

    private void RebuildTiles(string? selectKey)
    {
        _isRefreshingUi = true;

        _todoTiles.Clear();
        _memoTiles.Clear();
        _backupTiles.Clear();

        bool showActiveNotes = _noteFilterMode is NoteFilterMode.All or NoteFilterMode.Active;
        bool showHiddenNotes = _noteFilterMode is NoteFilterMode.All or NoteFilterMode.Hidden;
        bool showTrashNotes = _noteFilterMode is NoteFilterMode.All or NoteFilterMode.Trash;

        if (showActiveNotes)
        {
            foreach (NoteDocument note in SortNotes(_app.Notes.Where(note => note.Kind == NoteKind.Todo)))
            {
                _todoTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (showHiddenNotes)
        {
            foreach (NoteDocument note in SortNotes(_app.HiddenNotes.Where(note => note.Kind == NoteKind.Todo)))
            {
                _todoTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (_noteFilterMode is NoteFilterMode.All or NoteFilterMode.Active)
        {
            _todoTiles.Add(BuildActionTile(ManagerTileActionKind.CreateTodo));
        }

        if (showActiveNotes)
        {
            foreach (NoteDocument note in SortNotes(_app.Notes.Where(note => note.Kind == NoteKind.Standard)))
            {
                _memoTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (showHiddenNotes)
        {
            foreach (NoteDocument note in SortNotes(_app.HiddenNotes.Where(note => note.Kind == NoteKind.Standard)))
            {
                _memoTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (_noteFilterMode is NoteFilterMode.All or NoteFilterMode.Active)
        {
            _memoTiles.Add(BuildActionTile(ManagerTileActionKind.CreateSticky));
        }

        if (showTrashNotes)
        {
            foreach (NoteDocument note in SortNotes(_app.BackupNotes))
            {
                _backupTiles.Add(BuildNoteTile(note, string.Equals(BuildNoteKey(note), selectKey, StringComparison.OrdinalIgnoreCase)));
            }

            if (_app.BackupNotes.Count > 0)
            {
                _backupTiles.Add(BuildActionTile(ManagerTileActionKind.EmptyTrash));
            }
        }

        TodoSectionHeader.Visibility = _noteFilterMode == NoteFilterMode.Trash ? Visibility.Collapsed : Visibility.Visible;
        TodoTilesItemsControl.Visibility = _noteFilterMode == NoteFilterMode.Trash ? Visibility.Collapsed : Visibility.Visible;
        MemoSectionHeader.Visibility = _noteFilterMode == NoteFilterMode.Trash ? Visibility.Collapsed : Visibility.Visible;
        MemoTilesItemsControl.Visibility = _noteFilterMode == NoteFilterMode.Trash ? Visibility.Collapsed : Visibility.Visible;
        TrashSectionHeader.Visibility = showTrashNotes ? Visibility.Visible : Visibility.Collapsed;
        BackupTilesItemsControl.Visibility = showTrashNotes ? Visibility.Visible : Visibility.Collapsed;

        _selectedTileKey = GetSelectableTiles()
            .Select(tile => tile.Key)
            .FirstOrDefault(key => string.Equals(key, selectKey, StringComparison.OrdinalIgnoreCase))
            ?? GetSelectableTiles().FirstOrDefault()?.Key;

        _isRefreshingUi = false;
    }

    private IEnumerable<NoteDocument> SortNotes(IEnumerable<NoteDocument> notes)
    {
        return _noteSortMode switch
        {
            NoteSortMode.CreatedAt => notes
                .OrderByDescending(note => note.IsFavorite)
                .ThenByDescending(note => note.CreatedAt)
                .ThenBy(note => note.Title, StringComparer.OrdinalIgnoreCase),
            NoteSortMode.Title => notes
                .OrderByDescending(note => note.IsFavorite)
                .ThenBy(note => note.Title, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(note => note.ModifiedAt),
            _ => notes
                .OrderByDescending(note => note.IsFavorite)
                .ThenByDescending(note => note.ModifiedAt)
                .ThenBy(note => note.Title, StringComparer.OrdinalIgnoreCase)
        };
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
            IsFavorite = false,
            IsBackup = false,
            Title = label,
            ActionLabel = label,
            ActionGlyph = glyph,
            ActionGlyphFontFamily = glyphFontFamily,
            ActionGlyphFontSize = glyphFontSize,
            Badge = string.Empty,
            RelativeTimeText = string.Empty,
            BackgroundBrush = new MediaSolidColorBrush(MediaColor.FromRgb(250, 247, 240)),
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
            backgroundColor = DarkenColor(backgroundColor, _app.Settings.HiddenNoteDarknessFactor);
        }

        MediaBrush foregroundBrush = CloneBrushWithOpacity(BuildForegroundBrush(backgroundColor), note.IsHidden ? 0.82 : 1.0);
        MediaBrush previewBrush = CloneBrushWithOpacity(foregroundBrush, note.IsHidden ? 0.68 : 0.88);

        return new ManagerTileItem
        {
            Key = BuildNoteKey(note),
            Note = note,
            IsSelected = isSelected,
            IsFavorite = note.IsFavorite,
            IsBackup = note.IsBackup,
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
        UpdateFavoriteButtonUi(selected);

        if (selected is null)
        {
            _isSelectedNoteEditing = false;
            SelectedMetaTextBlock.Text = "Select a note tile.";
            SelectedTitleTextBlock.Text = "-";
            SelectedTitleEditor.Text = string.Empty;
            SelectedModifiedTextBlock.Text = "-";
            SelectedCreatedTextBlock.Text = "-";
            SelectedPreviewTextBlock.Text = string.Empty;
            SelectedContentEditor.Text = string.Empty;
            SetBackgroundPresetButtonsEnabled(false, null);
            RestoreBackupButton.Visibility = Visibility.Collapsed;
            ToggleHiddenButton.Content = "Hide Note";
            UpdateSelectedNoteEditorUi();
            _isRefreshingUi = false;
            return;
        }

        SelectedMetaTextBlock.Text = selected.IsBackup
            ? $"Trash {(selected.Kind == NoteKind.Todo ? "todo" : "sticky")} memo"
            : selected.IsHidden
                ? $"Hidden {(selected.Kind == NoteKind.Todo ? "todo" : "sticky")} memo"
            : $"{(selected.Kind == NoteKind.Todo ? "Todo" : "Sticky")} memo";
        SelectedTitleTextBlock.Text = selected.Title;
        if (!_isSelectedNoteEditing)
        {
            SelectedTitleEditor.Text = selected.Title;
            SelectedContentEditor.Text = BuildManagerEditableContent(selected);
        }
        SelectedModifiedTextBlock.Text = FormatRelativeAge(selected.ModifiedAt);
        SelectedCreatedTextBlock.Text = FormatRelativeAge(selected.CreatedAt);
        ApplySelectedPreviewSizing();
        SelectedPreviewTextBlock.Text = BuildSelectedPreviewText(selected);
        SelectedPreviewScrollViewer.ScrollToTop();
        SetBackgroundPresetButtonsEnabled(!isBackup, selected.BackgroundColor);
        ToggleHiddenButton.Content = isHidden ? "Show Note" : "Hide Note";
        UpdateSelectedNoteEditorUi();

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

    private void SelectedTitleTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BeginSelectedNoteEdit(focusContent: false);
            e.Handled = true;
        }
    }

    private void SelectedPreviewScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BeginSelectedNoteEdit(focusContent: true);
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelectedNoteEditing)
        {
            return;
        }

        DependencyObject? source = e.OriginalSource as DependencyObject;
        if (IsDescendantOf(source, SelectedTitleEditor) || IsDescendantOf(source, SelectedContentEditor))
        {
            return;
        }

        CommitSelectedNoteEditIfNeeded();
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

    private void QuickBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsBackupFolderLinked())
        {
            _showGlobalSettings = true;
            UpdatePanelModeUi();
            OpenBackupSettingsWindow();
            return;
        }

        try
        {
            LocalBackupItem backup = _localBackupService.CreateBackup(_app.NotesDirectory, _app.Settings.GoogleDriveBackupFolderPath);
            ShowQuickBackupStatus($"Backed up {backup.CreatedAt:HH:mm}", success: true);
        }
        catch (Exception exception)
        {
            ShowQuickBackupStatus("Backup failed", success: false);
            MessageBox.Show(
                $"Could not create a backup.\n{exception.Message}",
                "MikaNote Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void FavoriteToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNote() is not NoteDocument selected || selected.IsBackup)
        {
            return;
        }

        _app.SetFavorite(selected, !selected.IsFavorite);
        RebuildTiles(BuildNoteKey(selected));
        RefreshSelectionUi();
    }

    private void TileFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingUi ||
            sender is not System.Windows.Controls.Button button ||
            button.Tag is not ManagerTileItem tile ||
            tile.Note is not NoteDocument note ||
            note.IsBackup)
        {
            return;
        }

        _app.SetFavorite(note, !note.IsFavorite);
        _selectedTileKey = tile.Key;
        RebuildTiles(_selectedTileKey);
        RefreshSelectionUi();
        e.Handled = true;
    }

    private void TileDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingUi ||
            sender is not System.Windows.Controls.Button button ||
            button.Tag is not ManagerTileItem tile ||
            tile.Note is not NoteDocument note)
        {
            return;
        }

        if (note.IsBackup)
        {
            NoteDocument? restored = _app.RestoreBackupNote(note);
            RebuildTiles(restored is null ? null : BuildNoteKey(restored));
            RefreshSelectionUi();
            e.Handled = true;
            return;
        }

        _app.DeleteNote(note);
        RebuildTiles(selectKey: null);
        RefreshSelectionUi();
        e.Handled = true;
    }

    private void SortByModifiedButton_Click(object sender, RoutedEventArgs e)
    {
        SetNoteSortMode(NoteSortMode.ModifiedAt);
    }

    private void SortByCreatedButton_Click(object sender, RoutedEventArgs e)
    {
        SetNoteSortMode(NoteSortMode.CreatedAt);
    }

    private void SortByTitleButton_Click(object sender, RoutedEventArgs e)
    {
        SetNoteSortMode(NoteSortMode.Title);
    }

    private void FilterAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetNoteFilterMode(NoteFilterMode.All);
    }

    private void FilterActiveButton_Click(object sender, RoutedEventArgs e)
    {
        SetNoteFilterMode(NoteFilterMode.Active);
    }

    private void FilterHiddenButton_Click(object sender, RoutedEventArgs e)
    {
        SetNoteFilterMode(NoteFilterMode.Hidden);
    }

    private void FilterTrashButton_Click(object sender, RoutedEventArgs e)
    {
        SetNoteFilterMode(NoteFilterMode.Trash);
    }

    private void GoogleDriveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenBackupSettingsWindow();
    }

    private void OpenBackupSettingsWindow()
    {
        GoogleDriveFolderWindow dialog = new(_app)
        {
            Owner = this
        };
        dialog.ShowDialog();
        UpdateQuickBackupButtonUi();
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

        double hiddenNoteDarknessFactor = GetSelectedHiddenNoteDarknessFactor();

        _app.UpdateGlobalSettings(
            Math.Round(CornerRadiusSlider.Value),
            titleFontSize,
            contentFontSize,
            titleLineSpacing,
            contentLineSpacing,
            defaultBackgroundColor,
            hiddenNoteDarknessFactor);
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

    private void BeginSelectedNoteEdit(bool focusContent)
    {
        NoteDocument? selected = GetSelectedNote();
        if (selected is null || selected.IsBackup)
        {
            return;
        }

        _isSelectedNoteEditing = true;
        SelectedTitleEditor.Text = selected.Title;
        SelectedContentEditor.Text = BuildManagerEditableContent(selected);
        UpdateSelectedNoteEditorUi();

        if (focusContent)
        {
            SelectedContentEditor.Focus();
            SelectedContentEditor.CaretIndex = SelectedContentEditor.Text.Length;
            return;
        }

        SelectedTitleEditor.Focus();
        SelectedTitleEditor.SelectAll();
    }

    private void CommitSelectedNoteEditIfNeeded()
    {
        if (!_isSelectedNoteEditing)
        {
            return;
        }

        NoteDocument? selected = GetSelectedNote();
        _isSelectedNoteEditing = false;

        if (selected is null || selected.IsBackup)
        {
            UpdateSelectedNoteEditorUi();
            return;
        }

        string updatedTitle = SelectedTitleEditor.Text ?? string.Empty;
        string updatedContent = SelectedContentEditor.Text ?? string.Empty;

        if (string.Equals(updatedTitle, selected.Title, StringComparison.Ordinal)
            && string.Equals(updatedContent, BuildManagerEditableContent(selected), StringComparison.Ordinal))
        {
            RefreshSelectionUi();
            return;
        }

        _app.SaveNoteFromManager(selected, updatedTitle, updatedContent, selected.BackgroundColor);
    }

    private void UpdateSelectedNoteEditorUi()
    {
        Visibility readOnlyVisibility = _isSelectedNoteEditing ? Visibility.Collapsed : Visibility.Visible;
        Visibility editVisibility = _isSelectedNoteEditing ? Visibility.Visible : Visibility.Collapsed;

        SelectedTitleTextBlock.Visibility = readOnlyVisibility;
        SelectedTitleEditor.Visibility = editVisibility;
        SelectedPreviewScrollViewer.Visibility = readOnlyVisibility;
        SelectedPreviewEditorBorder.Visibility = editVisibility;
    }

    private void BuildHiddenNoteDarknessPresetButtons()
    {
        HiddenNoteDarknessPresetPanel.Children.Clear();

        foreach (DarknessPreset preset in HiddenNoteDarknessPresets)
        {
            WpfButton button = new()
            {
                Style = (Style)FindResource("InactiveDarknessPresetButtonStyle"),
                Tag = preset.Factor,
                ToolTip = preset.Label,
                Background = new MediaSolidColorBrush(ParseBackgroundColor(preset.SwatchColor))
            };
            button.Click += HiddenNoteDarknessPresetButton_Click;
            HiddenNoteDarknessPresetPanel.Children.Add(button);
        }
    }

    private void HiddenNoteDarknessPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingUi || sender is not WpfButton button || button.Tag is not double factor)
        {
            return;
        }

        SetHiddenNoteDarknessPresetSelection(factor);
        ApplyGlobalSettingsFromUi();
    }

    private double GetSelectedHiddenNoteDarknessFactor()
    {
        foreach (WpfButton button in HiddenNoteDarknessPresetPanel.Children.OfType<WpfButton>())
        {
            if (button.BorderThickness.Left >= 2 && button.Tag is double factor)
            {
                return factor;
            }
        }

        return _app.Settings.HiddenNoteDarknessFactor;
    }

    private void SetHiddenNoteDarknessPresetSelection(double selectedFactor)
    {
        foreach (WpfButton button in HiddenNoteDarknessPresetPanel.Children.OfType<WpfButton>())
        {
            bool isSelected = button.Tag is double factor && Math.Abs(factor - selectedFactor) < 0.001;
            button.BorderBrush = isSelected
                ? new MediaSolidColorBrush(MediaColor.FromRgb(45, 48, 51))
                : new MediaSolidColorBrush(MediaColor.FromRgb(216, 208, 195));
            button.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
            button.Opacity = isSelected ? 1.0 : 0.82;
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
                : new MediaSolidColorBrush(MediaColor.FromRgb(216, 208, 195));
            button.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
        }
    }

    private void ApplySelectedPreviewSizing()
    {
        double contentFontSize = _app.Settings.ContentFontSize;
        double lineHeight = contentFontSize * _app.Settings.ContentLineSpacing;

        SelectedPreviewTextBlock.FontSize = contentFontSize;
        SelectedPreviewTextBlock.LineHeight = lineHeight;
        SelectedContentEditor.FontSize = contentFontSize;
        SelectedTitleEditor.FontSize = _app.Settings.TitleFontSize;
        SelectedContentEditor.SetValue(Block.LineHeightProperty, lineHeight);
        SelectedTitleEditor.SetValue(Block.LineHeightProperty, _app.Settings.TitleFontSize * _app.Settings.TitleLineSpacing);

        double verticalPadding = SelectedContentEditor.Padding.Top + SelectedContentEditor.Padding.Bottom;
        double borderThickness = SelectedPreviewEditorBorder.BorderThickness.Top + SelectedPreviewEditorBorder.BorderThickness.Bottom;
        double previewHeight = (lineHeight * 5) + verticalPadding + borderThickness + 4;
        SelectedPreviewScrollViewer.Height = previewHeight;
        SelectedPreviewEditorBorder.Height = previewHeight;
    }

    private static string BuildManagerEditableContent(NoteDocument note)
    {
        return note.Content;
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = GetParentDependencyObject(node);
        }

        return false;
    }

    private static DependencyObject? GetParentDependencyObject(DependencyObject node)
    {
        if (node is System.Windows.Media.Visual || node is System.Windows.Media.Media3D.Visual3D)
        {
            return System.Windows.Media.VisualTreeHelper.GetParent(node);
        }

        if (node is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.Parent;
        }

        if (node is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement);
        }

        return null;
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

    private void SetNoteSortMode(NoteSortMode sortMode)
    {
        if (_noteSortMode == sortMode)
        {
            return;
        }

        _noteSortMode = sortMode;
        UpdateSortModeButtons();
        RebuildTiles(_selectedTileKey);
        RefreshSelectionUi();
    }

    private void UpdateSortModeButtons()
    {
        UpdateSortModeButtonVisual(SortByModifiedButton, _noteSortMode == NoteSortMode.ModifiedAt);
        UpdateSortModeButtonVisual(SortByCreatedButton, _noteSortMode == NoteSortMode.CreatedAt);
        UpdateSortModeButtonVisual(SortByTitleButton, _noteSortMode == NoteSortMode.Title);
    }

    private void SetNoteFilterMode(NoteFilterMode filterMode)
    {
        if (_noteFilterMode == filterMode)
        {
            return;
        }

        _noteFilterMode = filterMode;
        UpdateFilterButtons();
        RebuildTiles(_selectedTileKey);
        RefreshSelectionUi();
    }

    private void UpdateFilterButtons()
    {
        UpdateSortModeButtonVisual(FilterAllButton, _noteFilterMode == NoteFilterMode.All);
        UpdateSortModeButtonVisual(FilterActiveButton, _noteFilterMode == NoteFilterMode.Active);
        UpdateSortModeButtonVisual(FilterHiddenButton, _noteFilterMode == NoteFilterMode.Hidden);
        UpdateSortModeButtonVisual(FilterTrashButton, _noteFilterMode == NoteFilterMode.Trash);
    }

    private static void UpdateSortModeButtonVisual(System.Windows.Controls.Button button, bool isSelected)
    {
        button.Background = isSelected
            ? new MediaSolidColorBrush(MediaColor.FromRgb(255, 253, 248))
            : new MediaSolidColorBrush(MediaColor.FromRgb(244, 241, 234));
        button.Foreground = isSelected
            ? new MediaSolidColorBrush(MediaColor.FromRgb(46, 42, 36))
            : new MediaSolidColorBrush(MediaColor.FromRgb(95, 87, 74));
        button.BorderBrush = new MediaSolidColorBrush(MediaColor.FromRgb(216, 208, 195));
        button.BorderThickness = isSelected ? new Thickness(1.5) : new Thickness(1);
    }

    private bool IsBackupFolderLinked()
    {
        return _app.Settings.GoogleDriveFolderLinked &&
               !string.IsNullOrWhiteSpace(_app.Settings.GoogleDriveBackupFolderPath) &&
               Directory.Exists(_app.Settings.GoogleDriveBackupFolderPath);
    }

    private void UpdateQuickBackupButtonUi()
    {
        bool linked = IsBackupFolderLinked();

        QuickBackupTextBlock.Text = linked ? "Backup" : "Setup Backup";
        QuickBackupButton.ToolTip = linked
            ? "Back up notes now"
            : "Connect a backup folder";

        QuickBackupButton.Background = linked
            ? new MediaSolidColorBrush(MediaColor.FromRgb(255, 253, 248))
            : new MediaSolidColorBrush(MediaColor.FromRgb(244, 241, 234));
        QuickBackupButton.Foreground = linked
            ? new MediaSolidColorBrush(MediaColor.FromRgb(46, 42, 36))
            : new MediaSolidColorBrush(MediaColor.FromRgb(95, 87, 74));
        QuickBackupButton.BorderBrush = linked
            ? new MediaSolidColorBrush(MediaColor.FromRgb(186, 177, 161))
            : new MediaSolidColorBrush(MediaColor.FromRgb(216, 208, 195));

        QuickBackupArrowIcon.Visibility = Visibility.Visible;
        QuickBackupSettingsIcon.Visibility = Visibility.Collapsed;
        QuickBackupDoneIcon.Visibility = Visibility.Collapsed;
    }

    private void ShowQuickBackupStatus(string text, bool success)
    {
        _quickBackupStatusTimer.Stop();

        QuickBackupTextBlock.Text = text;
        QuickBackupButton.Background = new MediaSolidColorBrush(success
            ? MediaColor.FromRgb(228, 245, 234)
            : MediaColor.FromRgb(246, 223, 223));
        QuickBackupButton.Foreground = new MediaSolidColorBrush(success
            ? MediaColor.FromRgb(47, 112, 76)
            : MediaColor.FromRgb(160, 68, 68));
        QuickBackupButton.BorderBrush = new MediaSolidColorBrush(success
            ? MediaColor.FromRgb(114, 178, 139)
            : MediaColor.FromRgb(214, 94, 94));

        QuickBackupArrowIcon.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        QuickBackupSettingsIcon.Visibility = Visibility.Collapsed;
        QuickBackupDoneIcon.Visibility = success ? Visibility.Visible : Visibility.Collapsed;

        if (success)
        {
            DoubleAnimation popAnimation = new()
            {
                From = 1.0,
                To = 1.12,
                Duration = TimeSpan.FromMilliseconds(120),
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            QuickBackupButtonScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, popAnimation);
            QuickBackupButtonScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, popAnimation);
        }

        _quickBackupStatusTimer.Start();
    }

    private void UpdateFavoriteButtonUi(NoteDocument? selected)
    {
        bool canFavorite = selected is not null && !selected.IsBackup;
        bool isFavorite = selected?.IsFavorite == true;

        FavoriteToggleButton.IsEnabled = canFavorite;
        FavoriteToggleButton.ToolTip = !canFavorite
            ? "Favorites are available for active and hidden notes."
            : isFavorite
                ? "Remove from favorites"
                : "Add to favorites";
        FavoriteToggleButton.Background = canFavorite && isFavorite
            ? new MediaSolidColorBrush(MediaColor.FromRgb(255, 249, 235))
            : new MediaSolidColorBrush(MediaColor.FromRgb(250, 247, 240));
        FavoriteToggleButton.BorderBrush = canFavorite && isFavorite
            ? new MediaSolidColorBrush(MediaColor.FromRgb(224, 192, 102))
            : new MediaSolidColorBrush(MediaColor.FromRgb(216, 208, 195));
        FavoriteToggleButton.Foreground = new MediaSolidColorBrush(MediaColor.FromRgb(176, 138, 56));
        FavoriteToggleFilledIcon.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
        FavoriteToggleOutlineIcon.Visibility = isFavorite ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void UpdateTabButtonVisual(System.Windows.Controls.Button button, bool isActive, bool isLeft)
    {
        button.Background = isActive
            ? new MediaSolidColorBrush(MediaColor.FromRgb(255, 253, 248))
            : new MediaSolidColorBrush(MediaColor.FromRgb(216, 208, 195));
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
        public PresetOption(string label, object value, string? swatchColor = null)
        {
            Label = label;
            Value = value;
            SwatchColor = swatchColor;
        }

        public string Label { get; }
        public object Value { get; }
        public string? SwatchColor { get; }

        public override string ToString()
        {
            return Label;
        }
    }

    private sealed class DarknessPreset
    {
        public DarknessPreset(string label, double factor, string swatchColor)
        {
            Label = label;
            Factor = factor;
            SwatchColor = swatchColor;
        }

        public string Label { get; }
        public double Factor { get; }
        public string SwatchColor { get; }
    }
}


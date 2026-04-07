using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Panel = System.Windows.Controls.Panel;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace MikaNote.App;

internal enum SettingsPanelMode
{
    None,
    TextSize,
    BackgroundColor
}

public partial class NoteWindow : Window
{
    private static readonly Regex UrlRegex = new(@"((https?://|www\.)[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int SC_MAXIMIZE = 0xF030;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private readonly NoteDocument _note;
    private readonly NoteRepository _repository;
    private readonly DispatcherTimer _layoutSaveTimer;
    private readonly DispatcherTimer _actionBarCollapseTimer;
    private readonly ActionBarWindow _actionBarWindow;
    private readonly SettingsPanelWindow _settingsPanelWindow;
    private readonly TodoOrganizerWindow _todoOrganizerWindow;
    private readonly List<TodoItemData> _todoEditItems = new();

    private bool _isEditing;
    private bool _suspendLayoutSave;
    private bool _isTitleAreaHovered;
    private bool _isActionPopupHovered;
    private bool _isSettingsPopupHovered;
    private bool _isDesktopLayerAttached;
    private bool _isDeleting;
    private bool _isDragging;
    private bool _isUpdatingTransientUi;
    private bool _popupRefreshPending;
    private bool _isNewNoteMenuOpen;
    private bool _todoDragStarted;
    private bool _suppressNextTodoEditorGlobalClick;
    private bool _suppressNextEditModeGlobalClick;
    private SettingsPanelMode _settingsPanelMode;
    private string _originalTitle = string.Empty;
    private string _originalContent = string.Empty;
    private double _currentCornerRadius;
    private IntPtr _windowHandle;
    private nint _originalStyle;
    private nint _originalExStyle;
    private int? _todoDragIndex;
    private Point _todoDragStartPoint;

    private ActionBarWindow ActionBar => _actionBarWindow;
    private SettingsPanelWindow SettingsPanel => _settingsPanelWindow;
    private TodoOrganizerWindow TodoOrganizer => _todoOrganizerWindow;

    public string NoteFilePath => _note.FilePath;

    public NoteWindow(NoteDocument note, NoteRepository repository)
    {
        InitializeComponent();

        _note = note;
        _repository = repository;
        _actionBarWindow = new ActionBarWindow();
        _settingsPanelWindow = new SettingsPanelWindow();
        _todoOrganizerWindow = new TodoOrganizerWindow();

        _actionBarWindow.ManagerButton.Click += ManagerButton_Click;
        _actionBarWindow.NewNoteButton.Click += NewNoteButton_Click;
        _actionBarWindow.NewStickyMemoMenuItem.Click += NewStickyMemoMenuItem_Click;
        _actionBarWindow.NewTodoMemoMenuItem.Click += NewTodoMemoMenuItem_Click;
        _actionBarWindow.NewNoteContextMenu.Opened += NewNoteContextMenu_Opened;
        _actionBarWindow.NewNoteContextMenu.Closed += NewNoteContextMenu_Closed;
        _actionBarWindow.TextSizeButton.Click += TextSizeButton_Click;
        _actionBarWindow.ColorButton.Click += ColorButton_Click;
        _actionBarWindow.SaveButton.Click += SaveButton_Click;
        _actionBarWindow.CloseButton.Click += CloseButton_Click;
        _actionBarWindow.MouseEnter += ActionPopup_MouseEnter;
        _actionBarWindow.MouseLeave += ActionPopup_MouseLeave;

        _settingsPanelWindow.MouseEnter += SettingsPopup_MouseEnter;
        _settingsPanelWindow.MouseLeave += SettingsPopup_MouseLeave;

        HookButtonClicks(_settingsPanelWindow.TitleFontSizeOptionsPanel, FontSizeOption_Click);
        HookButtonClicks(_settingsPanelWindow.ContentFontSizeOptionsPanel, FontSizeOption_Click);
        HookButtonClicks(_settingsPanelWindow.TitleLineSpacingOptionsPanel, LineSpacingOption_Click);
        HookButtonClicks(_settingsPanelWindow.ContentLineSpacingOptionsPanel, LineSpacingOption_Click);
        HookButtonClicks(_settingsPanelWindow.ColorOptionsPanel, ColorOption_Click);

        _layoutSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _layoutSaveTimer.Tick += (_, _) =>
        {
            _layoutSaveTimer.Stop();
            SaveLayoutNow();
        };

        _actionBarCollapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _actionBarCollapseTimer.Tick += (_, _) =>
        {
            _actionBarCollapseTimer.Stop();
            UpdateActionBarState();
        };

        (double startLeft, double startTop, double startWidth, double startHeight) = NormalizeBoundsToVisibleDesktop(
            _note.Left,
            _note.Top,
            _note.Width,
            _note.Height);

        _suspendLayoutSave = true;
        Left = startLeft;
        Top = startTop;
        Width = startWidth;
        Height = startHeight;
        _suspendLayoutSave = false;

        RefreshReadonlyView();
        ApplyNoteAppearance();
        UpdateSettingsPanelState();
        UpdateActionBarState();

        if (Application.Current is App app)
        {
            ApplyGlobalSettings(app.Settings);
        }

        Loaded += (_, _) =>
        {
            SyncActionBarLayout();
        };
        SourceInitialized += NoteWindow_SourceInitialized;
        Deactivated += (_, _) => DismissTransientPanels();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
        };
        LocationChanged += (_, _) =>
        {
            if (_isDragging)
            {
                return;
            }

            ScheduleLayoutSave();
            SchedulePopupRefresh();
        };
        SizeChanged += (_, _) =>
        {
            ScheduleLayoutSave();
            SyncActionBarLayout();
            UpdateRoundedClip();
            SchedulePopupRefresh();
            RefreshDesktopHostSurface();
        };
        Closing += (_, _) =>
        {
            if (_isDeleting)
            {
                _actionBarWindow.ClosePermanently();
                _settingsPanelWindow.ClosePermanently();
                _todoOrganizerWindow.ClosePermanently();
                return;
            }

            if (_isEditing)
            {
                SaveAndExitEditMode();
            }

            SaveLayoutNow();
            _actionBarWindow.ClosePermanently();
            _settingsPanelWindow.ClosePermanently();
            _todoOrganizerWindow.ClosePermanently();
        };
    }

    private static void HookButtonClicks(Panel panel, RoutedEventHandler handler)
    {
        foreach (object child in panel.Children)
        {
            if (child is Button button)
            {
                button.Click += handler;
            }
        }
    }

    private void NoteWindow_SourceInitialized(object? sender, EventArgs e)
    {
        HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
        if (source is null)
        {
            return;
        }

        _windowHandle = source.Handle;
        _originalStyle = DesktopWindowHost.GetWindowStyle(_windowHandle);
        _originalExStyle = DesktopWindowHost.GetWindowExStyle(_windowHandle);
        source.AddHook(WndProc);

        ApplyShowDesktopBehavior(true);

        ApplyWindowRegion(source);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SYSCOMMAND)
        {
            int command = wParam.ToInt32() & 0xFFF0;
            if (command == SC_MAXIMIZE)
            {
                handled = true;
                return IntPtr.Zero;
            }
        }

        if (msg == WM_NCLBUTTONDBLCLK)
        {
            int hitTest = wParam.ToInt32();
            if (IsMaximizeRelatedHitTest(hitTest))
            {
                handled = true;
                return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    private void RefreshReadonlyView()
    {
        Title = _note.Title;
        TitleTextBlock.Text = _note.Title;
        RenderReadonlyContent();
    }

    private void EnterEditMode()
    {
        if (_isEditing)
        {
            return;
        }

        _isEditing = true;
        _suppressNextEditModeGlobalClick = true;

        _originalTitle = _note.Title;
        _originalContent = _note.Content;

        TitleEditor.Text = _note.Title;

        TitleTextBlock.Visibility = Visibility.Collapsed;
        ContentScrollViewer.Visibility = Visibility.Collapsed;

        TitleEditor.Visibility = Visibility.Visible;

        if (_note.Kind == NoteKind.Todo)
        {
            _todoEditItems.Clear();
            _todoEditItems.AddRange(ParseTodoItems(_note.Content));

            if (_todoEditItems.Count == 0)
            {
                _todoEditItems.Add(new TodoItemData(string.Empty, false));
            }

            ContentEditor.Visibility = Visibility.Collapsed;
            TodoEditorScrollViewer.Visibility = Visibility.Visible;
            RefreshTodoEditor();
        }
        else
        {
            ContentEditor.Text = _note.Content;
            ContentEditor.Visibility = Visibility.Visible;
            TodoEditorScrollViewer.Visibility = Visibility.Collapsed;
        }

        UpdateActionBarState();

        if (_note.Kind == NoteKind.Todo)
        {
            FocusFirstTodoEditor();
        }
        else
        {
            TitleEditor.Focus();
            TitleEditor.SelectAll();
        }
    }

    private void ApplyNoteAppearance()
    {
        Color backgroundColor = ParseBackgroundColor(_note.BackgroundColor);
        Brush backgroundBrush = new SolidColorBrush(backgroundColor);
        Brush textBrush = BuildForegroundBrush(backgroundColor);
        Brush editorBorderBrush = BuildEditorBorderBrush(backgroundColor);

        RootBorder.Background = backgroundBrush;
        TitleAreaRoot.Background = Brushes.Transparent;
        BodyAreaRoot.Background = Brushes.Transparent;

        TitleTextBlock.Foreground = textBrush;
        ContentTextBlock.Foreground = textBrush;
        TitleEditor.Foreground = textBrush;
        ContentEditor.Foreground = textBrush;
        TitleEditor.Background = backgroundBrush;
        ContentEditor.Background = backgroundBrush;
        TitleEditor.BorderBrush = editorBorderBrush;
        ContentEditor.BorderBrush = editorBorderBrush;
        TitleEditor.CaretBrush = textBrush;
        ContentEditor.CaretBrush = textBrush;
        TodoOrganizer.SetCornerRadius(Math.Max(0, _currentCornerRadius * 0.5));

        if (_isEditing && _note.Kind == NoteKind.Todo)
        {
            RefreshTodoEditor();
        }

        RenderReadonlyContent();
        UpdateSettingsSelectionVisuals();
    }

    public void RefreshFromNote()
    {
        RefreshReadonlyView();
        ApplyNoteAppearance();
    }

    public void ApplyGlobalSettings(AppSettings settings)
    {
        _currentCornerRadius = Math.Max(0, settings.NoteCornerRadius);
        TitleTextBlock.FontSize = settings.TitleFontSize;
        TitleEditor.FontSize = settings.TitleFontSize;
        ContentTextBlock.FontSize = settings.ContentFontSize;
        ContentEditor.FontSize = settings.ContentFontSize;
        double titleLineHeight = settings.TitleFontSize * settings.TitleLineSpacing;
        double contentLineHeight = settings.ContentFontSize * settings.ContentLineSpacing;
        TitleTextBlock.LineHeight = titleLineHeight;
        ContentTextBlock.LineHeight = contentLineHeight;
        TitleEditor.SetValue(Block.LineHeightProperty, titleLineHeight);
        ContentEditor.SetValue(Block.LineHeightProperty, contentLineHeight);
        RootBorder.CornerRadius = new CornerRadius(_currentCornerRadius);
        ActionBar.SetCornerRadius(Math.Max(0, _currentCornerRadius * 0.5));
        SettingsPanel.SetCornerRadius(Math.Max(0, _currentCornerRadius * 0.5));
        UpdateRoundedClip();
        ApplyShowDesktopBehavior(true);
        if (_isEditing && _note.Kind == NoteKind.Todo)
        {
            RefreshTodoEditor();
        }
        UpdateSettingsSelectionVisuals();
    }

    public void PrepareForDeletion()
    {
        _isDeleting = true;
    }

    private void RenderReadonlyContent()
    {
        ContentTextBlock.Inlines.Clear();
        TodoItemsPanel.Children.Clear();
        ContentScrollViewer.ScrollToVerticalOffset(0);

        if (_note.Kind == NoteKind.Todo)
        {
            RenderTodoContent();
            return;
        }

        ContentTextBlock.Visibility = Visibility.Visible;
        TodoItemsPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(_note.Content))
        {
            ContentTextBlock.Inlines.Add(new Run("(double-click to edit)"));
            return;
        }

        Brush defaultBrush = ContentTextBlock.Foreground;
        Brush hyperlinkBrush = BuildHyperlinkBrush(ParseBackgroundColor(_note.BackgroundColor));

        int currentIndex = 0;
        foreach (Match match in UrlRegex.Matches(_note.Content))
        {
            if (match.Index > currentIndex)
            {
                AppendTextFragment(ContentTextBlock.Inlines, _note.Content[currentIndex..match.Index], defaultBrush);
            }

            string rawUrl = match.Value;
            string navigateUrl = NormalizeUrl(rawUrl);

            Hyperlink hyperlink = new(new Run(rawUrl))
            {
                NavigateUri = Uri.TryCreate(navigateUrl, UriKind.Absolute, out Uri? uri) ? uri : null,
                Foreground = hyperlinkBrush,
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand
            };
            hyperlink.Click += Hyperlink_Click;
            ContentTextBlock.Inlines.Add(hyperlink);

            currentIndex = match.Index + match.Length;
        }

        if (currentIndex < _note.Content.Length)
        {
            AppendTextFragment(ContentTextBlock.Inlines, _note.Content[currentIndex..], defaultBrush);
        }
    }

    private void RenderTodoContent()
    {
        ContentTextBlock.Visibility = Visibility.Collapsed;
        TodoItemsPanel.Visibility = Visibility.Visible;

        List<TodoItemData> items = ParseTodoItems(_note.Content);
        if (items.Count == 0)
        {
            TodoItemsPanel.Children.Add(new TextBlock
            {
                Text = "(double-click to edit)",
                Foreground = ContentTextBlock.Foreground,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        Color backgroundColor = ParseBackgroundColor(_note.BackgroundColor);
        Brush textBrush = ContentTextBlock.Foreground;
        for (int index = 0; index < items.Count; index++)
        {
            TodoItemData item = items[index];
            Brush itemTextBrush = item.IsChecked
                ? CloneBrushWithOpacity(textBrush, 0.68)
                : textBrush;

            Border block = new()
            {
                Background = BuildTodoReadonlyBlockBrush(backgroundColor, item.IsChecked),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(Math.Max(6, _currentCornerRadius)),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Tag = index
            };
            block.PreviewMouseLeftButtonDown += TodoReadonlyBlock_PreviewMouseLeftButtonDown;

            Grid row = new();

            TextBlock text = new()
            {
                Text = item.Text,
                Foreground = itemTextBrush,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                TextDecorations = item.IsChecked ? TextDecorations.Strikethrough : null
            };
            text.SetBinding(TextBlock.FontSizeProperty, new System.Windows.Data.Binding(nameof(ContentTextBlock.FontSize)) { Source = ContentTextBlock });
            text.SetBinding(TextBlock.LineHeightProperty, new System.Windows.Data.Binding(nameof(ContentTextBlock.LineHeight)) { Source = ContentTextBlock });

            row.Children.Add(text);
            block.Child = row;
            TodoItemsPanel.Children.Add(block);
        }
    }

    private static void AppendTextFragment(InlineCollection inlines, string text, Brush foreground)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] parts = normalized.Split('\n');
        for (int index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length > 0)
            {
                inlines.Add(new Run(parts[index]) { Foreground = foreground });
            }

            if (index < parts.Length - 1)
            {
                inlines.Add(new LineBreak());
            }
        }
    }

    private static Brush CloneBrushWithOpacity(Brush source, double opacity)
    {
        Brush clone = source.CloneCurrentValue();
        clone.Opacity = opacity;
        if (clone.CanFreeze)
        {
            clone.Freeze();
        }

        return clone;
    }

    private void TodoReadonlyBlock_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border block || block.Tag is not int itemIndex)
        {
            return;
        }

        List<TodoItemData> items = ParseTodoItems(_note.Content);
        if (itemIndex < 0 || itemIndex >= items.Count)
        {
            return;
        }

        TodoItemData current = items[itemIndex];
        items[itemIndex] = current with { IsChecked = !current.IsChecked };

        string updatedContent = SerializeTodoItems(items);
        _repository.SaveNote(_note, _note.Title, updatedContent);
        RefreshReadonlyView();
        NotifyAppNotesChanged();
        e.Handled = true;
    }

    private static List<TodoItemData> ParseTodoItems(string rawContent)
    {
        List<TodoItemData> items = new();
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return items;
        }

        string[] lines = rawContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new TodoItemData(line[3..].TrimStart(), true));
            }
            else if (line.StartsWith("[ ]", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new TodoItemData(line[3..].TrimStart(), false));
            }
            else
            {
                items.Add(new TodoItemData(line, false));
            }
        }

        return items;
    }

    private static string SerializeTodoItems(IEnumerable<TodoItemData> items)
    {
        return string.Join(
            Environment.NewLine,
            items.Select(item => item.IsChecked ? $"[x] {item.Text}" : $"[ ] {item.Text}"));
    }

    private void RefreshTodoEditor()
    {
        if (!_isEditing || _note.Kind != NoteKind.Todo)
        {
            return;
        }

        TodoEditorPanel.Children.Clear();
        TodoOrganizer.ItemsPanel.Children.Clear();

        for (int index = 0; index < _todoEditItems.Count; index++)
        {
            TodoEditorPanel.Children.Add(CreateTodoEditorRow(index));
            TodoOrganizer.ItemsPanel.Children.Add(CreateTodoOrganizerRow(index));
        }

        TodoEditorPanel.Children.Add(CreateTodoAddButton());
        TodoOrganizer.ItemsPanel.Children.Add(CreateTodoOrganizerAddButton());
        UpdateTodoOrganizerState();
    }

    private Button CreateTodoAddButton()
    {
        double buttonWidth = Math.Max(120, Math.Round(Math.Max(120, ActualWidth - 36) * 0.7));
        Color backgroundColor = ParseBackgroundColor(_note.BackgroundColor);

        Button addButton = new()
        {
            Content = "+",
            Width = buttonWidth,
            Height = 34,
            FontSize = 20,
            FontWeight = FontWeights.Normal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderBrush = BuildTodoBorderBrush(backgroundColor),
            BorderThickness = new Thickness(1),
            Foreground = BuildForegroundBrush(backgroundColor),
            Style = (Style)FindResource("TodoAddButtonStyle"),
            Tag = _todoEditItems.Count
        };
        addButton.PreviewMouseLeftButtonDown += TodoEditorInternalControl_PreviewMouseLeftButtonDown;
        addButton.Click += TodoAddButton_Click;
        return addButton;
    }

    private Border CreateTodoEditorRow(int index)
    {
        Color backgroundColor = ParseBackgroundColor(_note.BackgroundColor);
        Border rowBorder = new()
        {
            Background = BuildTodoBlockBrush(backgroundColor),
            BorderBrush = BuildTodoBorderBrush(backgroundColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Max(6, _currentCornerRadius)),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Tag = index
        };

        Grid rowGrid = new();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        TextBox editor = new()
        {
            Text = _todoEditItems[index].Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = BuildForegroundBrush(backgroundColor),
            CaretBrush = BuildForegroundBrush(backgroundColor),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 12, 0),
            Tag = index
        };
        editor.SetBinding(TextBox.FontSizeProperty, new System.Windows.Data.Binding(nameof(ContentTextBlock.FontSize)) { Source = ContentTextBlock });
        editor.SetBinding(Block.LineHeightProperty, new System.Windows.Data.Binding(nameof(ContentTextBlock.LineHeight)) { Source = ContentTextBlock });
        editor.PreviewMouseLeftButtonDown += TodoEditorInternalControl_PreviewMouseLeftButtonDown;
        editor.TextChanged += TodoItemEditor_TextChanged;

        Grid.SetColumn(editor, 0);
        rowGrid.Children.Add(editor);
        rowBorder.Child = rowGrid;
        return rowBorder;
    }

    private Border CreateTodoOrganizerRow(int index)
    {
        Border rowBorder = new()
        {
            Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(88, 88, 88)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Max(5, _currentCornerRadius * 0.6)),
            Padding = new Thickness(5, 4, 5, 4),
            Margin = new Thickness(0, 0, 0, 4),
            MinHeight = 26,
            Tag = index,
            AllowDrop = true
        };
        rowBorder.PreviewMouseLeftButtonDown += TodoOrganizerRow_PreviewMouseLeftButtonDown;
        rowBorder.MouseMove += TodoOrganizerRow_MouseMove;
        rowBorder.Drop += TodoOrganizerRow_Drop;
        rowBorder.DragOver += TodoOrganizerRow_DragOver;

        Grid rowGrid = new();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bool isChecked = _todoEditItems[index].IsChecked;
        TextBlock stateText = new()
        {
            Text = isChecked ? "v" : "x",
            Foreground = isChecked ? Brushes.LimeGreen : Brushes.IndianRed,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock previewText = new()
        {
            Text = BuildTodoOrganizerPreviewText(_todoEditItems[index].Text),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brushes.White,
            FontSize = 10,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 40
        };

        Button removeButton = new()
        {
            Content = "X",
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(88, 88, 88)),
            BorderThickness = new Thickness(1),
            FontSize = 9,
            Tag = index
        };
        removeButton.PreviewMouseLeftButtonDown += TodoOrganizerInternalControl_PreviewMouseLeftButtonDown;
        removeButton.Click += TodoRemoveButton_Click;

        Grid.SetColumn(stateText, 0);
        Grid.SetColumn(previewText, 1);
        Grid.SetColumn(removeButton, 2);
        rowGrid.Children.Add(stateText);
        rowGrid.Children.Add(previewText);
        rowGrid.Children.Add(removeButton);
        rowBorder.Child = rowGrid;
        return rowBorder;
    }

    private Button CreateTodoOrganizerAddButton()
    {
        Button addButton = new()
        {
            Content = "+",
            Height = 26,
            Margin = new Thickness(0, 0, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(88, 88, 88)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            FontWeight = FontWeights.Normal,
            Tag = _todoEditItems.Count
        };
        addButton.PreviewMouseLeftButtonDown += TodoOrganizerInternalControl_PreviewMouseLeftButtonDown;
        addButton.Click += TodoAddButton_Click;
        return addButton;
    }

    private void TodoAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int insertIndex)
        {
            return;
        }

        int safeIndex = Math.Max(0, Math.Min(insertIndex, _todoEditItems.Count));
        _todoEditItems.Insert(safeIndex, new TodoItemData(string.Empty, false));
        RefreshTodoEditor();
        FocusTodoEditorAt(safeIndex);
    }

    private void TodoEditorInternalControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _suppressNextTodoEditorGlobalClick = true;
    }

    private void TodoOrganizerInternalControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _suppressNextTodoEditorGlobalClick = true;
    }

    private void TodoRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int itemIndex)
        {
            return;
        }

        if (itemIndex < 0 || itemIndex >= _todoEditItems.Count)
        {
            return;
        }

        _todoEditItems.RemoveAt(itemIndex);
        if (_todoEditItems.Count == 0)
        {
            _todoEditItems.Add(new TodoItemData(string.Empty, false));
        }

        RefreshTodoEditor();
        FocusTodoEditorAt(Math.Min(itemIndex, _todoEditItems.Count - 1));
    }

    private void TodoItemEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox editor || editor.Tag is not int itemIndex)
        {
            return;
        }

        if (itemIndex < 0 || itemIndex >= _todoEditItems.Count)
        {
            return;
        }

        TodoItemData current = _todoEditItems[itemIndex];
        _todoEditItems[itemIndex] = current with { Text = editor.Text };
    }

    private void TodoOrganizerRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border rowBorder || rowBorder.Tag is not int itemIndex)
        {
            return;
        }

        _todoDragIndex = itemIndex;
        _todoDragStarted = false;
        _todoDragStartPoint = e.GetPosition(this);
        _suppressNextTodoEditorGlobalClick = true;
    }

    private void TodoOrganizerRow_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isEditing || _todoDragIndex is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point currentPoint = e.GetPosition(this);
        Vector dragDelta = currentPoint - _todoDragStartPoint;
        if (_todoDragStarted
            || (Math.Abs(dragDelta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(dragDelta.Y) < SystemParameters.MinimumVerticalDragDistance))
        {
            return;
        }

        _todoDragStarted = true;
        DragDrop.DoDragDrop((DependencyObject)sender, _todoDragIndex.Value, System.Windows.DragDropEffects.Move);
        _todoDragIndex = null;
        _todoDragStarted = false;
    }

    private void TodoOrganizerRow_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(int)) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void TodoOrganizerRow_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Border rowBorder
            || rowBorder.Tag is not int targetIndex
            || !e.Data.GetDataPresent(typeof(int)))
        {
            return;
        }

        int sourceIndex = (int)e.Data.GetData(typeof(int))!;
        if (sourceIndex < 0 || sourceIndex >= _todoEditItems.Count || sourceIndex == targetIndex)
        {
            return;
        }

        Point dropPoint = e.GetPosition(rowBorder);
        int insertIndex = targetIndex;
        if (dropPoint.Y > rowBorder.ActualHeight / 2)
        {
            insertIndex++;
        }

        TodoItemData movedItem = _todoEditItems[sourceIndex];
        _todoEditItems.RemoveAt(sourceIndex);
        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        insertIndex = Math.Max(0, Math.Min(insertIndex, _todoEditItems.Count));
        _todoEditItems.Insert(insertIndex, movedItem);
        RefreshTodoEditor();
        FocusTodoEditorAt(insertIndex);
        e.Handled = true;
    }

    private static string BuildTodoOrganizerPreviewText(string text)
    {
        string singleLine = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (singleLine.Length == 0)
        {
            return "(empty)";
        }

        return singleLine.Length <= 16 ? singleLine : $"{singleLine[..16]}...";
    }

    private void UpdateTodoOrganizerState()
    {
        if (!_isEditing || _note.Kind != NoteKind.Todo)
        {
            TodoOrganizer.HidePane();
            return;
        }

        double organizerLeft = Left - TodoOrganizer.Width - 8;
        double organizerTop = Top + TitleAreaRoot.ActualHeight;
        double rowHeight = 30;
        double desiredHeight = (_todoEditItems.Count * rowHeight) + rowHeight + 38;
        double maxHeight = Math.Max(80, SystemParameters.WorkArea.Bottom - organizerTop - 10);
        double organizerHeight = Math.Min(maxHeight, Math.Max(48, desiredHeight));
        TodoOrganizer.ShowAt(organizerLeft, organizerTop, organizerHeight);
        TodoOrganizer.ItemsScrollViewer.ScrollToTop();
    }

    private void FocusFirstTodoEditor()
    {
        FocusTodoEditorAt(0);
    }

    private void FocusTodoEditorAt(int index)
    {
        TextBox? editor = FindTodoEditorByIndex(index);
        if (editor is null)
        {
            return;
        }

        editor.Focus();
        editor.CaretIndex = editor.Text.Length;
    }

    private TextBox? FindTodoEditorByIndex(int index)
    {
        foreach (object child in TodoEditorPanel.Children)
        {
            if (child is Border border
                && border.Child is Grid rowGrid)
            {
                foreach (UIElement element in rowGrid.Children)
                {
                    if (element is TextBox textBox && textBox.Tag is int itemIndex && itemIndex == index)
                    {
                        return textBox;
                    }
                }
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = GetParentDependencyObject(node);
        }

        return null;
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

    private static string NormalizeUrl(string url)
    {
        return url.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? $"https://{url}"
            : url;
    }

    private void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink hyperlink)
        {
            return;
        }

        string? target = hyperlink.NavigateUri?.AbsoluteUri;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if the system cannot open the URL.
        }
    }

    private void SaveAndExitEditMode()
    {
        if (!_isEditing)
        {
            return;
        }

        string contentToSave = _note.Kind == NoteKind.Todo
            ? SerializeTodoItems(_todoEditItems.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
            : ContentEditor.Text;

        _repository.SaveNote(_note, TitleEditor.Text, contentToSave);

        ExitEditModeVisuals();
        RefreshReadonlyView();
        NotifyAppNotesChanged();
    }

    private void CancelAndExitEditMode()
    {
        if (!_isEditing)
        {
            return;
        }

        _note.Title = _originalTitle;
        _note.Content = _originalContent;
        _todoEditItems.Clear();

        ExitEditModeVisuals();
        RefreshReadonlyView();
    }

    private void ExitEditModeVisuals()
    {
        _isEditing = false;

        TitleTextBlock.Visibility = Visibility.Visible;
        ContentScrollViewer.Visibility = Visibility.Visible;

        TitleEditor.Visibility = Visibility.Collapsed;
        ContentEditor.Visibility = Visibility.Collapsed;
        TodoEditorScrollViewer.Visibility = Visibility.Collapsed;
        TodoOrganizer.HidePane();

        UpdateActionBarState();
    }

    private void ScheduleLayoutSave()
    {
        if (_suspendLayoutSave || !IsLoaded || _isEditing)
        {
            return;
        }

        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Start();
    }

    private void SaveLayoutNow()
    {
        if (_suspendLayoutSave || WindowState == WindowState.Minimized)
        {
            return;
        }

        _repository.SaveLayout(
            _note,
            Left,
            Top,
            ActualWidth,
            ActualHeight);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BringToFront();
        _isTitleAreaHovered = true;
        CancelActionBarCollapse();
        UpdateActionBarState();

        if (IsInteractiveControlClick(e.OriginalSource))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            if (_isEditing)
            {
                SaveAndExitEditMode();
            }
            else
            {
                EnterEditMode();
            }

            e.Handled = true;
            return;
        }

        if (_isEditing)
        {
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            _suspendLayoutSave = true;
            DismissTransientPanelsForDrag();

            try
            {
                DragMove();
            }
            catch
            {
                // DragMove can throw if mouse state changes mid-drag.
            }
            finally
            {
                _isDragging = false;
                _suspendLayoutSave = false;
            }

            ScheduleLayoutSave();
            RefreshDesktopHostSurface();
            RefreshAllOpenNoteSurfaces();
        }
    }

    private void DismissTransientPanelsForDrag()
    {
        DismissTransientPanels();
    }

    private void DismissTransientPanels()
    {
        _isTitleAreaHovered = false;
        _isActionPopupHovered = false;
        _isSettingsPopupHovered = false;
        _settingsPanelMode = SettingsPanelMode.None;
        CancelActionBarCollapse();
        UpdateActionBarState();
    }

    public void DismissTransientPanelsExternal()
    {
        if (!_isTitleAreaHovered
            && !_isActionPopupHovered
            && !_isSettingsPopupHovered
            && _settingsPanelMode == SettingsPanelMode.None
            && !ActionBar.IsPaneVisible
            && !SettingsPanel.IsPaneVisible)
        {
            return;
        }

        DismissTransientPanels();
    }

    public void HandleGlobalMouseDown(Point screenPoint)
    {
        if (_suppressNextEditModeGlobalClick)
        {
            _suppressNextEditModeGlobalClick = false;
            return;
        }

        if (_suppressNextTodoEditorGlobalClick)
        {
            _suppressNextTodoEditorGlobalClick = false;
            return;
        }

        if (_isEditing)
        {
            bool clickedEditor = HitTestElementScreenPoint(TitleEditor, screenPoint)
                || HitTestElementScreenPoint(ContentEditor, screenPoint)
                || HitTestElementScreenPoint(TodoEditorScrollViewer, screenPoint)
                || TodoOrganizer.ContainsScreenPoint(screenPoint);
            bool clickedTransientUi = ActionBar.ContainsScreenPoint(screenPoint) || SettingsPanel.ContainsScreenPoint(screenPoint);

            if (!clickedEditor && !clickedTransientUi)
            {
                SaveAndExitEditMode();
            }
        }

        bool settingsOpen = _settingsPanelMode != SettingsPanelMode.None || SettingsPanel.IsPaneVisible;
        if (!settingsOpen)
        {
            return;
        }

        if (ActionBar.ContainsScreenPoint(screenPoint) || SettingsPanel.ContainsScreenPoint(screenPoint))
        {
            return;
        }

        DismissTransientPanels();
    }

    private static bool IsElementScreenPoint(FrameworkElement element, Point screenPoint, double extraPadding = 0)
    {
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        Point topLeft = element.PointToScreen(new Point(0, 0));
        return screenPoint.X >= topLeft.X - extraPadding
            && screenPoint.X < topLeft.X + element.ActualWidth + extraPadding
            && screenPoint.Y >= topLeft.Y - extraPadding
            && screenPoint.Y < topLeft.Y + element.ActualHeight + extraPadding;
    }

    private static bool HitTestElementScreenPoint(FrameworkElement element, Point screenPoint)
    {
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        Point localPoint = element.PointFromScreen(screenPoint);
        Rect bounds = new(0, 0, element.ActualWidth, element.ActualHeight);
        if (!bounds.Contains(localPoint))
        {
            return false;
        }

        return element.InputHitTest(localPoint) is not null;
    }

    private void TitleArea_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        if (Application.Current is App app)
        {
            app.DismissTransientPanelsExcept(this);
        }

        _isTitleAreaHovered = true;
        CancelActionBarCollapse();
        UpdateActionBarState();
    }

    private void TitleArea_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        _isTitleAreaHovered = false;
        StartActionBarCollapseDelay();
    }

    private void ActionPopup_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        _isActionPopupHovered = true;
        CancelActionBarCollapse();
        UpdateActionBarState();
    }

    private void ActionPopup_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        _isActionPopupHovered = false;
        StartActionBarCollapseDelay();
    }

    private void SettingsPopup_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        _isSettingsPopupHovered = true;
        CancelActionBarCollapse();
        UpdateActionBarState();
    }

    private void SettingsPopup_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        _isSettingsPopupHovered = false;
        StartActionBarCollapseDelay();
    }

    private static bool IsInteractiveControlClick(object source)
    {
        DependencyObject? node = source as DependencyObject;
        while (node is not null)
        {
            if (node is Button or TextBox)
            {
                return true;
            }

            node = GetParentDependencyObject(node);
        }

        return false;
    }

    private static DependencyObject? GetParentDependencyObject(DependencyObject node)
    {
        return node switch
        {
            Visual or Visual3D => VisualTreeHelper.GetParent(node),
            FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
            ContentElement contentElement => ContentOperations.GetParent(contentElement),
            _ => null
        };
    }

    private static bool IsMaximizeRelatedHitTest(int hitTest)
    {
        return hitTest is HTCAPTION
            or HTLEFT
            or HTRIGHT
            or HTTOP
            or HTTOPLEFT
            or HTTOPRIGHT
            or HTBOTTOM
            or HTBOTTOMLEFT
            or HTBOTTOMRIGHT;
    }

    private void UpdateActionBarState()
    {
        if (_isUpdatingTransientUi)
        {
            return;
        }

        _isUpdatingTransientUi = true;

        try
        {
            SyncActionBarLayout();
            UpdateSettingsPanelState();

            bool expanded = !_isDragging && (
                _isEditing
                || _isTitleAreaHovered
                || _isActionPopupHovered
                || _isNewNoteMenuOpen
                || _isSettingsPopupHovered
                || _settingsPanelMode != SettingsPanelMode.None);

            if (expanded)
            {
                ActionBar.ShowAt(Left, Top - ActionBar.Height + 1, ActualWidth);
            }
            else if (ActionBar.IsPaneVisible)
            {
                ActionBar.HidePane();
            }

            ActionBar.CloseButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ActionBar.ManagerButton.Visibility = (!_isEditing && expanded) ? Visibility.Visible : Visibility.Collapsed;
            ActionBar.SaveButton.Visibility = (_isEditing && expanded) ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isUpdatingTransientUi = false;
        }

        SchedulePopupRefresh();
    }

    private void StartActionBarCollapseDelay()
    {
        if (_isEditing)
        {
            return;
        }

        _actionBarCollapseTimer.Stop();
        _actionBarCollapseTimer.Start();
    }

    private void CancelActionBarCollapse()
    {
        _actionBarCollapseTimer.Stop();
    }

    private void SyncActionBarLayout()
    {
        if (ActionBar.IsPaneVisible)
        {
            ActionBar.UpdatePlacement(Left, Top - ActionBar.Height + 1, ActualWidth);
        }

        if (TodoOrganizer.IsPaneVisible)
        {
            UpdateTodoOrganizerState();
        }
    }

    private void SchedulePopupRefresh()
    {
        if (_popupRefreshPending)
        {
            return;
        }

        _popupRefreshPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _popupRefreshPending = false;
            RefreshTransientWindowPositions();
        }, DispatcherPriority.Background);
    }

    private void RefreshTransientWindowPositions()
    {
        if (ActionBar.IsPaneVisible)
        {
            ActionBar.UpdatePlacement(Left, Top - ActionBar.Height + 1, ActualWidth);
        }

        if (SettingsPanel.IsPaneVisible)
        {
            SettingsPanel.UpdatePlacement(Left - SettingsPanel.ActualWidth - 8, Top);
        }

        if (TodoOrganizer.IsPaneVisible)
        {
            UpdateTodoOrganizerState();
        }
    }

    private void UpdateSettingsPanelState()
    {
        if (_isDragging)
        {
            if (SettingsPanel.IsPaneVisible)
            {
                SettingsPanel.HidePane();
            }
            UpdateToolbarToggleVisual(ActionBar.TextSizeButton, false);
            UpdateToolbarToggleVisual(ActionBar.ColorButton, false);
            return;
        }

        bool isFontSettingsOpen = _settingsPanelMode == SettingsPanelMode.TextSize;
        bool isColorSettingsOpen = _settingsPanelMode == SettingsPanelMode.BackgroundColor;

        SettingsPanel.SettingsTitleTextBlock.Text = isFontSettingsOpen ? "Text Size" : "Background";
        SettingsPanel.FontSettingsPanel.Visibility = isFontSettingsOpen ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.ColorSettingsPanel.Visibility = isColorSettingsOpen ? Visibility.Visible : Visibility.Collapsed;
        bool shouldOpenSettings = isFontSettingsOpen || isColorSettingsOpen;

        if (shouldOpenSettings)
        {
            SettingsPanel.UpdateLayout();
            SettingsPanel.ShowAt(Left - SettingsPanel.ActualWidth - 8, Top);
        }
        else if (SettingsPanel.IsPaneVisible)
        {
            SettingsPanel.HidePane();
        }

        UpdateToolbarToggleVisual(ActionBar.TextSizeButton, isFontSettingsOpen);
        UpdateToolbarToggleVisual(ActionBar.ColorButton, isColorSettingsOpen);
    }

    private void UpdateSettingsSelectionVisuals()
    {
        UpdateFontSelectionVisuals();
        UpdateColorSelectionVisuals();
    }

    private void UpdateFontSelectionVisuals()
    {
        AppSettings settings = (Application.Current as App)?.Settings ?? new AppSettings();
        UpdateFontSelectionVisualsForPanel(SettingsPanel.TitleFontSizeOptionsPanel, settings.TitleFontSize, "Title");
        UpdateFontSelectionVisualsForPanel(SettingsPanel.ContentFontSizeOptionsPanel, settings.ContentFontSize, "Content");
        UpdateFontSelectionVisualsForPanel(SettingsPanel.TitleLineSpacingOptionsPanel, settings.TitleLineSpacing, "TitleLine");
        UpdateFontSelectionVisualsForPanel(SettingsPanel.ContentLineSpacingOptionsPanel, settings.ContentLineSpacing, "ContentLine");
    }

    private static void UpdateFontSelectionVisualsForPanel(WrapPanel wrapPanel, double currentValue, string prefix)
    {
        foreach (object option in wrapPanel.Children)
        {
            if (option is Button button
                && button.Tag is string rawValue
                && rawValue.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(rawValue[(prefix.Length + 1)..], out double fontSize))
            {
                bool isSelected = Math.Abs(fontSize - currentValue) < 0.1;
                button.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                button.BorderBrush = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(104, 104, 104));
                button.Background = isSelected ? new SolidColorBrush(Color.FromRgb(84, 84, 84)) : new SolidColorBrush(Color.FromRgb(58, 58, 58));
                button.Foreground = Brushes.White;
            }
        }
    }

    private void UpdateColorSelectionVisuals()
    {
        foreach (object child in SettingsPanel.ColorSettingsPanel.Children)
        {
            if (child is not WrapPanel wrapPanel)
            {
                continue;
            }

            foreach (object option in wrapPanel.Children)
            {
                if (option is Button button && button.Tag is string colorValue)
                {
                    bool isSelected = string.Equals(colorValue, _note.BackgroundColor, StringComparison.OrdinalIgnoreCase);
                    Color swatchColor = ParseBackgroundColor(colorValue);
                    button.BorderThickness = isSelected ? new Thickness(3) : new Thickness(1);
                    button.BorderBrush = isSelected
                        ? (IsDarkColor(swatchColor) ? Brushes.White : Brushes.Black)
                        : new SolidColorBrush(Color.FromRgb(104, 104, 104));
                }
            }
        }
    }

    private static void UpdateToolbarToggleVisual(Button button, bool isSelected)
    {
        button.Background = isSelected ? new SolidColorBrush(Color.FromRgb(92, 92, 92)) : new SolidColorBrush(Color.FromRgb(74, 74, 74));
        button.BorderBrush = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(104, 104, 104));
    }

    private static Color ParseBackgroundColor(string colorValue)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(colorValue);
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString("#FFF4A0");
        }
    }

    private static Brush BuildForegroundBrush(Color backgroundColor)
    {
        return IsDarkColor(backgroundColor) ? Brushes.White : Brushes.Black;
    }

    private static Brush BuildEditorBorderBrush(Color backgroundColor)
    {
        return IsDarkColor(backgroundColor)
            ? new SolidColorBrush(Color.FromRgb(120, 120, 120))
            : new SolidColorBrush(Color.FromRgb(160, 160, 160));
    }

    private static Brush BuildHyperlinkBrush(Color backgroundColor)
    {
        return IsDarkColor(backgroundColor)
            ? new SolidColorBrush(Color.FromRgb(151, 201, 255))
            : new SolidColorBrush(Color.FromRgb(0, 84, 184));
    }

    private static Brush BuildTodoBlockBrush(Color backgroundColor)
    {
        return IsDarkColor(backgroundColor)
            ? new SolidColorBrush(Color.FromArgb(70, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
    }

    private static Brush BuildTodoReadonlyBlockBrush(Color backgroundColor, bool isChecked)
    {
        if (IsDarkColor(backgroundColor))
        {
            return isChecked
                ? new SolidColorBrush(BlendColors(backgroundColor, Colors.Black, 0.22, 235))
                : new SolidColorBrush(Color.FromArgb(104, 255, 255, 255));
        }

        return isChecked
            ? new SolidColorBrush(BlendColors(backgroundColor, Colors.Black, 0.14, 235))
            : new SolidColorBrush(Color.FromArgb(108, 255, 255, 255));
    }

    private static Color BlendColors(Color baseColor, Color overlayColor, double overlayAmount, byte alpha)
    {
        overlayAmount = Math.Max(0, Math.Min(1, overlayAmount));

        byte BlendChannel(byte source, byte overlay)
        {
            double value = (source * (1 - overlayAmount)) + (overlay * overlayAmount);
            return (byte)Math.Round(value);
        }

        return Color.FromArgb(
            alpha,
            BlendChannel(baseColor.R, overlayColor.R),
            BlendChannel(baseColor.G, overlayColor.G),
            BlendChannel(baseColor.B, overlayColor.B));
    }

    private static Brush BuildTodoBorderBrush(Color backgroundColor)
    {
        return IsDarkColor(backgroundColor)
            ? new SolidColorBrush(Color.FromArgb(120, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(90, 60, 60, 60));
    }

    private static bool IsDarkColor(Color color)
    {
        double brightness = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return brightness < 145;
    }

    private void UpdateRoundedClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        if (_currentCornerRadius <= 0)
        {
            RootBorder.Clip = null;
            return;
        }

        RootBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, ActualWidth, ActualHeight),
            _currentCornerRadius,
            _currentCornerRadius);

        ApplyWindowRegion(PresentationSource.FromVisual(this) as HwndSource);
    }

    private void ApplyWindowRegion(HwndSource? source)
    {
        if (_windowHandle == IntPtr.Zero || source is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        if (_currentCornerRadius <= 0)
        {
            SetWindowRgn(_windowHandle, IntPtr.Zero, true);
            return;
        }

        Matrix transform = source.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        int width = Math.Max(1, (int)Math.Round(ActualWidth * transform.M11));
        int height = Math.Max(1, (int)Math.Round(ActualHeight * transform.M22));
        int radiusX = Math.Max(1, (int)Math.Round(_currentCornerRadius * transform.M11));
        int radiusY = Math.Max(1, (int)Math.Round(_currentCornerRadius * transform.M22));

        IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radiusX * 2, radiusY * 2);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(_windowHandle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private (double left, double top, double width, double height) NormalizeBoundsToVisibleDesktop(double left, double top, double width, double height)
    {
        double virtualLeft = SystemParameters.VirtualScreenLeft;
        double virtualTop = SystemParameters.VirtualScreenTop;
        double virtualWidth = SystemParameters.VirtualScreenWidth;
        double virtualHeight = SystemParameters.VirtualScreenHeight;

        double safeWidth = Math.Max(MinWidth, Math.Min(width, virtualWidth));
        double safeHeight = Math.Max(MinHeight, Math.Min(height, virtualHeight));

        double maxLeft = virtualLeft + Math.Max(0, virtualWidth - safeWidth);
        double maxTop = virtualTop + Math.Max(0, virtualHeight - safeHeight);

        double safeLeft = Math.Min(Math.Max(left, virtualLeft), maxLeft);
        double safeTop = Math.Min(Math.Max(top, virtualTop), maxTop);

        return (safeLeft, safeTop, safeWidth, safeHeight);
    }

    private CustomPopupPlacement[] ActionPopup_CustomPopupPlacement(Size popupSize, Size targetSize, Point offset)
    {
        Point topPlacement = new(0, -popupSize.Height);
        return
        [
            new CustomPopupPlacement(topPlacement, PopupPrimaryAxis.None)
        ];
    }

    private CustomPopupPlacement[] SettingsPopup_CustomPopupPlacement(Size popupSize, Size targetSize, Point offset)
    {
        Point leftPlacement = new(-popupSize.Width - 8, 0);
        return
        [
            new CustomPopupPlacement(leftPlacement, PopupPrimaryAxis.None)
        ];
    }

    private (int x, int y, int width, int height) GetDevicePixelBounds(HwndSource source)
    {
        Matrix transform = source.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

        int x = (int)Math.Round(Left * transform.M11);
        int y = (int)Math.Round(Top * transform.M22);
        int width = (int)Math.Round(Width * transform.M11);
        int height = (int)Math.Round(Height * transform.M22);

        return (x, y, width, height);
    }

    private void Body_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BringToFront();

        if (IsInteractiveControlClick(e.OriginalSource)
            || (!_isEditing && _note.Kind == NoteKind.Todo && IsDescendantOf(e.OriginalSource as DependencyObject, TodoItemsPanel))
            || (_isEditing && _note.Kind == NoteKind.Todo && IsDescendantOf(e.OriginalSource as DependencyObject, TodoEditorScrollViewer)))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            if (_isEditing)
            {
                SaveAndExitEditMode();
            }
            else
            {
                EnterEditMode();
            }

            e.Handled = true;
        }
    }

    private void ContentEditor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isEditing || e.ClickCount < 3)
        {
            return;
        }

        Point clickPoint = e.GetPosition(ContentEditor);
        Dispatcher.BeginInvoke(() => SelectParagraphAtPoint(clickPoint), DispatcherPriority.Input);
    }

    private void SelectParagraphAtPoint(Point clickPoint)
    {
        if (!_isEditing)
        {
            return;
        }

        int index = ContentEditor.GetCharacterIndexFromPoint(clickPoint, snapToText: true);
        if (index < 0)
        {
            index = ContentEditor.CaretIndex;
        }

        string text = ContentEditor.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        index = Math.Max(0, Math.Min(index, text.Length - 1));
        (int start, int length) = GetParagraphSelection(text, index);
        ContentEditor.Focus();
        ContentEditor.Select(start, length);
    }

    private static (int start, int length) GetParagraphSelection(string text, int index)
    {
        MatchCollection separators = Regex.Matches(text, @"(?:\r\n|\r|\n){2,}");
        int start = 0;
        int end = text.Length;

        foreach (Match separator in separators)
        {
            if (separator.Index + separator.Length <= index)
            {
                start = separator.Index + separator.Length;
                continue;
            }

            if (separator.Index > index)
            {
                end = separator.Index;
                break;
            }
        }

        while (start < end && (text[start] == '\r' || text[start] == '\n'))
        {
            start++;
        }

        while (end > start && (text[end - 1] == '\r' || text[end - 1] == '\n'))
        {
            end--;
        }

        return (start, Math.Max(0, end - start));
    }

    public void BringToFront()
    {
        WindowState = WindowState.Normal;

        if (_windowHandle == IntPtr.Zero)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
        }

        if (_isDesktopLayerAttached)
        {
            DesktopWindowHost.BringToFront(_windowHandle);
        }

        Activate();
        Focus();
    }

    public void ApplyShowDesktopBehavior(bool keepVisibleOnShowDesktop)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
        if (source is null)
        {
            return;
        }

        (int x, int y, int width, int height) = GetDevicePixelBounds(source);

        if (keepVisibleOnShowDesktop && !_isDesktopLayerAttached)
        {
            try
            {
                _isDesktopLayerAttached = DesktopWindowHost.TryAttachToDesktop(
                    _windowHandle,
                    _originalStyle,
                    _originalExStyle,
                    x,
                    y,
                    width,
                    height);
            }
            catch
            {
                _isDesktopLayerAttached = false;
            }
        }
        else if (!keepVisibleOnShowDesktop && _isDesktopLayerAttached)
        {
            DesktopWindowHost.DetachFromDesktop(_windowHandle, _originalStyle, _originalExStyle, x, y, width, height);
            _isDesktopLayerAttached = false;
        }

        ActionBar.SetDesktopAttachment(keepVisibleOnShowDesktop);
        SettingsPanel.SetDesktopAttachment(keepVisibleOnShowDesktop);
        TodoOrganizer.SetDesktopAttachment(keepVisibleOnShowDesktop);
        RefreshTransientWindowPositions();
    }

    public void RefreshPresentation()
    {
        InvalidateVisual();
        UpdateLayout();
        RefreshDesktopHostSurface();
    }

    private void RefreshDesktopHostSurface()
    {
        if (!_isDesktopLayerAttached || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        DesktopWindowHost.RefreshDesktopHost(_windowHandle);
    }

    private void RefreshAllOpenNoteSurfaces()
    {
        if (Application.Current is App app)
        {
            app.RefreshAllOpenNoteSurfaces();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    private void TextSizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPanel(SettingsPanelMode.TextSize);
    }

    private void ManagerButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ShowManagerWindow();
        }
    }

    private void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        ActionBar.NewNoteContextMenu.PlacementTarget = ActionBar.NewNoteButton;
        ActionBar.NewNoteContextMenu.IsOpen = true;
    }

    private void NewStickyMemoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.CreateAndOpenNewNote();
        }
    }

    private void NewTodoMemoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.CreateAndOpenNewTodoNote();
        }
    }

    private void NewNoteContextMenu_Opened(object? sender, RoutedEventArgs e)
    {
        _isNewNoteMenuOpen = true;
        CancelActionBarCollapse();
        UpdateActionBarState();
    }

    private void NewNoteContextMenu_Closed(object? sender, RoutedEventArgs e)
    {
        _isNewNoteMenuOpen = false;
        StartActionBarCollapseDelay();
        UpdateActionBarState();
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPanel(SettingsPanelMode.BackgroundColor);
    }

    private void ToggleSettingsPanel(SettingsPanelMode mode)
    {
        if (Application.Current is App app)
        {
            app.DismissTransientPanelsExcept(this);
        }

        _settingsPanelMode = _settingsPanelMode == mode ? SettingsPanelMode.None : mode;
        _isTitleAreaHovered = true;
        CancelActionBarCollapse();
        UpdateActionBarState();
    }

    private void FontSizeOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string rawValue)
        {
            return;
        }

        string[] parts = rawValue.Split(':');
        if (parts.Length != 2 || !double.TryParse(parts[1], out double fontSize))
        {
            return;
        }

        if (Application.Current is not App app)
        {
            return;
        }

        double titleFontSize = app.Settings.TitleFontSize;
        double contentFontSize = app.Settings.ContentFontSize;

        if (string.Equals(parts[0], "Title", StringComparison.OrdinalIgnoreCase))
        {
            titleFontSize = fontSize;
        }
        else
        {
            contentFontSize = fontSize;
        }

        app.UpdateGlobalSettings(
            app.Settings.NoteCornerRadius,
            titleFontSize,
            contentFontSize,
            app.Settings.TitleLineSpacing,
            app.Settings.ContentLineSpacing,
            app.Settings.DefaultBackgroundColor);
    }

    private void LineSpacingOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string rawValue)
        {
            return;
        }

        string[] parts = rawValue.Split(':');
        if (parts.Length != 2 || !double.TryParse(parts[1], out double lineSpacing))
        {
            return;
        }

        if (Application.Current is not App app)
        {
            return;
        }

        double titleLineSpacing = app.Settings.TitleLineSpacing;
        double contentLineSpacing = app.Settings.ContentLineSpacing;

        if (string.Equals(parts[0], "TitleLine", StringComparison.OrdinalIgnoreCase))
        {
            titleLineSpacing = lineSpacing;
        }
        else
        {
            contentLineSpacing = lineSpacing;
        }

        app.UpdateGlobalSettings(
            app.Settings.NoteCornerRadius,
            app.Settings.TitleFontSize,
            app.Settings.ContentFontSize,
            titleLineSpacing,
            contentLineSpacing,
            app.Settings.DefaultBackgroundColor);
    }

    private void ColorOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string colorValue)
        {
            return;
        }

        _repository.SaveAppearance(_note, _note.ContentFontSize, colorValue);
        ApplyNoteAppearance();
        NotifyAppNotesChanged();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAndExitEditMode();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.HideNote(_note);
            return;
        }

        Close();
    }

    private void NotifyAppNotesChanged()
    {
        if (Application.Current is App app)
        {
            app.NotifyNotesChanged();
        }
    }

    private sealed record TodoItemData(string Text, bool IsChecked);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_isEditing)
        {
            if (e.Key == Key.Escape)
            {
                CancelAndExitEditMode();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SaveAndExitEditMode();
                e.Handled = true;
                return;
            }

            return;
        }

        if (e.Key == Key.Escape && _settingsPanelMode != SettingsPanelMode.None)
        {
            _settingsPanelMode = SettingsPanelMode.None;
            _isSettingsPopupHovered = false;
            UpdateActionBarState();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            EnterEditMode();
            e.Handled = true;
        }
    }
}

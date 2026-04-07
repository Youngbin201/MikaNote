using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using System.Runtime.InteropServices;

namespace MikaNote.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\MikaNote.App.Singleton";
    private const string CommandPipeName = "MikaNote.App.CommandPipe";
    private const string ShowWelcomeSplashArgument = "--show-welcome-splash";

    private readonly List<NoteWindow> _openWindows = new();
    private readonly CancellationTokenSource _pipeCancellationTokenSource = new();
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private NoteRepository? _repository;
    private MainWindow? _managerWindow;
    private AppSettings _settings = new();
    private Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;
    private IntPtr _mouseHookHandle;
    private LowLevelMouseProc? _mouseHookProc;

    public ObservableCollection<NoteDocument> Notes { get; } = new();
    public ObservableCollection<NoteDocument> HiddenNotes { get; } = new();
    public ObservableCollection<NoteDocument> BackupNotes { get; } = new();
    public AppSettings Settings => _settings;
    public string NotesDirectory => _repository?.NotesDirectory ?? string.Empty;

    public event EventHandler? NotesChanged;
    public event EventHandler? SettingsChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (TryHandleInstallerCommand(e.Args))
        {
            Shutdown();
            return;
        }

        if (!TryAcquireSingleInstance(e.Args))
        {
            Shutdown();
            return;
        }

        try
        {
            _repository = new NoteRepository(NoteRepository.ResolveWritableDirectory());
            _settings = _repository.LoadAppSettings();
            _settings.KeepVisibleOnShowDesktop = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Could not prepare note storage.\n{exception.Message}",
                "MikaNote",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        SplashWindow? splashWindow = ShowWelcomeSplashIfRequested(e.Args, out DateTimeOffset splashShownAt);

        EnsureNotifyIcon();
        InstallGlobalMouseHook();
        _ = Task.Run(() => RunCommandPipeServerAsync(_pipeCancellationTokenSource.Token));

        LoadAndOpenAllNotes();

        if (e.Args.Contains("--new-todo", StringComparer.OrdinalIgnoreCase))
        {
            CreateAndOpenNewTodoNote();
        }
        else if (e.Args.Contains("--new", StringComparer.OrdinalIgnoreCase))
        {
            CreateAndOpenNewNote();
        }

        if (Notes.Count == 0 && HiddenNotes.Count == 0 && BackupNotes.Count == 0)
        {
            CreateAndOpenNewNote();
        }
        
        CloseWelcomeSplashWhenReady(splashWindow, splashShownAt);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        UninstallGlobalMouseHook();
        DisposeNotifyIcon();
        _pipeCancellationTokenSource.Cancel();
        _pipeCancellationTokenSource.Dispose();

        if (_singleInstanceMutex is not null)
        {
            if (_ownsSingleInstanceMutex)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Ignore if mutex ownership already ended.
                }
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private bool TryHandleInstallerCommand(string[] args)
    {
        string executablePath = GetExecutablePath();

        if (args.Contains("--install-shell", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ShellContextMenuRegistrar.Install(executablePath);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Failed to register desktop context menu.\n{exception.Message}",
                    "MikaNote",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return true;
        }

        if (args.Contains("--uninstall-shell", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ShellContextMenuRegistrar.Uninstall();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Failed to unregister desktop context menu.\n{exception.Message}",
                    "MikaNote",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return true;
        }

        if (args.Contains("--enable-startup", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                StartupRegistrar.Enable(executablePath);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Failed to enable startup.\n{exception.Message}",
                    "MikaNote",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return true;
        }

        if (args.Contains("--disable-startup", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                StartupRegistrar.Disable();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Failed to disable startup.\n{exception.Message}",
                    "MikaNote",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return true;
        }

        return false;
    }

    private static string GetExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine executable path.");
    }

    private SplashWindow? ShowWelcomeSplashIfRequested(string[] args, out DateTimeOffset shownAt)
    {
        shownAt = DateTimeOffset.UtcNow;

        if (!args.Contains(ShowWelcomeSplashArgument, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        SplashWindow splashWindow = new();
        splashWindow.Show();
        splashWindow.UpdateLayout();
        shownAt = DateTimeOffset.UtcNow;
        return splashWindow;
    }

    private void CloseWelcomeSplashWhenReady(SplashWindow? splashWindow, DateTimeOffset shownAt)
    {
        if (splashWindow is null)
        {
            return;
        }

        TimeSpan visibleFor = DateTimeOffset.UtcNow - shownAt;
        TimeSpan delay = TimeSpan.FromMilliseconds(Math.Max(0, 1600 - visibleFor.TotalMilliseconds));

        DispatcherTimer timer = new()
        {
            Interval = delay
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            splashWindow.Close();
        };

        timer.Start();
    }

    private bool TryAcquireSingleInstance(string[] args)
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool isPrimaryInstance);
        if (isPrimaryInstance)
        {
            _ownsSingleInstanceMutex = true;
            return true;
        }

        SendCommandToPrimaryInstance(args.Length > 0 ? args : new[] { "--activate" });
        return false;
    }

    private static void SendCommandToPrimaryInstance(string[] args)
    {
        try
        {
            using NamedPipeClientStream client = new(".", CommandPipeName, PipeDirection.Out);
            client.Connect(350);

            using StreamWriter writer = new(client, Encoding.UTF8, leaveOpen: true);
            string payload = JsonSerializer.Serialize(args);
            writer.WriteLine(payload);
            writer.Flush();
        }
        catch
        {
            // No-op. If IPC fails, second instance exits quietly.
        }
    }

    private async Task RunCommandPipeServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = new(
                    CommandPipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using StreamReader reader = new(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[]? args = JsonSerializer.Deserialize<string[]>(line);
                if (args is null || args.Length == 0)
                {
                    continue;
                }

                await Dispatcher.InvokeAsync(() => HandleExternalCommand(args));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue listening even if one IPC request fails.
            }
        }
    }

    private void HandleExternalCommand(string[] args)
    {
        if (_repository is null)
        {
            return;
        }

        if (args.Contains("--new-todo", StringComparer.OrdinalIgnoreCase))
        {
            CreateAndOpenNewTodoNote();
            return;
        }

        if (args.Contains("--new", StringComparer.OrdinalIgnoreCase))
        {
            CreateAndOpenNewNote();
            return;
        }

        if (args.Contains("--activate", StringComparer.OrdinalIgnoreCase))
        {
            ActivateAnyOpenNote();
        }
    }

    private void LoadAndOpenAllNotes()
    {
        if (_repository is null)
        {
            return;
        }

        IReadOnlyList<NoteDocument> notes = _repository.LoadAllNotes();
        Notes.Clear();
        HiddenNotes.Clear();

        foreach (NoteDocument note in notes)
        {
            Notes.Add(note);
            OpenNoteWindow(note, activate: false);
        }

        foreach (NoteDocument hiddenNote in _repository.LoadHiddenNotes())
        {
            HiddenNotes.Add(hiddenNote);
        }

        BackupNotes.Clear();
        foreach (NoteDocument backupNote in _repository.LoadBackupNotes())
        {
            BackupNotes.Add(backupNote);
        }

        NotifyNotesChanged();
        ActivateAnyOpenNote();
    }

    public NoteDocument CreateAndOpenNewNote()
    {
        return CreateAndOpenNewNote(NoteKind.Standard);
    }

    public NoteDocument CreateAndOpenNewTodoNote()
    {
        return CreateAndOpenNewNote(NoteKind.Todo);
    }

    private NoteDocument CreateAndOpenNewNote(NoteKind kind)
    {
        if (_repository is null)
        {
            throw new InvalidOperationException("Repository is not ready.");
        }

        NoteDocument note = _repository.CreateNewNote(_settings, kind);
        Notes.Add(note);
        OpenNoteWindow(note, activate: true);
        NotifyNotesChanged();
        return note;
    }

    public void ActivateNote(NoteDocument note)
    {
        if (note.IsHidden)
        {
            ShowHiddenNote(note);
            return;
        }

        OpenNoteWindow(note, activate: true);
    }

    public void SaveNoteFromManager(NoteDocument note, string title, string content, string backgroundColor)
    {
        if (_repository is null)
        {
            return;
        }

        _repository.SaveNote(note, title, content);
        _repository.SaveAppearance(note, note.ContentFontSize, backgroundColor);
        RefreshWindowFromNote(note);
        NotifyNotesChanged();
    }

    public void UpdateNoteBackgroundFromManager(NoteDocument note, string backgroundColor)
    {
        if (_repository is null || note.IsBackup)
        {
            return;
        }

        _repository.SaveAppearance(note, note.ContentFontSize, backgroundColor);
        RefreshWindowFromNote(note);
        NotifyNotesChanged();
    }

    public void DeleteNote(NoteDocument note)
    {
        if (_repository is null)
        {
            return;
        }

        NoteWindow? openWindow = _openWindows.FirstOrDefault(window => string.Equals(window.NoteFilePath, note.FilePath, StringComparison.OrdinalIgnoreCase));
        if (openWindow is not null)
        {
            openWindow.PrepareForDeletion();
            openWindow.Close();
        }

        _repository.DeleteNote(note);
        Notes.Remove(note);
        HiddenNotes.Remove(note);
        BackupNotes.Add(note);
        NotifyNotesChanged();
    }

    public void HideNote(NoteDocument note)
    {
        if (_repository is null || note.IsBackup || note.IsHidden)
        {
            return;
        }

        NoteWindow? openWindow = _openWindows.FirstOrDefault(window => string.Equals(window.NoteFilePath, note.FilePath, StringComparison.OrdinalIgnoreCase));
        openWindow?.Close();

        _repository.HideNote(note);
        Notes.Remove(note);
        HiddenNotes.Add(note);
        NotifyNotesChanged();
    }

    public NoteDocument? ShowHiddenNote(NoteDocument note)
    {
        if (_repository is null || note.IsBackup || !note.IsHidden)
        {
            return null;
        }

        NoteDocument shown = _repository.ShowHiddenNote(note);
        HiddenNotes.Remove(note);
        Notes.Add(shown);
        OpenNoteWindow(shown, activate: true);
        NotifyNotesChanged();
        return shown;
    }

    public NoteDocument? RestoreBackupNote(NoteDocument note)
    {
        if (_repository is null)
        {
            return null;
        }

        NoteDocument restored = _repository.RestoreBackupNote(note);
        BackupNotes.Remove(note);
        Notes.Add(restored);
        OpenNoteWindow(restored, activate: true);
        NotifyNotesChanged();
        return restored;
    }

    public void PermanentlyDeleteBackupNote(NoteDocument note)
    {
        if (_repository is null)
        {
            return;
        }

        _repository.PermanentlyDeleteNote(note);
        BackupNotes.Remove(note);
        NotifyNotesChanged();
    }

    public void EmptyTrash()
    {
        if (_repository is null)
        {
            return;
        }

        foreach (NoteDocument note in BackupNotes.ToList())
        {
            _repository.PermanentlyDeleteNote(note);
            BackupNotes.Remove(note);
        }

        NotifyNotesChanged();
    }

    public void OpenNotesDirectory()
    {
        if (_repository is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_repository.NotesDirectory}\"",
            UseShellExecute = true
        });
    }

    public void UpdateGlobalSettings(
        double noteCornerRadius,
        double titleFontSize,
        double contentFontSize,
        double titleLineSpacing,
        double contentLineSpacing,
        string defaultBackgroundColor)
    {
        if (_repository is null)
        {
            return;
        }

        _settings.KeepVisibleOnShowDesktop = true;
        _settings.NoteCornerRadius = Math.Max(0, noteCornerRadius);
        _settings.TitleFontSize = Math.Max(8, titleFontSize);
        _settings.ContentFontSize = Math.Max(8, contentFontSize);
        _settings.TitleLineSpacing = Math.Max(1.0, titleLineSpacing);
        _settings.ContentLineSpacing = Math.Max(1.0, contentLineSpacing);
        _settings.DefaultBackgroundColor = string.IsNullOrWhiteSpace(defaultBackgroundColor)
            ? "#FFF4A0"
            : defaultBackgroundColor;
        _repository.SaveAppSettings(_settings);

        foreach (NoteWindow window in _openWindows)
        {
            window.ApplyGlobalSettings(_settings);
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowManagerWindow()
    {
        if (_managerWindow is null)
        {
            _managerWindow = new MainWindow(this);
            _managerWindow.Closed += (_, _) => _managerWindow = null;
        }

        if (!_managerWindow.IsVisible)
        {
            _managerWindow.Show();
        }

        _managerWindow.WindowState = WindowState.Normal;
        _managerWindow.Activate();
        _managerWindow.Focus();
    }

    public void NotifyNotesChanged()
    {
        NotesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshAllOpenNoteSurfaces()
    {
        foreach (NoteWindow window in _openWindows)
        {
            window.RefreshPresentation();
        }
    }

    public void DismissTransientPanelsExcept(NoteWindow activeWindow)
    {
        foreach (NoteWindow window in _openWindows)
        {
            if (!ReferenceEquals(window, activeWindow))
            {
                window.DismissTransientPanelsExternal();
            }
        }
    }

    public void RunMikaNote()
    {
        if (_openWindows.Count == 0)
        {
            CreateAndOpenNewNote();
            return;
        }

        ActivateAnyOpenNote();
    }

    private void OpenNoteWindow(NoteDocument note, bool activate)
    {
        if (_repository is null)
        {
            return;
        }

        NoteWindow? existingWindow = _openWindows.FirstOrDefault(window => string.Equals(window.NoteFilePath, note.FilePath, StringComparison.OrdinalIgnoreCase));
        if (existingWindow is not null)
        {
            if (activate)
            {
                existingWindow.BringToFront();
            }

            return;
        }

        NoteWindow window = new(note, _repository);
        window.ApplyGlobalSettings(_settings);
        window.Closed += (_, _) => _openWindows.Remove(window);

        _openWindows.Add(window);
        window.Show();

        if (activate)
        {
            window.BringToFront();
        }
    }

    private void ActivateAnyOpenNote()
    {
        NoteWindow? target = _openWindows.FirstOrDefault();
        if (target is null)
        {
            return;
        }

        target.WindowState = WindowState.Normal;
        target.BringToFront();
    }

    private void RefreshWindowFromNote(NoteDocument note)
    {
        NoteWindow? window = _openWindows.FirstOrDefault(candidate => string.Equals(candidate.NoteFilePath, note.FilePath, StringComparison.OrdinalIgnoreCase));
        window?.RefreshFromNote();
    }

    private void EnsureNotifyIcon()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        Forms.ContextMenuStrip trayMenu = new();
        trayMenu.Items.Add("Open MikaNote Manager", null, (_, _) => ShowManagerWindow());
        trayMenu.Items.Add("New Sticky Memo", null, (_, _) => CreateAndOpenNewNote());
        trayMenu.Items.Add("New Todo Memo", null, (_, _) => CreateAndOpenNewTodoNote());
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitFromTray());

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "MikaNote",
            Icon = LoadTrayIcon(),
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        _notifyIcon.DoubleClick += (_, _) => RunMikaNote();
    }

    private Drawing.Icon LoadTrayIcon()
    {
        string executablePath = GetExecutablePath();
        string executableDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
        string iconPath = Path.Combine(executableDirectory, "Assets", "MikaIcon.ico");

        if (File.Exists(iconPath))
        {
            return new Drawing.Icon(iconPath);
        }

        return Drawing.SystemIcons.Application;
    }

    private void InstallGlobalMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        _mouseHookProc = MouseHookCallback;
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(null), 0);
    }

    private void UninstallGlobalMouseHook()
    {
        if (_mouseHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
        _mouseHookProc = null;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsMouseDownMessage(wParam))
        {
            LowLevelMouseHookStruct hookData = Marshal.PtrToStructure<LowLevelMouseHookStruct>(lParam);
            Dispatcher.BeginInvoke(() =>
            {
                if (_isExiting)
                {
                    return;
                }

                HandleGlobalMouseDown(hookData.pt.X, hookData.pt.Y);
            }, DispatcherPriority.Background);
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void HandleGlobalMouseDown(int x, int y)
    {
        System.Windows.Point screenPoint = new(x, y);
        foreach (NoteWindow window in _openWindows.ToList())
        {
            window.HandleGlobalMouseDown(screenPoint);
        }
    }

    private static bool IsMouseDownMessage(IntPtr wParam)
    {
        int message = wParam.ToInt32();
        return message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN or WM_NCLBUTTONDOWN;
    }

    private void ExitFromTray()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        Shutdown();
    }

    private void DisposeNotifyIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_NCLBUTTONDOWN = 0x00A1;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseHookStruct
    {
        public NativePoint pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

}

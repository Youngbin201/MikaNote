using System.IO;
using System.Windows;
using System.Windows.Media;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;

namespace MikaNote.App;

public partial class GoogleDriveFolderWindow : Window
{
    private readonly App _app;
    private readonly GoogleDriveDesktopService _driveDesktopService = new();
    private readonly LocalBackupService _localBackupService = new();

    public GoogleDriveFolderWindow(App app)
    {
        InitializeComponent();
        _app = app;
        RefreshStatus();
    }

    private void AutoConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_driveDesktopService.IsInstalled())
        {
            MessageBox.Show(
                "Google Drive for desktop was not found on this PC.",
                "Connect Google Drive Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RefreshStatus();
            return;
        }

        string? detectedRoot = _driveDesktopService.TryDetectRootFolder();
        if (string.IsNullOrWhiteSpace(detectedRoot))
        {
            MessageBox.Show(
                "Google Drive was installed, but MikaNote could not find a synced folder automatically.",
                "Connect Google Drive Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RefreshStatus();
            return;
        }

        LinkFolder(detectedRoot);
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Select your synced Google Drive folder. MikaNote will create 'MikaNote Backups' automatically.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        LinkFolder(dialog.SelectedPath);
    }

    private void InstallGoogleDriveButton_Click(object sender, RoutedEventArgs e)
    {
        _driveDesktopService.OpenInstallPage();
    }

    private void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LocalBackupItem backup = _localBackupService.CreateBackup(_app.NotesDirectory, _app.Settings.GoogleDriveBackupFolderPath);
            RefreshBackupList(backup.FullPath);
            BackupActionStatusTextBlock.Text = $"Backup created: {backup.Name}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Could not create a backup.\n{exception.Message}",
                "Backup Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RefreshBackupsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshBackupList();
    }

    private void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        string? backupPath = BackupFilesComboBox.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            MessageBox.Show(
                "Select a backup to restore.",
                "Backup Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            "Restore this backup now? MikaNote will replace the current notes folder with the selected backup.",
            "Restore Backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            string linkedFolder = _app.Settings.GoogleDriveBackupFolderPath;
            DateTimeOffset? linkedAt = _app.Settings.GoogleDriveFolderLinkedAt;

            _localBackupService.RestoreBackup(backupPath, _app.NotesDirectory);
            _app.UpdateGoogleDriveFolderSettings(linkedFolder, isLinked: true, linkedAt);
            _app.ReloadNotesFromStorage();
            RefreshStatus();
            BackupActionStatusTextBlock.Text = "Backup restored.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Could not restore the backup.\n{exception.Message}",
                "Backup Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _app.UpdateGoogleDriveFolderSettings(string.Empty, isLinked: false, linkedAt: null);
        RefreshStatus();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _driveDesktopService.OpenFolder(_app.Settings.GoogleDriveBackupFolderPath);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LinkFolder(string selectedBaseFolder)
    {
        try
        {
            string backupFolder = _driveDesktopService.EnsureBackupFolder(selectedBaseFolder);
            _app.UpdateGoogleDriveFolderSettings(backupFolder, isLinked: true, linkedAt: DateTimeOffset.UtcNow);
            RefreshStatus();

            MessageBox.Show(
                $"Google Drive folder linked.\nMikaNote created:\n{backupFolder}",
                "Connect Google Drive Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Could not prepare the backup folder.\n{exception.Message}",
                "Connect Google Drive Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RefreshStatus()
    {
        bool installed = _driveDesktopService.IsInstalled();
        bool linked = _app.Settings.GoogleDriveFolderLinked &&
                      !string.IsNullOrWhiteSpace(_app.Settings.GoogleDriveBackupFolderPath) &&
                      Directory.Exists(_app.Settings.GoogleDriveBackupFolderPath);

        ConnectionStatusBorder.Padding = linked ? new Thickness(10, 8, 10, 8) : new Thickness(12);
        ConnectionStatusBorder.Margin = linked ? new Thickness(0, 10, 0, 0) : new Thickness(0, 16, 0, 0);
        DriveStatusRow.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
        SetupPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
        BackupManagementPanel.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;

        StatusTextBlock.Text = installed
            ? "Google Drive for desktop found."
            : "Google Drive for desktop not found.";

        LinkedFolderTextBlock.Text = linked
            ? "Backup folder linked."
            : "No backup folder selected.";

        BackupFolderPathTextBlock.Text = linked
            ? _app.Settings.GoogleDriveBackupFolderPath
            : "No backup folder linked.";

        SetStatusIcon(DriveFoundCircle, DriveFoundCheckIcon, DriveFoundCrossIcon, installed);
        SetStatusIcon(BackupFolderCircle, BackupFolderCheckIcon, BackupFolderCrossIcon, linked);

        DisconnectButton.IsEnabled = linked;
        OpenFolderButton.IsEnabled = linked;

        if (linked)
        {
            RefreshBackupList();
        }
        else
        {
            BackupFilesComboBox.ItemsSource = null;
            BackupActionStatusTextBlock.Text = "Connect a backup folder first.";
        }
    }

    private void RefreshBackupList(string? selectedPath = null)
    {
        IReadOnlyList<LocalBackupItem> backups = _localBackupService.ListBackups(_app.Settings.GoogleDriveBackupFolderPath);
        BackupFilesComboBox.ItemsSource = backups;

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            BackupFilesComboBox.SelectedValue = selectedPath;
        }
        else
        {
            BackupFilesComboBox.SelectedIndex = backups.Count > 0 ? 0 : -1;
        }

        BackupActionStatusTextBlock.Text = backups.Count == 0
            ? "No backups yet."
            : $"{backups.Count} backup{(backups.Count == 1 ? string.Empty : "s")} available.";
    }

    private static void SetStatusIcon(
        System.Windows.Shapes.Ellipse circle,
        System.Windows.Shapes.Path checkIcon,
        System.Windows.Shapes.Path crossIcon,
        bool isOk)
    {
        circle.Fill = isOk
            ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E4F5EA"))
            : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F6DFDF"));
        circle.Stroke = isOk
            ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2F9B5B"))
            : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D65E5E"));
        checkIcon.Visibility = isOk ? Visibility.Visible : Visibility.Collapsed;
        crossIcon.Visibility = isOk ? Visibility.Collapsed : Visibility.Visible;
    }
}

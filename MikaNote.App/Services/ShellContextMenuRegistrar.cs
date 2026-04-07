using System.IO;
using Microsoft.Win32;

namespace MikaNote.App;

public static class ShellContextMenuRegistrar
{
    private static readonly ShellMenuRegistration[] MenuRegistrations =
    {
        new(
            @"Software\Classes\DesktopBackground\Shell\000_RunMikaMemo",
            @"Software\Classes\Directory\Background\Shell\000_RunMikaMemo",
            "Run Memo",
            string.Empty,
            true,
            false),
        new(
            @"Software\Classes\DesktopBackground\Shell\001_NewStickyMemo",
            @"Software\Classes\Directory\Background\Shell\001_NewStickyMemo",
            "New Sticky Memo",
            "--new",
            false,
            false),
        new(
            @"Software\Classes\DesktopBackground\Shell\002_NewTodoMemo",
            @"Software\Classes\Directory\Background\Shell\002_NewTodoMemo",
            "New Todo Memo",
            "--new-todo",
            false,
            true)
    };

    private static readonly string[] LegacyMenuPaths =
    {
        @"Software\Classes\DesktopBackground\Shell\RunMikaMemo",
        @"Software\Classes\Directory\Background\Shell\RunMikaMemo",
        @"Software\Classes\DesktopBackground\Shell\NewStickyMemo",
        @"Software\Classes\Directory\Background\Shell\NewStickyMemo",
        @"Software\Classes\DesktopBackground\Shell\NewTodoMemo",
        @"Software\Classes\Directory\Background\Shell\NewTodoMemo",
        @"Software\Classes\DesktopBackground\Shell\NewMikaMemo",
        @"Software\Classes\Directory\Background\Shell\NewMikaMemo"
    };

    public static void Install(string executablePath)
    {
        UninstallLegacyEntries();

        string executableDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
        string iconPath = Path.Combine(executableDirectory, "Assets", "MikaIcon.ico");
        string menuIconPath = File.Exists(iconPath) ? iconPath : executablePath;

        foreach (ShellMenuRegistration registration in MenuRegistrations)
        {
            foreach (string menuPath in registration.RegistryPaths)
            {
                using RegistryKey? menuKey = Registry.CurrentUser.CreateSubKey(menuPath);
                if (menuKey is null)
                {
                    continue;
                }

                menuKey.SetValue("MUIVerb", registration.MenuLabel, RegistryValueKind.String);
                menuKey.SetValue("Icon", menuIconPath, RegistryValueKind.String);
                menuKey.DeleteValue("Position", throwOnMissingValue: false);
                SetSeparatorValue(menuKey, "SeparatorBefore", registration.SeparatorBefore);
                SetSeparatorValue(menuKey, "SeparatorAfter", registration.SeparatorAfter);

                using RegistryKey commandKey = menuKey.CreateSubKey("command");
                string command = string.IsNullOrWhiteSpace(registration.CommandArgument)
                    ? $"\"{executablePath}\""
                    : $"\"{executablePath}\" {registration.CommandArgument}";
                commandKey.SetValue(string.Empty, command, RegistryValueKind.String);
            }
        }
    }

    public static void Uninstall()
    {
        foreach (ShellMenuRegistration registration in MenuRegistrations)
        {
            foreach (string menuPath in registration.RegistryPaths)
            {
                Registry.CurrentUser.DeleteSubKeyTree(menuPath, throwOnMissingSubKey: false);
            }
        }

        UninstallLegacyEntries();
    }

    private static void UninstallLegacyEntries()
    {
        foreach (string menuPath in LegacyMenuPaths)
        {
            Registry.CurrentUser.DeleteSubKeyTree(menuPath, throwOnMissingSubKey: false);
        }
    }

    private static void SetSeparatorValue(RegistryKey menuKey, string valueName, bool enabled)
    {
        if (enabled)
        {
            menuKey.SetValue(valueName, string.Empty, RegistryValueKind.String);
            return;
        }

        menuKey.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private sealed record ShellMenuRegistration(
        string DesktopBackgroundPath,
        string DirectoryBackgroundPath,
        string MenuLabel,
        string CommandArgument,
        bool SeparatorBefore,
        bool SeparatorAfter)
    {
        public string[] RegistryPaths { get; } = { DesktopBackgroundPath, DirectoryBackgroundPath };
    }
}

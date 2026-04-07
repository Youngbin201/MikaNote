using Microsoft.Win32;

namespace MikaNote.App;

public static class StartupRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "MikaNote";

    public static void EnsureEnabled(string executablePath)
    {
        Enable(executablePath);
    }

    public static void Enable(string executablePath)
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key?.SetValue(StartupValueName, $"\"{executablePath}\" --startup", RegistryValueKind.String);
    }

    public static void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key?.GetValue(StartupValueName) is not null)
        {
            key.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }
    }
}

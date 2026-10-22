using System.IO;
using Microsoft.Win32;

namespace GpuSwitcher;

internal static class StartupPreferences
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GpuSwitcher";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return string.Equals(key?.GetValue(ValueName) as string, StartupCommand(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var command = StartupCommand();
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
            if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string StartupCommand()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The app executable path is unavailable.");
        if (Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "GpuSwitcher.dll");
            if (!File.Exists(assemblyPath))
                throw new InvalidOperationException("The app assembly path is unavailable.");
            return $"\"{processPath}\" \"{assemblyPath}\" --minimized";
        }

        return $"\"{processPath}\" --minimized";
    }
}

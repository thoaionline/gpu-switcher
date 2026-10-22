using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace GpuSwitcher;

internal static class GpuPreferences
{
    private const string RegistryPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private static readonly string BackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuSwitcher", "last-change.json");

    public static string? Read(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return key?.GetValue(path) as string;
    }

    public static string Label(string? value)
    {
        var token = value?.Split(';', StringSplitOptions.TrimEntries)
            .FirstOrDefault(x => x.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase));
        return token?.Split('=', 2).LastOrDefault() switch
        {
            "1" => "Power saving",
            "2" => "High performance",
            "0" => "Windows default",
            _ => "Windows default"
        };
    }

    public static bool IsHighPerformance(string? value) => Label(value) == "High performance";

    public static string WithHighPerformance(string? value)
    {
        var parts = (value ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        parts.Add("GpuPreference=2");
        return string.Join(';', parts) + ';';
    }

    public static ApplyResult Apply(IEnumerable<string> paths)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("Could not open your Windows graphics preferences.");
        var changes = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var original = key.GetValue(path) as string;
                return new PreferenceChange(path, original, WithHighPerformance(original));
            })
            // A recent scan can be stale. Check the saved preference again before any backup or write.
            .Where(x => !IsHighPerformance(x.Original) && x.Original != x.Applied)
            .ToArray();
        if (changes.Length == 0) return new ApplyResult(0, Array.Empty<string>());

        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(changes, new JsonSerializerOptions { WriteIndented = true }));
        var failures = new List<string>();
        var applied = 0;
        foreach (var change in changes)
        {
            try { key.SetValue(change.Path, change.Applied, RegistryValueKind.String); applied++; }
            catch (Exception ex) { failures.Add($"{Path.GetFileName(change.Path)}: {ex.Message}"); }
        }
        return new ApplyResult(applied, failures);
    }

    public static bool CanUndo => File.Exists(BackupPath);

    public static ApplyResult Undo()
    {
        if (!CanUndo) return new ApplyResult(0, Array.Empty<string>());
        var changes = JsonSerializer.Deserialize<PreferenceChange[]>(File.ReadAllText(BackupPath)) ?? [];
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("Could not open your Windows graphics preferences.");
        var failures = new List<string>();
        var restored = 0;
        foreach (var change in changes)
        {
            try
            {
                // A later manual edit wins over this app's older backup.
                if (key.GetValue(change.Path) as string != change.Applied)
                {
                    failures.Add($"{Path.GetFileName(change.Path)}: changed elsewhere; skipped");
                    continue;
                }
                if (change.Original is null) key.DeleteValue(change.Path, throwOnMissingValue: false);
                else key.SetValue(change.Path, change.Original, RegistryValueKind.String);
                restored++;
            }
            catch (Exception ex) { failures.Add($"{Path.GetFileName(change.Path)}: {ex.Message}"); }
        }
        if (failures.Count == 0) File.Delete(BackupPath);
        return new ApplyResult(restored, failures);
    }

    private sealed record PreferenceChange(string Path, string? Original, string Applied);
}

internal sealed record ApplyResult(int Changed, IReadOnlyList<string> Failures);

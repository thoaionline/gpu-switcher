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

    public static string Label(string? value, IReadOnlyList<GpuAdapter> adapters)
    {
        var code = Token(value, "GpuPreference");
        if (code == GpuTarget.SpecificPreferenceCode.ToString())
        {
            var id = Token(value, "SpecificAdapter");
            var adapter = id is null ? null : adapters.FirstOrDefault(x =>
                string.Equals(x.RegistryId, id, StringComparison.OrdinalIgnoreCase));
            return adapter is null ? "Specific GPU (unavailable)" : $"Specific: {adapter.Name}";
        }
        return code switch
        {
            "1" => "Power saving",
            "2" => "High performance",
            "0" => "Windows default",
            _ => "Windows default"
        };
    }

    public static bool Matches(string? value, GpuTarget target)
    {
        if (Token(value, "GpuPreference") != target.PreferenceCode.ToString()) return false;
        var adapterId = Token(value, "SpecificAdapter");
        return string.Equals(adapterId, target.AdapterId, StringComparison.OrdinalIgnoreCase);
    }

    public static string WithTarget(string? value, GpuTarget target)
    {
        var parts = (value ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)
                && !x.StartsWith("SpecificAdapter=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (target.AdapterId is not null) parts.Add($"SpecificAdapter={target.AdapterId}");
        parts.Add($"GpuPreference={target.PreferenceCode}");
        return string.Join(';', parts) + ';';
    }

    public static ApplyResult Apply(IEnumerable<string> paths, GpuTarget target)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("Could not open your Windows graphics preferences.");
        var changes = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var original = key.GetValue(path) as string;
                return new PreferenceChange(path, original, WithTarget(original, target));
            })
            // A recent scan can be stale. Check the saved preference again before any backup or write.
            .Where(x => !Matches(x.Original, target) && x.Original != x.Applied)
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

    private static string? Token(string? value, string name) => value?
        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .LastOrDefault(x => x.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?
        .Split('=', 2).LastOrDefault();

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

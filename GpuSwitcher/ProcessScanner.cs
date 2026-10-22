using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace GpuSwitcher;

internal sealed class ProcessRow : INotifyPropertyChanged
{
    private bool _selected;
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Pids { get; init; }
    public required string Gpu { get; init; }
    public required string Preference { get; init; }
    public required string? RawPreference { get; init; }
    public required IReadOnlyList<long> ObservedGpuLuids { get; init; }
    public required string IneligibleReason { get; init; }
    public required bool Eligible { get; init; }
    public string Note { get; private set; } = "";
    public bool OnOtherGpu { get; private set; }
    public bool NeedsChange { get; private set; }
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
    }

    public void UpdateTarget(GpuTarget target, IReadOnlyList<GpuAdapter> adapters)
    {
        NeedsChange = Eligible && !GpuPreferences.Matches(RawPreference, target);
        var targetAdapter = adapters.FirstOrDefault(x => x.Luid == target.Luid);
        OnOtherGpu = targetAdapter is not null && ObservedGpuLuids.Any(luid =>
        {
            var observedAdapter = adapters.FirstOrDefault(x => x.Luid == luid);
            return observedAdapter is not null && (targetAdapter.RegistryId is not null
                && observedAdapter.RegistryId is not null
                    ? !string.Equals(observedAdapter.RegistryId, targetAdapter.RegistryId,
                        StringComparison.OrdinalIgnoreCase)
                    : observedAdapter.Luid != targetAdapter.Luid);
        });
        Note = !Eligible ? IneligibleReason :
            !NeedsChange ? "Already preferred" :
            OnOtherGpu ? "Using another GPU" : "Ready to switch";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NeedsChange)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OnOtherGpu)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Note)));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal static class ProcessScanner
{
    public static IReadOnlyList<ProcessRow> Scan(
        IReadOnlyList<GpuAdapter> adapters,
        IReadOnlyDictionary<int, IReadOnlyList<GpuActivity>> activity,
        ISet<string> selectedPaths,
        GpuTarget target)
    {
        var processes = new List<(string Name, int Pid, string? Path)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    string? path;
                    try { path = process.MainModule?.FileName; }
                    catch { path = null; }
                    processes.Add((process.ProcessName, process.Id, path));
                }
                catch { /* A process may exit during enumeration. */ }
            }
        }

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var ownPath = Environment.ProcessPath;
        var rows = new List<ProcessRow>();
        foreach (var group in processes.GroupBy(x => x.Path ?? $"<pid:{x.Pid}>", StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var path = first.Path;
            var eligible = path is not null
                && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, ownPath, StringComparison.OrdinalIgnoreCase);
            var rawPreference = path is null ? null : GpuPreferences.Read(path);
            var preference = path is null ? "Unavailable" : GpuPreferences.Label(rawPreference, adapters);
            var observed = group.SelectMany(x => activity.GetValueOrDefault(x.Pid) ?? [])
                .GroupBy(x => x.Luid)
                .Select(g => new GpuActivity(g.Key, g.Max(x => x.Percent)))
                .OrderByDescending(x => x.Percent).ToArray();
            var gpu = observed.Length == 0 ? "No activity observed" : string.Join("  ·  ",
                observed.Take(2).Select(sample =>
                {
                    var adapter = adapters.FirstOrDefault(x => x.Luid == sample.Luid);
                    return $"{(adapter is null ? "Unknown GPU" : $"GPU {adapter.WindowsIndex} · {adapter.Name}")} {sample.Percent:0.#}%";
                }));
            var row = new ProcessRow
            {
                Name = path is null ? first.Name : System.IO.Path.GetFileNameWithoutExtension(path),
                Path = path ?? "Path unavailable",
                Pids = string.Join(", ", group.Select(x => x.Pid)),
                Gpu = gpu,
                Preference = preference,
                RawPreference = rawPreference,
                ObservedGpuLuids = observed.Select(x => x.Luid).ToArray(),
                IneligibleReason = path is null ? "Executable path unavailable" : "Windows/system process",
                Eligible = eligible,
                Selected = path is not null && selectedPaths.Contains(path)
            };
            row.UpdateTarget(target, adapters);
            rows.Add(row);
        }
        return rows.OrderByDescending(x => x.OnOtherGpu)
            .ThenByDescending(x => x.NeedsChange)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

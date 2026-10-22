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
    public required string Note { get; init; }
    public required bool Eligible { get; init; }
    public required bool OnOtherGpu { get; init; }
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal static class ProcessScanner
{
    public static IReadOnlyList<ProcessRow> Scan(
        IReadOnlyList<GpuAdapter> adapters,
        IReadOnlyDictionary<int, IReadOnlyList<GpuActivity>> activity,
        ISet<string> selectedPaths)
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
            var preference = path is null ? "Unavailable" : GpuPreferences.Label(GpuPreferences.Read(path));
            var observed = group.SelectMany(x => activity.GetValueOrDefault(x.Pid) ?? [])
                .GroupBy(x => x.Luid)
                .Select(g => new GpuActivity(g.Key, g.Max(x => x.Percent)))
                .OrderByDescending(x => x.Percent).ToArray();
            var onOtherGpu = observed.Any(sample =>
                adapters.FirstOrDefault(adapter => adapter.Luid == sample.Luid) is { IsHighPerformance: false });
            var gpu = observed.Length == 0 ? "No activity observed" : string.Join("  ·  ",
                observed.Take(2).Select(sample =>
                {
                    var adapter = adapters.FirstOrDefault(x => x.Luid == sample.Luid);
                    return $"{(adapter is null ? "Unknown GPU" : $"GPU {adapter.WindowsIndex} · {adapter.Name}")} {sample.Percent:0.#}%";
                }));
            var note = path is null ? "Executable path unavailable" :
                !eligible ? "Windows/system process" :
                preference == "High performance" ? "Already preferred" :
                onOtherGpu ? "Using another GPU" : "Ready to switch";
            rows.Add(new ProcessRow
            {
                Name = path is null ? first.Name : System.IO.Path.GetFileNameWithoutExtension(path),
                Path = path ?? "Path unavailable",
                Pids = string.Join(", ", group.Select(x => x.Pid)),
                Gpu = gpu,
                Preference = preference,
                Note = note,
                Eligible = eligible,
                OnOtherGpu = onOtherGpu,
                Selected = path is not null && selectedPaths.Contains(path)
            });
        }
        return rows.OrderByDescending(x => x.OnOtherGpu)
            .ThenByDescending(x => x.Eligible && x.Preference != "High performance")
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

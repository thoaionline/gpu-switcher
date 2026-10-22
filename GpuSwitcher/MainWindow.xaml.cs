using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GpuSwitcher;

public partial class MainWindow : Window
{
    private IReadOnlyList<GpuAdapter> _adapters = [];
    private IReadOnlyList<ProcessRow> _rows = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RunAutoCycleAsync();
        UndoButton.IsEnabled = GpuPreferences.CanUndo;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task<bool> RefreshAsync()
    {
        if (_busy) return false;
        SetBusy(true);
        StatusText.Text = "Scanning running processes and sampling GPU activity…";
        var selected = _rows.Where(x => x.Selected && x.Eligible).Select(x => x.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            var result = await Task.Run(async () =>
            {
                var adapters = GpuAdapterDiscovery.Discover();
                IReadOnlyDictionary<int, IReadOnlyList<GpuActivity>> activity;
                string? warning = null;
                try { activity = await GpuEngineSampler.SampleAsync(); }
                catch (Exception ex)
                {
                    activity = new Dictionary<int, IReadOnlyList<GpuActivity>>();
                    warning = ex.Message;
                }
                var rows = ProcessScanner.Scan(adapters, activity, selected);
                return (adapters, rows, warning);
            });
            _adapters = result.adapters;
            _rows = result.rows;
            PreferredGpuText.Text = _adapters.FirstOrDefault(x => x.IsHighPerformance)?.Name ?? "Unavailable";
            OtherGpusText.Text = string.Join(" · ", _adapters.Where(x => !x.IsHighPerformance)
                .Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase));
            if (OtherGpusText.Text.Length == 0) OtherGpusText.Text = "None detected";
            CountText.Text = $"{_rows.Count} processes · {_rows.Count(x => x.Eligible)} eligible apps";
            ApplyAllButton.IsEnabled = _adapters.Count > 1 && _rows.Any(x => x.Eligible && x.Preference != "High performance");
            AutoSwitchToggle.IsEnabled = _adapters.Count > 1 && _adapters.Any(x => x.IsHighPerformance);
            UpdateFilter();
            StatusText.Text = result.warning is null
                ? $"Updated {DateTime.Now:t}. Select apps or apply to all eligible apps."
                : $"Updated {DateTime.Now:t}. GPU activity unavailable: {result.warning}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Scan failed: {ex.Message}";
            ApplyAllButton.IsEnabled = false;
            return false;
        }
        finally { SetBusy(false); }
    }

    private async void AutoSwitchToggle_Checked(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        await RunAutoCycleAsync();
    }

    private void AutoSwitchToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        if (IsLoaded) StatusText.Text = "Automatic switching is off.";
    }

    private async Task RunAutoCycleAsync()
    {
        if (_busy || AutoSwitchToggle.IsChecked != true) return;
        if (!await RefreshAsync() || AutoSwitchToggle.IsChecked != true) return;
        if (_adapters.Count < 2 || !_adapters.Any(x => x.IsHighPerformance))
        {
            AutoSwitchToggle.IsChecked = false;
            StatusText.Text = "Automatic switching stopped because a High performance GPU is unavailable.";
            return;
        }

        var paths = _rows.Where(x => x.Eligible && x.Preference != "High performance")
            .Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0)
        {
            StatusText.Text = $"Auto mode checked at {DateTime.Now:t}. No new apps need switching.";
            return;
        }

        SetBusy(true);
        ApplyResult? result = null;
        try { result = await Task.Run(() => GpuPreferences.Apply(paths)); }
        catch (Exception ex) { StatusText.Text = $"Automatic switch failed: {ex.Message}"; }
        finally { SetBusy(false); }

        if (result is null) return;
        await RefreshAsync();
        StatusText.Text = result.Failures.Count == 0
            ? $"Auto mode set {result.Changed} app{(result.Changed == 1 ? "" : "s")} to High performance. Restart them to use it."
            : $"Auto mode set {result.Changed} apps; {result.Failures.Count} failed. {result.Failures[0]}";
    }

    private void UpdateFilter()
    {
        var search = SearchBox?.Text?.Trim() ?? "";
        var filter = FilterBox?.SelectedIndex ?? 0;
        ProcessList.ItemsSource = _rows.Where(x =>
            (search.Length == 0 || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Path.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (filter switch { 1 => x.OnOtherGpu, 2 => x.Eligible, _ => true })).ToArray();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (ProcessList is not null) UpdateFilter();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void SelectEligible_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows.Where(x => x.Eligible && x.Preference != "High performance")) row.Selected = true;
        StatusText.Text = $"{_rows.Count(x => x.Selected)} apps selected.";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.Selected = false;
        StatusText.Text = "Selection cleared.";
    }

    private async void ApplySelected_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(_rows.Where(x => x.Selected && x.Eligible && x.Preference != "High performance"));

    private async void ApplyAll_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(_rows.Where(x => x.Eligible && x.Preference != "High performance"));

    private async Task ApplyAsync(IEnumerable<ProcessRow> rows)
    {
        if (_busy) return;
        var paths = rows.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) { StatusText.Text = "No apps need a preference change."; return; }
        var answer = MessageBox.Show(this,
            $"Set {paths.Length} app{(paths.Length == 1 ? "" : "s")} to Windows’ High performance GPU?\n\n" +
            "Running apps may need to be restarted. A backup for Undo will be saved in your user profile.",
            "Apply GPU preference", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        SetBusy(true);
        string? notice = null;
        try
        {
            var result = await Task.Run(() => GpuPreferences.Apply(paths));
            notice = $"Saved High performance for {result.Changed} app{(result.Changed == 1 ? "" : "s")}. Restart those apps to apply it.";
            if (result.Failures.Count > 0) MessageBox.Show(this, string.Join("\n", result.Failures),
                "Some apps could not be updated", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save preference", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SetBusy(false); }
        await RefreshAsync();
        if (notice is not null) StatusText.Text = notice;
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        AutoSwitchToggle.IsChecked = false;
        SetBusy(true);
        string? notice = null;
        try
        {
            var result = await Task.Run(GpuPreferences.Undo);
            notice = $"Restored {result.Changed} app preference{(result.Changed == 1 ? "" : "s")}.";
            if (result.Failures.Count > 0) MessageBox.Show(this, string.Join("\n", result.Failures),
                "Some apps were skipped", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not undo", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SetBusy(false); }
        await RefreshAsync();
        if (notice is not null) StatusText.Text = notice;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ApplySelectedButton.IsEnabled = !busy;
        if (busy) ApplyAllButton.IsEnabled = false;
        UndoButton.IsEnabled = !busy && GpuPreferences.CanUndo;
    }
}

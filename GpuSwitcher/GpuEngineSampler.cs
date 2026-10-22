using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GpuSwitcher;

internal sealed record GpuActivity(long Luid, double Percent);

internal static partial class GpuEngineSampler
{
    // A short two-sample PDH query reports activity; it cannot tell which GPU an idle app will use later.
    public static async Task<IReadOnlyDictionary<int, IReadOnlyList<GpuActivity>>> SampleAsync()
    {
        var status = PdhOpenQueryW(null, 0, out var query);
        if (status != 0) throw new InvalidOperationException($"Could not open GPU counters (0x{status:X8}).");
        try
        {
            status = PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", 0, out var counter);
            if (status != 0) throw new InvalidOperationException($"GPU Engine counters are unavailable (0x{status:X8}).");
            status = PdhCollectQueryData(query);
            if (status != 0) throw new InvalidOperationException($"Could not sample GPU counters (0x{status:X8}).");
            await Task.Delay(350);
            status = PdhCollectQueryData(query);
            if (status != 0) throw new InvalidOperationException($"Could not sample GPU counters (0x{status:X8}).");

            uint bytes = 0, count = 0;
            status = PdhGetFormattedCounterArrayW(counter, 0x200, ref bytes, ref count, 0);
            if (bytes == 0) return new Dictionary<int, IReadOnlyList<GpuActivity>>();
            var buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                status = PdhGetFormattedCounterArrayW(counter, 0x200, ref bytes, ref count, buffer);
                if (status != 0) throw new InvalidOperationException($"Could not read GPU counters (0x{status:X8}).");
                var values = new Dictionary<(int Pid, long Luid), double>();
                var stride = Marshal.SizeOf<CounterValueItem>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterValueItem>(buffer + i * stride);
                    if (item.Status is not (0 or 1) || item.Value <= 0 || !double.IsFinite(item.Value)) continue;
                    var instance = Marshal.PtrToStringUni(item.Name);
                    if (instance is null) continue;
                    var match = EngineName().Match(instance);
                    if (!match.Success || !int.TryParse(match.Groups["pid"].Value, out var pid)) continue;
                    var high = Convert.ToUInt32(match.Groups["high"].Value, 16);
                    var low = Convert.ToUInt32(match.Groups["low"].Value, 16);
                    var luid = unchecked((long)(((ulong)high << 32) | low));
                    var key = (pid, luid);
                    // Task Manager also reports the busiest engine, rather than adding unrelated engine percentages.
                    values[key] = Math.Max(values.GetValueOrDefault(key), item.Value);
                }
                return values.GroupBy(x => x.Key.Pid).ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<GpuActivity>)g.Select(x => new GpuActivity(x.Key.Luid, x.Value)).ToArray());
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { PdhCloseQuery(query); }
    }

    [GeneratedRegex(@"^pid_(?<pid>\d+)_luid_0x(?<high>[0-9a-fA-F]+)_0x(?<low>[0-9a-fA-F]+)_phys_", RegexOptions.CultureInvariant)]
    private static partial Regex EngineName();

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValueItem
    {
        public nint Name;
        public uint Status;
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, ref uint itemCount, nint items);
}

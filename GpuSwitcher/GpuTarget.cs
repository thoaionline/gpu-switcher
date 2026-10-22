namespace GpuSwitcher;

internal sealed record GpuTarget(string Label, int PreferenceCode, string? AdapterId = null, long? Luid = null)
{
    public const int SpecificPreferenceCode = 1073741824;

    public string Key => AdapterId is null ? PreferenceCode.ToString() : $"specific:{AdapterId}";

    public static GpuTarget HighPerformance(GpuAdapter? adapter = null) =>
        new("High performance", 2, Luid: adapter?.Luid);

    public static GpuTarget PowerSaving(GpuAdapter? adapter = null) =>
        new("Power saving", 1, Luid: adapter?.Luid);

    public static GpuTarget Specific(GpuAdapter adapter, bool showIndex = false) =>
        new(showIndex ? $"GPU {adapter.WindowsIndex} · {adapter.Name}" : adapter.Name,
            SpecificPreferenceCode,
            adapter.RegistryId, adapter.Luid);
}

using System.Globalization;

namespace Radiant.Core;

public sealed record HardwareDetail(string Label, string Value);
public sealed record HardwareGroup(string Title, IReadOnlyList<HardwareDetail> Rows);

public static class HardwareDetails
{
    public static IReadOnlyList<HardwareGroup> Create(HardwareSnapshot snapshot)
    {
        var processor = new List<HardwareDetail>
        {
            new("Processor", Known(snapshot.CpuName)),
            new("Physical cores", Count(snapshot.CpuCores)),
            new("Logical processors", Count(snapshot.LogicalProcessors)),
            new("Sampled CPU load", snapshot.CpuLoadPercent is >= 0 and <= 100 ? $"{snapshot.CpuLoadPercent.Value.ToString("0.0", CultureInfo.InvariantCulture)}%" : "Unavailable"),
        };
        var graphics = new List<HardwareDetail>();
        foreach (var (adapter, index) in snapshot.Graphics.Select((g, i) => (g, i)))
        {
            graphics.Add(new($"Adapter {index + 1}", Known(adapter.Name)));
            graphics.Add(new("Driver version", Known(adapter.DriverVersion)));
        }
        if (graphics.Count == 0) graphics.Add(new("Graphics adapters", "Unavailable"));
        bool validMemory = snapshot.TotalMemoryBytes is > 0 && snapshot.AvailableMemoryBytes is not null && snapshot.AvailableMemoryBytes <= snapshot.TotalMemoryBytes;
        var memory = new List<HardwareDetail>
        {
            new("Visible to Windows", snapshot.TotalMemoryBytes is > 0 ? GiB(snapshot.TotalMemoryBytes.Value) : "Unavailable"),
            new("Available now", validMemory ? GiB(snapshot.AvailableMemoryBytes!.Value) : "Unavailable"),
        };
        var drives = snapshot.Drives.Select(d => new HardwareDetail(
            $"{d.Name} · {(d.IsSystem ? "Windows drive" : "Fixed drive")}",
            d.TotalBytes > 0 && d.FreeBytes <= d.TotalBytes
                ? $"{GiB(d.FreeBytes)} free of {GiB(d.TotalBytes)} · {(100d * d.FreeBytes / d.TotalBytes).ToString("0.0", CultureInfo.InvariantCulture)}% free"
                : "Unavailable")).ToArray();
        var windows = new List<HardwareDetail>
        {
            new("Windows version", Known(snapshot.OsVersion)),
            new("Architecture", Known(snapshot.Architecture)),
            new("Active power plan", Known(snapshot.PowerPlan)),
            new("Uptime", snapshot.Uptime is { } uptime && uptime >= TimeSpan.Zero ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m" : "Unavailable"),
            new("Game Mode preference", snapshot.GameModeEnabled switch { true => "Explicitly on", false => "Explicitly off", _ => "Unavailable · No explicit preference" }),
        };
        return [new("Processor", processor), new("Graphics & drivers", graphics), new("Physical memory", memory), new("Fixed drives", drives.Length > 0 ? drives : [new("Storage", "Unavailable")]), new("Windows & session", windows)];
    }

    private static string Known(string? value) => string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
    private static string Count(int? value) => value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : "Unavailable";
    private static string GiB(ulong bytes) => $"{(bytes / 1073741824d).ToString("0.0", CultureInfo.InvariantCulture)} GiB";
}

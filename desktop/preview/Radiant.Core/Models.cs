namespace Radiant.Core;

public enum CheckStatus { Pass, Attention, Info, Unknown }

public sealed record CheckResult(string Id, string Title, CheckStatus Status, string Evidence, string Explanation)
{
    public string NextStep => CheckGuidance.For(this);
}
public sealed record GraphicsAdapter(string Name, string? DriverVersion);
public sealed record DriveSnapshot(string Name, bool IsSystem, ulong TotalBytes, ulong FreeBytes);

// No serials, device IDs, usernames, hostnames, IP addresses or MAC addresses.
public sealed record HardwareSnapshot
{
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.Now;
    public string? CpuName { get; init; }
    public int? CpuCores { get; init; }
    public int? LogicalProcessors { get; init; }
    public double? CpuLoadPercent { get; init; }
    public ulong? TotalMemoryBytes { get; init; }
    public ulong? AvailableMemoryBytes { get; init; }
    public IReadOnlyList<GraphicsAdapter> Graphics { get; init; } = [];
    public IReadOnlyList<DriveSnapshot> Drives { get; init; } = [];
    public string? OsName { get; init; }
    public string? OsVersion { get; init; }
    public string? Architecture { get; init; }
    public TimeSpan? Uptime { get; init; }
    public string? PowerPlan { get; init; }
    public bool? GameModeEnabled { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = [];
}

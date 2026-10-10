namespace Radiant.Core;

public sealed record DiskSnapshot(string Name, long TotalBytes, long FreeBytes);

public sealed record SystemSnapshot(
    string CpuName,
    int LogicalProcessors,
    double CpuUsagePercent,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    string GpuName,
    string OsVersion,
    IReadOnlyList<DiskSnapshot> Disks);

public sealed record ProcessSnapshot(int Id, string Name, double CpuPercent, long MemoryBytes);

public sealed record TweakStatus(
    string Id,
    string Title,
    string Description,
    string Category,
    string CurrentValue,
    string TargetValue,
    bool Supported,
    bool RequiresAdmin,
    bool RestartRequired,
    string? UnavailableReason);

public sealed record PowerPlan(string Id, string Name, bool Active);

public sealed record ChangeRecord(
    string Id,
    string TweakId,
    string Before,
    string After,
    string Status,
    DateTimeOffset CreatedAt,
    string? Error,
    bool RestartRequired);

public sealed record SettingTarget(string SettingId, SettingValue Value);

public sealed record OptimizationPlan(
    string TweakId,
    IReadOnlyList<SettingTarget> Targets,
    bool RestartRequired);

/// <summary>The adapter owns the fixed action and setting allowlists.</summary>
public interface ISettingsAdapter
{
    Task<OptimizationPlan> PrepareAsync(string tweakId, string? option, CancellationToken cancellationToken);
    Task<SettingValue> ReadAsync(string settingId, CancellationToken cancellationToken);
    Task WriteAsync(string settingId, SettingValue value, CancellationToken cancellationToken);
}

public static class ChangeStatus
{
    public const string Pending = "pending";
    public const string Applied = "applied";
    public const string Unchanged = "unchanged";
    public const string Failed = "failed";
    public const string Partial = "partial";
    public const string Conflict = "conflict";
    public const string Interrupted = "interrupted";
    public const string NeedsAttention = "needs_attention";
    public const string Restoring = "restoring";
    public const string Restored = "restored";
    public const string RestoreFailed = "restore_failed";
}

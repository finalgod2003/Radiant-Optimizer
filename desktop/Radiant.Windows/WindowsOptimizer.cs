using Radiant.Core;

namespace Radiant.Windows;

public sealed class WindowsOptimizer
{
    private readonly WindowsSettingsAdapter settings = new();
    private readonly WindowsHardwareReader hardware = new();
    private readonly OptimizationEngine engine;

    public WindowsOptimizer(string dataDirectory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This optimizer requires Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        engine = new OptimizationEngine(settings, new FileChangeJournal(Path.Combine(dataDirectory, "recovery")));
    }

    public Task<SystemSnapshot> ReadSystemAsync(CancellationToken cancellationToken = default)
        => hardware.ReadSystemAsync(cancellationToken);

    public Task<IReadOnlyList<ProcessSnapshot>> ReadProcessesAsync(CancellationToken cancellationToken = default)
        => hardware.ReadProcessesAsync(cancellationToken);

    public Task<IReadOnlyList<TweakStatus>> ReadTweaksAsync(CancellationToken cancellationToken = default)
        => settings.ReadTweaksAsync(cancellationToken);

    public Task<IReadOnlyList<PowerPlan>> ReadPowerPlansAsync(CancellationToken cancellationToken = default)
        => Task.Run(PowerPlans.Read, cancellationToken);

    public Task<ChangeRecord> ApplyAsync(string id, string? option = null, CancellationToken cancellationToken = default)
        => engine.ApplyAsync(id, option, cancellationToken);

    public Task<ChangeRecord> RestoreAsync(string changeId, bool force = false, CancellationToken cancellationToken = default)
        => engine.RestoreAsync(changeId, force, cancellationToken);

    public Task<IReadOnlyList<ChangeRecord>> ReadHistoryAsync(CancellationToken cancellationToken = default)
        => engine.ReadHistoryAsync(cancellationToken);
}

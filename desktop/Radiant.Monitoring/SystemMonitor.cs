using System.ComponentModel;
using System.Runtime.InteropServices;
using Radiant.Core;
using Radiant.Windows;

namespace Radiant.Monitoring;

public sealed class SystemMonitor : IDisposable
{
    private readonly Func<CancellationToken, Task<SystemSnapshot>> readSystem;
    private readonly Func<IReadOnlyList<GpuSample>> readNvidia;
    private readonly Func<string, IReadOnlyList<GpuSample>> readWindows;
    private readonly Func<string> nvidiaReason;
    private readonly GpuCounters? gpu;
    private readonly NvmlSensors? nvml;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;
    public SystemMonitor(WindowsOptimizer optimizer)
    {
        ArgumentNullException.ThrowIfNull(optimizer);
        readSystem = optimizer.ReadSystemAsync;
        string? gpuError = null, nvmlError = null;
        try { gpu = new(); } catch (Exception error) when (Recoverable(error)) { gpuError = "Windows GPU initialization failed: " + error.Message; }
        try { nvml = new(); } catch (Exception error) when (Recoverable(error)) { nvmlError = "NVIDIA sensor initialization failed: " + error.Message; }
        readNvidia = () => nvml?.Read() ?? [];
        nvidiaReason = () => nvml?.UnavailableReason ?? nvmlError ?? "NVIDIA temperature is unavailable.";
        readWindows = reason => gpu?.Read(reason) ?? [UnavailableGpu(gpuError ?? "Windows GPU counters are unavailable.", reason)];
    }
    // Fault injection is internal to the isolated test project. Runtime construction always uses Windows/NVML.
    internal SystemMonitor(Func<CancellationToken, Task<SystemSnapshot>> readSystem,
        Func<IReadOnlyList<GpuSample>> readNvidia, Func<string, IReadOnlyList<GpuSample>> readWindows)
    {
        this.readSystem = readSystem; this.readNvidia = readNvidia; this.readWindows = readWindows;
        nvidiaReason = () => "No NVIDIA temperature was reported.";
    }
    public async Task<MonitorSample> SampleAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Metric cpu, used, total;
            try
            {
                var system = await readSystem(cancellationToken).ConfigureAwait(false);
                cpu = system.CpuUsagePercent >= 0 ? Metric.Available(system.CpuUsagePercent) : Metric.Missing("Windows could not calculate CPU activity.");
                total = Metric.Available(system.TotalMemoryBytes);
                used = Metric.Available(system.TotalMemoryBytes - system.AvailableMemoryBytes);
            }
            catch (Exception error) when (Recoverable(error))
            {
                cpu = used = total = Metric.Missing("Windows CPU/memory sampling failed: " + error.Message);
            }
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<GpuSample> nvidia = [];
            var temperatureReason = nvidiaReason();
            try { nvidia = readNvidia(); temperatureReason = nvidiaReason(); }
            catch (Exception error) when (Recoverable(error)) { temperatureReason = "NVIDIA sampling failed: " + error.Message; }
            List<GpuSample> windows;
            try { windows = readWindows(temperatureReason).ToList(); }
            catch (Exception error) when (Recoverable(error)) { windows = [UnavailableGpu("Windows GPU sampling failed: " + error.Message, temperatureReason)]; }
            foreach (var sensor in nvidia)
            {
                var match = windows.Where(w => string.Equals(w.Name, sensor.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (match.Length == 1 && nvidia.Count(n => string.Equals(n.Name, sensor.Name, StringComparison.OrdinalIgnoreCase)) == 1)
                {
                    var old = match[0];
                    windows[windows.IndexOf(old)] = old with { Source = "Windows PDH + NVIDIA NVML", TemperatureCelsius = sensor.TemperatureCelsius,
                        UsagePercent = old.UsagePercent.Value is not null ? old.UsagePercent : sensor.UsagePercent,
                        DedicatedUsedBytes = old.DedicatedUsedBytes.Value is not null ? old.DedicatedUsedBytes : sensor.DedicatedUsedBytes,
                        DedicatedTotalBytes = sensor.DedicatedTotalBytes };
                }
                else windows.Add(sensor); // Identical cards cannot be safely paired by name.
            }
            return new(DateTimeOffset.UtcNow, cpu, used, total, windows);
        }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            try { gpu?.Dispose(); }
            finally { nvml?.Dispose(); }
        }
        finally { gate.Release(); }
    }
    private static bool Recoverable(Exception error) => error is Win32Exception or IOException or UnauthorizedAccessException
        or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or ExternalException;
    private static GpuSample UnavailableGpu(string reason, string temperatureReason)
    {
        var missing = Metric.Missing(reason);
        return new("windows-unavailable", "Windows GPU", "Windows PDH", missing, Metric.Missing(temperatureReason), missing, missing, missing);
    }
}

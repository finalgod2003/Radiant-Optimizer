using System.Globalization;
using System.Text;

namespace Radiant.Monitoring;

public static class MonitoringCsv
{
    public static async Task WriteSensorsAsync(string path, IReadOnlyList<MonitorSample> samples, CancellationToken cancellationToken = default)
    {
        if (samples.Count > 3600) throw new ArgumentOutOfRangeException(nameof(samples));
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("timestamp_utc,cpu_percent,ram_used_bytes,ram_total_bytes,gpu_id,gpu_name,source,gpu_percent,temperature_c,dedicated_used_bytes,dedicated_total_bytes,shared_used_bytes,unavailable_reasons");
        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var missing = Metric.Missing("No GPU readings were available for this sample.");
            var gpuRows = sample.Gpus.Count == 0
                ? [new GpuSample("", "", "", missing, missing, missing, missing, missing)]
                : sample.Gpus.Take(64);
            foreach (var gpu in gpuRows)
            {
                var reasons = new[] { sample.CpuPercent, sample.MemoryUsedBytes, sample.MemoryTotalBytes, gpu.UsagePercent,
                    gpu.TemperatureCelsius, gpu.DedicatedUsedBytes, gpu.DedicatedTotalBytes, gpu.SharedUsedBytes }
                    .Select(m => m.UnavailableReason).Where(r => r is not null).Distinct();
                await writer.WriteLineAsync(string.Join(',', new[] { sample.At.ToUniversalTime().ToString("O"), Number(sample.CpuPercent),
                    Number(sample.MemoryUsedBytes), Number(sample.MemoryTotalBytes), Escape(gpu.Id), Escape(gpu.Name), Escape(gpu.Source),
                    Number(gpu.UsagePercent), Number(gpu.TemperatureCelsius), Number(gpu.DedicatedUsedBytes), Number(gpu.DedicatedTotalBytes),
                    Number(gpu.SharedUsedBytes), Escape(string.Join(" | ", reasons)) }));
            }
        }
    }
    public static async Task WriteFramesAsync(string path, CaptureResult result, CancellationToken cancellationToken = default)
    {
        if (result.Samples.Count > FrameAccumulator.MaximumFrames) throw new ArgumentOutOfRangeException(nameof(result));
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("process_id,process_name,process_started_utc,capture_started_utc,swapchain,present_time_seconds,between_presents_ms,received_utc");
        foreach (var sample in result.Samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', sample.Pid.ToString(CultureInfo.InvariantCulture), Escape(result.Target.Name),
                result.Target.StartedAt.ToUniversalTime().ToString("O"), result.StartedAt.ToUniversalTime().ToString("O"), Escape(sample.SwapChain),
                sample.TimeSeconds.ToString("R", CultureInfo.InvariantCulture), sample.FrameMilliseconds.ToString("R", CultureInfo.InvariantCulture), sample.ReceivedAt.ToUniversalTime().ToString("O")));
        }
    }
    private static string Number(Metric metric) => metric.Value is { } value && double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "";
    internal static string Escape(string text)
    {
        // Quote CSV delimiters and neutralize formula prefixes when opened by spreadsheet programs.
        var clean = text.Replace("\0", "", StringComparison.Ordinal);
        if (clean.TrimStart() is { Length: > 0 } trimmed && "=+-@".Contains(trimmed[0])) clean = "'" + clean;
        return "\"" + clean.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

namespace Radiant.Monitoring;

public sealed record Metric(double? Value, string? UnavailableReason)
{
    public static Metric Available(double value) => double.IsFinite(value) && value >= 0
        ? new(value, null) : Missing("The sensor returned an invalid value.");
    public static Metric Missing(string reason) => new(null, reason);
}

public sealed record GpuSample(string Id, string Name, string Source, Metric UsagePercent,
    Metric TemperatureCelsius, Metric DedicatedUsedBytes, Metric DedicatedTotalBytes, Metric SharedUsedBytes);

public sealed record MonitorSample(DateTimeOffset At, Metric CpuPercent, Metric MemoryUsedBytes,
    Metric MemoryTotalBytes, IReadOnlyList<GpuSample> Gpus);

public sealed record ProcessIdentity(int Pid, string Name, DateTimeOffset StartedAt)
{
    public override string ToString() => $"{Name} · {Pid}";
}

public sealed record FrameSample(int Pid, string SwapChain, double TimeSeconds,
    double FrameMilliseconds, DateTimeOffset ReceivedAt);

public sealed record FrameSummary(int FrameCount, double DurationSeconds, double AverageFps,
    double P95Milliseconds, double P99Milliseconds, double OnePercentLowFps, string SwapChain);

public sealed record FrameUpdate(FrameSummary? Summary, string? Reason, int AcceptedFrames, int RejectedRows);

public sealed record CaptureResult(ProcessIdentity Target, DateTimeOffset StartedAt, DateTimeOffset EndedAt,
    IReadOnlyList<FrameSample> Samples, FrameSummary? Summary, string? Reason, int? ExitCode);

public sealed class BoundedHistory<T>
{
    private readonly Queue<T> items = new();
    private readonly object gate = new();
    public int Capacity { get; }
    public BoundedHistory(int capacity = 600)
    {
        if (capacity is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }
    public void Add(T item)
    {
        lock (gate)
        {
            if (items.Count == Capacity) items.Dequeue();
            items.Enqueue(item);
        }
    }
    public T[] Snapshot() { lock (gate) return items.ToArray(); }
    public void Clear() { lock (gate) items.Clear(); }
}

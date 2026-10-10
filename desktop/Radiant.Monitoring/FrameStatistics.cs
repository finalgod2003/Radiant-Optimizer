using System.Globalization;
using System.Text;

namespace Radiant.Monitoring;

/// <summary>Strict single-record parser for PresentMon's --v1_metrics CSV schema.</summary>
public sealed class PresentMonCsvParser
{
    public const int MaximumLineLength = 16_384;
    private Dictionary<string, int>? columns;
    public bool HasHeader => columns is not null;
    public int RejectedRows { get; private set; }
    public bool TryRead(string line, DateTimeOffset receivedAt, out FrameSample? frame)
    {
        frame = null;
        var values = ParseRecord(line);
        if (values is null) { RejectedRows++; return false; }
        if (columns is null)
        {
            var names = values.Select(v => v.Trim().TrimStart('\uFEFF')).ToArray();
            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length
                || !new[] { "ProcessID", "SwapChainAddress", "TimeInSeconds", "MsBetweenPresents" }
                    .All(required => names.Contains(required, StringComparer.OrdinalIgnoreCase)))
            { RejectedRows++; return false; }
            columns = names.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
            return false;
        }
        if (values.Length != columns.Count) { RejectedRows++; return false; }
        string Field(string name) => values[columns[name]].Trim();
        if (!int.TryParse(Field("ProcessID"), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0
            || !TryNumber(Field("TimeInSeconds"), out var time) || time is < 0 or > 3600
            || !TryNumber(Field("MsBetweenPresents"), out var ms) || ms is <= 0 or > 60_000)
        { RejectedRows++; return false; }
        var swapchain = Field("SwapChainAddress");
        var digits = swapchain.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? swapchain[2..] : swapchain;
        if (digits.Length is < 1 or > 16 || !ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var address))
        { RejectedRows++; return false; }
        frame = new(pid, "0x" + address.ToString("X", CultureInfo.InvariantCulture), time, ms, receivedAt);
        return true;
    }

    private static bool TryNumber(string value, out double number) => double.TryParse(value, NumberStyles.Float,
        CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    internal static string[]? ParseRecord(string line)
    {
        if (line.Length > MaximumLineLength || line.Contains('\n') || line.Contains('\r')) return null;
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        var closedQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                    else { quoted = false; closedQuote = true; }
                }
                else value.Append(ch);
            }
            else if (ch == ',') { fields.Add(value.ToString()); value.Clear(); closedQuote = false; }
            else if (ch == '"' && value.Length == 0 && !closedQuote) quoted = true;
            else if (ch == '"' || closedQuote) return null;
            else value.Append(ch);
            if (fields.Count > 128) return null;
        }
        if (quoted) return null;
        fields.Add(value.ToString());
        return fields.ToArray();
    }
}

public sealed class FrameAccumulator
{
    public const int MaximumFrames = 60_000;
    private readonly int pid;
    private readonly int capacity;
    private readonly List<FrameSample> samples = new();
    private readonly Dictionary<string, double> latest = new(StringComparer.Ordinal);
    private readonly object gate = new();
    public int RejectedRows { get; private set; }
    public FrameAccumulator(int pid, int capacity = MaximumFrames)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
        if (capacity is < 1 or > MaximumFrames) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.pid = pid; this.capacity = capacity;
    }
    public bool AtCapacity { get { lock (gate) return samples.Count >= capacity; } }
    public bool Add(FrameSample sample)
    {
        lock (gate)
        {
            if (sample.Pid != pid || !double.IsFinite(sample.TimeSeconds) || sample.TimeSeconds is < 0 or > 3600
                || !double.IsFinite(sample.FrameMilliseconds) || sample.FrameMilliseconds is <= 0 or > 60_000
                || string.IsNullOrWhiteSpace(sample.SwapChain) || sample.SwapChain.Length > 18
                || samples.Count >= capacity || (latest.TryGetValue(sample.SwapChain, out var last) && sample.TimeSeconds <= last)
                || (!latest.ContainsKey(sample.SwapChain) && latest.Count >= 64))
            { RejectedRows++; return false; }
            latest[sample.SwapChain] = sample.TimeSeconds;
            samples.Add(sample);
            return true;
        }
    }
    public FrameSample[] Snapshot() { lock (gate) return samples.ToArray(); }
    public FrameSummary? SummarizeLive(DateTimeOffset now)
    {
        var all = Snapshot();
        if (all.Length == 0 || now - all[^1].ReceivedAt > TimeSpan.FromSeconds(3) || all[^1].ReceivedAt > now.AddSeconds(1)) return null;
        var latestTime = all.Max(s => s.TimeSeconds);
        var currentStreams = all.GroupBy(s => (s.Pid, s.SwapChain))
            .Where(g => now - g.Max(s => s.ReceivedAt) <= TimeSpan.FromSeconds(3) && g.Max(s => s.ReceivedAt) <= now.AddSeconds(1))
            .SelectMany(g => g).Where(s => s.TimeSeconds >= latestTime - 5 && now - s.ReceivedAt <= TimeSpan.FromSeconds(6) && s.ReceivedAt <= now.AddSeconds(1));
        return Summarize(currentStreams.ToArray());
    }
    public static FrameSummary? Summarize(IReadOnlyList<FrameSample> samples)
    {
        // A process may own several windows. Never add those presentation rates together.
        var stream = samples.Where(s => double.IsFinite(s.FrameMilliseconds) && s.FrameMilliseconds is > 0 and <= 60_000
                && double.IsFinite(s.TimeSeconds) && s.TimeSeconds >= 0)
            .GroupBy(s => (s.Pid, s.SwapChain)).OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(s => s.TimeSeconds)).ThenBy(g => g.Key.SwapChain, StringComparer.Ordinal)
            .FirstOrDefault()?.OrderBy(s => s.TimeSeconds).ToArray();
        if (stream is null || stream.Length < 2) return null;
        var times = stream.Select(s => s.FrameMilliseconds).Order().ToArray();
        var sum = times.Sum();
        var slowCount = Math.Max(1, (int)Math.Ceiling(times.Length * .01));
        double Percentile(double p) => times[Math.Clamp((int)Math.Ceiling(times.Length * p) - 1, 0, times.Length - 1)];
        return new(times.Length, sum / 1000, times.Length * 1000 / sum,
            Percentile(.95), Percentile(.99), 1000 / times.TakeLast(slowCount).Average(), stream[0].SwapChain);
    }
}

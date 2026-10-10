using System.Globalization;
using System.Text.Json;
using Radiant.Core;
using Radiant.Monitoring;
using Radiant.Windows;

DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
var tests = new (string Name, Func<Task> Run)[]
{
    ("quoted CSV honors embedded commas and quotes", () => Check(() => {
        var parser = Parser(); True(parser.TryRead("\"a,\"\"b\"\".exe\",42,0xAb,.100,16.6", Now, out var f)); Equal("0xAB", f!.SwapChain); })),
    ("malformed quote and wrong field counts are rejected", () => Check(() => {
        var p = Parser(); False(p.TryRead("\"bad,42,0x1,.1,16", Now, out _)); False(p.TryRead("a,42,0x1,.1,16,extra", Now, out _)); False(p.TryRead("\"a\"x,42,0x1,.1,16", Now, out _)); })),
    ("schema requires unique named columns", () => Check(() => {
        var p = new PresentMonCsvParser(); p.TryRead("ProcessID,ProcessID,SwapChainAddress,TimeInSeconds,MsBetweenPresents", Now, out _); False(p.HasHeader);
        p.TryRead("ProcessID,SwapChainAddress,CPUStartTime,FrameTime", Now, out _); False(p.HasHeader); })),
    ("column order and casing may differ", () => Check(() => {
        var p = new PresentMonCsvParser(); p.TryRead("msbetweenpresents,timeinseconds,swapchainaddress,processid", Now, out _);
        True(p.TryRead("20,2,0xA,42", Now, out var f)); Equal(20d, f!.FrameMilliseconds); })),
    ("NaN infinity negatives zero and huge durations are rejected", () => Check(() => {
        foreach (var value in new[] { "NaN", "Infinity", "-1", "0", "60001" }) False(Parser().TryRead($"a,42,0x1,.1,{value}", Now, out _));
        foreach (var value in new[] { "NaN", "Infinity", "-1", "3601" }) False(Parser().TryRead($"a,42,0x1,{value},16", Now, out _)); })),
    ("invalid process and swapchain identifiers are rejected", () => Check(() => {
        False(Parser().TryRead("a,-1,0x1,.1,16", Now, out _)); False(Parser().TryRead("a,42,anything,.1,16", Now, out _)); })),
    ("parser rejects oversized lines", () => Check(() => False(Parser().TryRead(new string('a', 16_385), Now, out _)))),
    ("PID mixtures and duplicate or backwards time never enter history", () => Check(() => {
        var a = new FrameAccumulator(42); True(a.Add(F(1))); False(a.Add(F(2) with { Pid = 43 })); False(a.Add(F(1))); False(a.Add(F(.5))); Equal(1, a.Snapshot().Length); })),
    ("multiple swapchains retain independent ordering", () => Check(() => {
        var a = new FrameAccumulator(42); True(a.Add(F(2))); True(a.Add(F(1) with { SwapChain = "0x2" })); Equal(2, a.Snapshot().Length); })),
    ("dominant swapchain FPS is never a sum of concurrent windows", () => Check(() => {
        var summary = FrameAccumulator.Summarize(new[] { F(1, 20), F(2, 20), F(3, 20), F(1, 10) with { SwapChain = "0x2" }, F(2, 10) with { SwapChain = "0x2" } });
        Equal("0x1", summary!.SwapChain); Near(50, summary.AverageFps); Equal(3, summary.FrameCount); })),
    ("average FPS nearest rank percentiles and slowest one percent formula", () => Check(() => {
        var summary = FrameAccumulator.Summarize(Enumerable.Range(1, 100).Select(i => F(i, i)).ToArray())!;
        Near(1000 / 50.5, summary.AverageFps); Near(95, summary.P95Milliseconds); Near(99, summary.P99Milliseconds); Near(10, summary.OnePercentLowFps); Near(5.05, summary.DurationSeconds); })),
    ("empty and single frame do not claim FPS", () => Check(() => { True(FrameAccumulator.Summarize([]) is null); True(FrameAccumulator.Summarize([F(1)]) is null); })),
    ("live FPS expires after three seconds", () => Check(() => {
        var a = new FrameAccumulator(42); a.Add(F(1)); a.Add(F(2)); True(a.SummarizeLive(Now) is not null); True(a.SummarizeLive(Now.AddSeconds(4)) is null); })),
    ("live frame window excludes old segments", () => Check(() => {
        var a = new FrameAccumulator(42); a.Add(F(0, 500)); a.Add(F(10, 20)); a.Add(F(11, 20)); Near(50, a.SummarizeLive(Now)!.AverageFps); })),
    ("a fresh secondary swapchain cannot keep a stale dominant one alive", () => Check(() => {
        var a = new FrameAccumulator(42); for (var i = 0; i < 10; i++) a.Add(F(i * .01, 100) with { ReceivedAt = Now.AddSeconds(-4) });
        a.Add(F(1, 20) with { SwapChain = "0x2" }); a.Add(F(2, 20) with { SwapChain = "0x2" });
        Equal("0x2", a.SummarizeLive(Now)!.SwapChain); Near(50, a.SummarizeLive(Now)!.AverageFps); })),
    ("frame and sensor histories have hard bounds", () => Check(() => {
        var a = new FrameAccumulator(42, 2); a.Add(F(1)); a.Add(F(2)); True(a.AtCapacity); False(a.Add(F(3))); Equal(2, a.Snapshot().Length);
        var b = new BoundedHistory<int>(2); b.Add(1); b.Add(2); b.Add(3); Equal(2, b.Snapshot()[0]); Equal(3, b.Snapshot()[1]); })),
    ("too many swapchains cannot grow the order map forever", () => Check(() => {
        var a = new FrameAccumulator(42); for (var i = 0; i < 64; i++) True(a.Add(F(1) with { SwapChain = $"0x{i:X}" })); False(a.Add(F(1) with { SwapChain = "0xFF" })); })),
    ("GPU LUID and engine group parse is stable across locale", () => Check(() => {
        var p = GpuCounters.Parse("pid_999_luid_0x00000000_0x0000ABCD_phys_0_eng_2_engtype_3D")!.Value;
        Equal("00000000:0000ABCD", p.Adapter); Equal("2", p.Engine!); True(GpuCounters.Parse("invalid") is null); })),
    ("GPU sums same engine processes then takes busiest engine", () => Check(() => {
        var usage = new CounterRead([C(1, 1, 30), C(2, 1, 40), C(1, 2, 90)], null);
        var rows = GpuCounters.Aggregate(usage, new([], null), new([], null), [], "unsupported"); Near(90, rows.Single().UsagePercent.Value!.Value); })),
    ("GPU counters use adapter memory and preserve sensor errors", () => Check(() => {
        var memory = new CounterRead([new("luid_0x0_0x1_phys_0", 2048)], null);
        var rows = GpuCounters.Aggregate(new([], "denied"), memory, new([], "no shared data"), [], "no temperature");
        Near(2048, rows.Single().DedicatedUsedBytes.Value!.Value); Equal("denied", rows.Single().UsagePercent.UnavailableReason!); True(rows.Single().TemperatureCelsius.Value is null); })),
    ("GPU invalid numbers do not become readings", () => Check(() => {
        var rows = GpuCounters.Aggregate(new([C(1, 1, double.NaN)], null), new([], null), new([], null), [new("00000000:00000001", "test", 100)], "missing");
        True(rows.Single().UsagePercent.Value is null); })),
    ("both GPU backends may fail without losing CPU and RAM", async () => {
        using var monitor = new SystemMonitor(_ => Task.FromResult(TestSystem()),
            () => throw new InvalidOperationException("NVML failed"), _ => throw new System.ComponentModel.Win32Exception("PDH failed"));
        var sample = await monitor.SampleAsync(); Near(12.5, sample.CpuPercent.Value!.Value); Near(75, sample.MemoryUsedBytes.Value!.Value);
        True(sample.Gpus.Single().UsagePercent.Value is null); True(sample.Gpus.Single().TemperatureCelsius.UnavailableReason!.Contains("NVML failed")); }),
    ("NVML failure leaves Windows GPU usage intact", async () => {
        using var monitor = new SystemMonitor(_ => Task.FromResult(TestSystem()),
            () => throw new InvalidOperationException("NVML failed"), reason => [TestGpu() with { TemperatureCelsius = Metric.Missing(reason) }]);
        var sample = await monitor.SampleAsync(); Near(20, sample.Gpus.Single().UsagePercent.Value!.Value); True(sample.Gpus.Single().TemperatureCelsius.Value is null); }),
    ("CPU provider failure leaves independent GPU sensors intact", async () => {
        using var monitor = new SystemMonitor(_ => throw new System.ComponentModel.Win32Exception("CPU failed"), () => [], _ => [TestGpu()]);
        var sample = await monitor.SampleAsync(); True(sample.CpuPercent.Value is null); Near(20, sample.Gpus.Single().UsagePercent.Value!.Value); }),
    ("owned helper argument list never asks for UAC or stops others", () => Check(() => {
        var p = PresentMonCapture.BuildStartInfo(@"C:\Program Files\Optimizer\PresentMon.exe", 42, TimeSpan.FromSeconds(10), "Optimizer-test");
        False(p.UseShellExecute); True(p.CreateNoWindow); True(p.ArgumentList.Contains("--output_stdout"));
        False(p.ArgumentList.Contains("--restart_as_admin")); False(p.ArgumentList.Contains("--stop_existing_session")); False(p.ArgumentList.Contains("--terminate_existing_session")); })),
    ("stream reader supports CRLF chunks and bounded records", async () => {
        var rows = new List<string>(); await PresentMonCapture.ReadOutputAsync(new StringReader("a,b\r\nc,d\nlast"), line => { rows.Add(line); return Task.CompletedTask; }, default);
        Equal(3, rows.Count); Equal("a,b", rows[0]); await Throws<InvalidDataException>(() => PresentMonCapture.ReadOutputAsync(new StringReader(new string('x', 20_000)), _ => Task.CompletedTask, default)); }),
    ("stream cancellation is honored", async () => {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Throws<OperationCanceledException>(() => PresentMonCapture.ReadOutputAsync(new StringReader("a,b"), _ => Task.CompletedTask, cancellation.Token)); }),
    ("stdout byte limit stops unbounded valid-length records", async () => {
        using var reader = new RepeatingReader(PresentMonCapture.MaximumOutputBytes + 4096);
        await Throws<InvalidDataException>(() => PresentMonCapture.ReadOutputAsync(reader, _ => Task.CompletedTask, default)); }),
    ("untrusted helper binary cannot execute", async () => {
        var dir = Path.Combine(Path.GetTempPath(), "optimizer-monitor-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try { var file = Path.Combine(dir, "test.exe"); await File.WriteAllTextAsync(file, "not an executable"); await using var capture = new PresentMonCapture(file); True(await capture.CheckAvailabilityAsync() is not null); }
        finally { Directory.Delete(dir, true); } }),
    ("correct-sized but modified helper fails pinned SHA256", async () => {
        var file = Path.Combine(Path.GetTempPath(), "optimizer-untrusted-" + Guid.NewGuid().ToString("N") + ".exe");
        try { await File.WriteAllBytesAsync(file, new byte[980_320]); await using var capture = new PresentMonCapture(file); True((await capture.CheckAvailabilityAsync())!.Contains("integrity")); }
        finally { File.Delete(file); } }),
    ("capture duration bounds are enforced before helper launch", async () => {
        await using var capture = new PresentMonCapture(Path.Combine(Path.GetTempPath(), "missing.exe"));
        await Throws<ArgumentOutOfRangeException>(() => capture.CaptureAsync(new(42, "test", Now), TimeSpan.FromSeconds(1)));
        await Throws<ArgumentOutOfRangeException>(() => capture.CaptureAsync(new(42, "test", Now), TimeSpan.FromSeconds(301))); }),
    ("invalid PID and failed launch never leave capture disposal waiting", async () => {
        var capture = new PresentMonCapture(Path.Combine(Path.GetTempPath(), "optimizer-missing-" + Guid.NewGuid().ToString("N") + ".exe"));
        await Throws<ArgumentOutOfRangeException>(() => capture.CaptureAsync(new(-1, "invalid", Now), TimeSpan.FromSeconds(5)));
        var result = await capture.CaptureAsync(ProcessTargets.Read(Environment.ProcessId)!, TimeSpan.FromSeconds(5));
        True(result.Reason!.Contains("missing")); await capture.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)); }),
    ("CSV export quotes names and blocks spreadsheet formulas", async () => {
        Equal("\"' =SUM(1,2)\"", MonitoringCsv.Escape(" =SUM(1,2)")); Equal("\"a,\"\"b\"", MonitoringCsv.Escape("a,\"b"));
        var path = Path.Combine(Path.GetTempPath(), "optimizer-frames-" + Guid.NewGuid().ToString("N") + ".csv");
        try { await MonitoringCsv.WriteFramesAsync(path, new(new(42, "=cmd", Now), Now, Now, [F(1)], null, null, 0)); var text = await File.ReadAllTextAsync(path); True(text.Contains("\"'=cmd\"")); True(text.Contains("between_presents_ms")); }
        finally { File.Delete(path); } }),
    ("sensor CSV preserves CPU and RAM when no GPU exists", async () => {
        var path = Path.Combine(Path.GetTempPath(), "optimizer-sensors-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            await MonitoringCsv.WriteSensorsAsync(path, [new(Now, Metric.Available(12.5), Metric.Available(75), Metric.Available(100), [])]);
            var lines = await File.ReadAllLinesAsync(path); Equal(2, lines.Length);
            var fields = PresentMonCsvParser.ParseRecord(lines[1])!; Equal(13, fields.Length);
            Equal("12.5", fields[1]); Equal("75", fields[2]); Equal("100", fields[3]);
            for (var i = 4; i <= 11; i++) Equal("", fields[i]); True(fields[12].Contains("No GPU readings"));
        }
        finally { File.Delete(path); }
    }),
    ("actual process identity rejects changed start times and missing PID", () => Check(() => {
        var self = ProcessTargets.Read(Environment.ProcessId)!; True(ProcessTargets.Matches(self)); False(ProcessTargets.Matches(self with { StartedAt = self.StartedAt.AddSeconds(-1) })); True(ProcessTargets.Read(-1) is null); })),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} monitoring checks passed. No game, registry, driver, or system configuration writes.");

if (args.Contains("--hardware-smoke"))
{
    using var monitor = new SystemMonitor(new WindowsOptimizer(Path.Combine(Path.GetTempPath(), "optimizer-monitor-readonly")));
    await monitor.SampleAsync(); await Task.Delay(1100);
    var sample = await monitor.SampleAsync();
    Console.WriteLine(JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true }));
    if (sample.CpuPercent.Value is null || sample.MemoryUsedBytes.Value is null) failures++;
}
var toolIndex = Array.IndexOf(args, "--capture");
if (toolIndex >= 0 && toolIndex + 2 < args.Length)
{
    await using var capture = new PresentMonCapture(args[toolIndex + 1]);
    var target = ProcessTargets.Read(int.Parse(args[toolIndex + 2], CultureInfo.InvariantCulture)) ?? throw new InvalidOperationException("Target missing.");
    var result = await capture.CaptureAsync(target, TimeSpan.FromSeconds(8), update => Console.WriteLine(JsonSerializer.Serialize(update)));
    Console.WriteLine(JsonSerializer.Serialize(result with { Samples = [] }, new JsonSerializerOptions { WriteIndented = true }));
}
var csvIndex = Array.IndexOf(args, "--verify-csv");
if (csvIndex >= 0 && csvIndex + 2 < args.Length)
{
    FrameSample[] ReadCsv(string path)
    {
        var p = new PresentMonCsvParser(); var frames = new List<FrameSample>();
        foreach (var line in File.ReadLines(path)) if (p.TryRead(line, DateTimeOffset.UtcNow, out var frame) && frame is not null) frames.Add(frame);
        True(p.HasHeader); True(frames.Count > 0); Equal(0, p.RejectedRows);
        return frames.ToArray();
    }
    var actual = ReadCsv(args[csvIndex + 1]); var expected = ReadCsv(args[csvIndex + 2]);
    Equal(expected.Length, actual.Length);
    for (var index = 0; index < actual.Length; index++)
    {
        Equal(expected[index].Pid, actual[index].Pid); Equal(expected[index].SwapChain, actual[index].SwapChain);
        Near(expected[index].TimeSeconds, actual[index].TimeSeconds); Near(expected[index].FrameMilliseconds, actual[index].FrameMilliseconds);
    }
    foreach (var group in actual.GroupBy(frame => frame.Pid))
    {
        var accumulator = new FrameAccumulator(group.Key); foreach (var frame in group) True(accumulator.Add(frame));
        var summary = FrameAccumulator.Summarize(accumulator.Snapshot());
        Console.WriteLine(JsonSerializer.Serialize(new { source = "official-PresentMon-v2.6.0-ETL-offline-replay", pid = group.Key, accepted = accumulator.Snapshot().Length, summary }));
    }
    Console.WriteLine($"PASS {actual.Length} official replay rows match the maintainer's v1 gold PID, swapchain, timestamp and frame interval values.");
}
return failures == 0 ? 0 : 1;

PresentMonCsvParser Parser() { var p = new PresentMonCsvParser(); p.TryRead("Application,ProcessID,SwapChainAddress,TimeInSeconds,MsBetweenPresents", Now, out _); return p; }
FrameSample F(double time, double ms = 16) => new(42, "0x1", time, ms, Now);
static CounterValue C(int pid, int engine, double value) => new($"pid_{pid}_luid_0x0_0x1_phys_0_eng_{engine}_engtype_3D", value);
static SystemSnapshot TestSystem() => new("test", 4, 12.5, 100, 25, "test", "test", []);
static GpuSample TestGpu() => new("test", "test", "Windows PDH", Metric.Available(20), Metric.Missing("unsupported"), Metric.Available(5), Metric.Available(10), Metric.Available(1));
static Task Check(Action body) { body(); return Task.CompletedTask; }
static void True(bool condition) { if (!condition) throw new Exception("Expected true."); }
static void False(bool condition) => True(!condition);
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
static void Near(double expected, double actual) { if (Math.Abs(expected - actual) > .000001) throw new Exception($"Expected {expected}; got {actual}."); }
static async Task Throws<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

sealed class RepeatingReader(long remaining) : TextReader
{
    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = (int)Math.Min(remaining, buffer.Length);
        for (var i = 0; i < count; i++) buffer.Span[i] = i % 2 == 0 ? 'a' : '\n';
        remaining -= count;
        return ValueTask.FromResult(count);
    }
}

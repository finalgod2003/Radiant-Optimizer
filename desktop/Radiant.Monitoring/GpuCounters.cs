using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Radiant.Monitoring;

internal sealed record CounterValue(string Instance, double Value);
internal sealed record CounterRead(IReadOnlyList<CounterValue> Values, string? Error);
internal sealed record AdapterInfo(string Id, string Name, double DedicatedBytes);

internal sealed class GpuCounters : IDisposable
{
    private IntPtr query;
    private readonly Dictionary<string, (IntPtr Handle, uint Error)> counters = new();
    private readonly IReadOnlyList<AdapterInfo> adapters = DxgiAdapters.Read();
    private const uint MoreData = 0x800007D2;
    private static readonly Regex InstancePattern = new(@"luid_(0x[0-9a-f]+)_(0x[0-9a-f]+)_phys_(\d+)(?:_eng_(\d+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public GpuCounters()
    {
        try
        {
            var status = Native.PdhOpenQuery(null, 0, out query);
            foreach (var (name, path) in new[] {
                ("usage", @"\GPU Engine(*)\Utilization Percentage"),
                ("dedicated", @"\GPU Adapter Memory(*)\Dedicated Usage"),
                ("shared", @"\GPU Adapter Memory(*)\Shared Usage") })
            {
                var handle = IntPtr.Zero;
                var error = status == 0 ? Native.PdhAddEnglishCounter(query, path, 0, out handle) : status;
                counters[name] = (handle, error);
            }
            if (query != IntPtr.Zero) Native.PdhCollectQueryData(query);
        }
        catch { Dispose(); throw; }
    }
    public IReadOnlyList<GpuSample> Read(string temperatureReason)
    {
        var status = query == IntPtr.Zero ? 0xC0000BBAu : Native.PdhCollectQueryData(query);
        CounterRead ReadCounter(string name) => status == 0 ? ReadArray(counters[name]) : new([], $"Windows GPU counters unavailable (0x{status:X8}).");
        var usage = ReadCounter("usage"); var dedicated = ReadCounter("dedicated"); var shared = ReadCounter("shared");
        return Aggregate(usage, dedicated, shared, adapters, temperatureReason);
    }

    internal static IReadOnlyList<GpuSample> Aggregate(CounterRead usage, CounterRead dedicated, CounterRead shared,
        IReadOnlyList<AdapterInfo> adapters, string temperatureReason)
    {
        static Dictionary<string, double> Memory(CounterRead read) => read.Values
            .Select(v => (Parsed: Parse(v.Instance), v.Value)).Where(v => v.Parsed is not null && double.IsFinite(v.Value) && v.Value >= 0)
            .GroupBy(v => v.Parsed!.Value.Adapter).ToDictionary(g => g.Key, g => g.Sum(x => x.Value));
        var d = Memory(dedicated); var s = Memory(shared);
        // Sum per-process utilization of the same physical engine; take the busiest engine.
        var u = usage.Values.Select(v => (Parsed: Parse(v.Instance), v.Value))
            .Where(v => v.Parsed is not null && v.Parsed.Value.Engine is not null && double.IsFinite(v.Value) && v.Value >= 0)
            .GroupBy(v => (v.Parsed!.Value.Adapter, v.Parsed.Value.Physical, v.Parsed.Value.Engine))
            .Select(g => (g.Key.Adapter, Value: Math.Clamp(g.Sum(v => v.Value), 0, 100)))
            .GroupBy(v => v.Adapter).ToDictionary(g => g.Key, g => g.Max(v => v.Value));
        var keys = adapters.Select(a => a.Id).Concat(u.Keys).Concat(d.Keys).Concat(s.Keys).Distinct().Order().Take(32);
        var result = new List<GpuSample>();
        foreach (var id in keys)
        {
            var info = adapters.FirstOrDefault(a => a.Id == id);
            Metric Get(Dictionary<string, double> values, string? error) => values.TryGetValue(id, out var value)
                ? Metric.Available(value) : Metric.Missing(error ?? "No valid counter sample; the GPU may be idle, unsupported, or still warming up.");
            result.Add(new(id, info?.Name ?? $"GPU {id}", "Windows PDH", Get(u, usage.Error), Metric.Missing(temperatureReason),
                Get(d, dedicated.Error), info is null ? Metric.Missing("Adapter memory capacity is unavailable.") : Metric.Available(info.DedicatedBytes), Get(s, shared.Error)));
        }
        if (result.Count == 0)
        {
            var missing = Metric.Missing(usage.Error ?? "No WDDM GPU counters were reported by Windows.");
            result.Add(new("windows-unavailable", "Windows GPU", "Windows PDH", missing, Metric.Missing(temperatureReason),
                Metric.Missing(dedicated.Error ?? "GPU adapter memory is unavailable."), Metric.Missing("GPU capacity is unavailable."),
                Metric.Missing(shared.Error ?? "GPU shared memory is unavailable.")));
        }
        return result;
    }

    internal static (string Adapter, string Physical, string? Engine)? Parse(string instance)
    {
        if (instance.Length > 1024) return null;
        var match = InstancePattern.Match(instance);
        if (!match.Success) return null;
        if (!uint.TryParse(match.Groups[1].Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high)
            || !uint.TryParse(match.Groups[2].Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low)) return null;
        return ($"{high:X8}:{low:X8}", match.Groups[3].Value, match.Groups[4].Success ? match.Groups[4].Value : null);
    }

    private static CounterRead ReadArray((IntPtr Handle, uint Error) counter)
    {
        if (counter.Error != 0 || counter.Handle == IntPtr.Zero) return new([], $"Windows GPU counter is unsupported (0x{counter.Error:X8}).");
        // Counter instances can change between sizing and retrieval. Retry with a freshly sized bounded buffer.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint bytes = 0;
            var status = Native.PdhGetFormattedCounterArray(counter.Handle, 0x200 | 0x8000, ref bytes, out _, IntPtr.Zero);
            if (status != MoreData || bytes is 0 or > 16_777_216) return new([], $"No valid Windows GPU sample (0x{status:X8}).");
            var buffer = Marshal.AllocHGlobal(checked((int)bytes));
            try
            {
                var allocated = bytes;
                status = Native.PdhGetFormattedCounterArray(counter.Handle, 0x200 | 0x8000, ref bytes, out var count, buffer);
                if (status == MoreData) continue;
                if (status != 0 || count > 65_536 || (ulong)count * (uint)Marshal.SizeOf<FormattedItem>() > allocated)
                    return new([], $"Windows GPU sample failed (0x{status:X8}).");
                var values = new List<CounterValue>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<FormattedItem>(IntPtr.Add(buffer, i * Marshal.SizeOf<FormattedItem>()));
                    if (item.Value.Status is 0 or 1 && double.IsFinite(item.Value.Number))
                    {
                        var name = Marshal.PtrToStringUni(item.Name);
                        if (name is not null) values.Add(new(name, item.Value.Number));
                    }
                }
                return new(values, null);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return new([], "Windows GPU instances changed during sampling; retrying next interval.");
    }
    public void Dispose() { if (query != IntPtr.Zero) { Native.PdhCloseQuery(query); query = IntPtr.Zero; } }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct FormattedValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Number; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FormattedItem { public IntPtr Name; public FormattedValue Value; }
    private static class Native
    {
        [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)] internal static extern uint PdhOpenQuery(string? source, nuint user, out IntPtr query);
        [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)] internal static extern uint PdhAddEnglishCounter(IntPtr query, string path, nuint user, out IntPtr counter);
        [DllImport("pdh.dll")] internal static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")] internal static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
        [DllImport("pdh.dll")] internal static extern uint PdhCloseQuery(IntPtr query);
    }
}

internal static class DxgiAdapters
{
    public static IReadOnlyList<AdapterInfo> Read()
    {
        var result = new List<AdapterInfo>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref iid, out var factory) < 0) return result;
        try
        {
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint index = 0; index < 32; index++)
            {
                if (enumerate(factory, index, out var adapter) < 0) break;
                try
                {
                    if (Method<GetDesc>(adapter, 10)(adapter, out var desc) >= 0 && (desc.Flags & 2) == 0)
                        result.Add(new($"{desc.LuidHigh:X8}:{desc.LuidLow:X8}", desc.Description.Trim(), desc.DedicatedVideoMemory));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }
    private static T Method<T>(IntPtr instance, int index) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc(IntPtr adapter, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow, LuidHigh, Flags;
    }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
}

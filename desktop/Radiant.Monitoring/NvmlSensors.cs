using System.Runtime.InteropServices;
using System.Text;

namespace Radiant.Monitoring;

internal sealed class NvmlSensors : IDisposable
{
    private IntPtr library;
    private bool initialized;
    private Shutdown? shutdown;
    private DeviceCount? count;
    private DeviceHandle? getHandle;
    private DeviceText? name, uuid;
    private DeviceTemperature? temperature;
    private DeviceUtilization? utilization;
    private DeviceMemory? memory;
    public string UnavailableReason { get; private set; } = "GPU temperature needs a supported NVIDIA driver with NVML; other vendors are not supported yet.";

    public NvmlSensors()
    {
        var candidates = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll") };
        foreach (var path in candidates)
        {
            if (!File.Exists(path) || HasReparsePoint(path)) continue;
            // Load only the driver's absolute official path. Its dependencies are restricted to
            // its own directory and System32; neither current directory nor PATH participates.
            library = LoadLibraryEx(path, IntPtr.Zero, 0x100 | 0x800);
            if (library != IntPtr.Zero) break;
        }
        if (library == IntPtr.Zero) return;
        try
        {
            shutdown = Bind<Shutdown>("nvmlShutdown");
            var status = Bind<Init>("nvmlInit_v2")();
            if (status != 0) { UnavailableReason = Error(status); return; }
            initialized = true;
            count = Bind<DeviceCount>("nvmlDeviceGetCount_v2");
            getHandle = Bind<DeviceHandle>("nvmlDeviceGetHandleByIndex_v2");
            name = Bind<DeviceText>("nvmlDeviceGetName");
            uuid = Bind<DeviceText>("nvmlDeviceGetUUID");
            temperature = Bind<DeviceTemperature>("nvmlDeviceGetTemperature");
            utilization = Bind<DeviceUtilization>("nvmlDeviceGetUtilizationRates");
            memory = Bind<DeviceMemory>("nvmlDeviceGetMemoryInfo");
        }
        catch (EntryPointNotFoundException) { UnavailableReason = "The installed NVIDIA NVML does not expose the required read-only APIs."; Dispose(); }
    }
    public IReadOnlyList<GpuSample> Read()
    {
        var result = new List<GpuSample>();
        if (!initialized || count is null || getHandle is null || name is null || uuid is null || temperature is null || utilization is null || memory is null) return result;
        var status = count(out var deviceCount);
        if (status != 0) { UnavailableReason = Error(status); return result; }
        for (uint index = 0; index < Math.Min(deviceCount, 32); index++)
        {
            status = getHandle(index, out var device);
            if (status != 0) continue;
            var deviceName = new StringBuilder(128); var deviceUuid = new StringBuilder(128);
            var nameStatus = name(device, deviceName, 128); var uuidStatus = uuid(device, deviceUuid, 128);
            var tempStatus = temperature(device, 0, out var temp);
            var utilStatus = utilization(device, out var util);
            var memoryStatus = memory(device, out var mem);
            var validMemory = mem.Total is > 0 and < 1_125_899_906_842_624 && mem.Used <= mem.Total && mem.Free <= mem.Total;
            Metric Field(uint code, double value) => code == 0 ? Metric.Available(value) : Metric.Missing(Error(code));
            result.Add(new(uuidStatus == 0 ? "nvml:" + deviceUuid : "nvml:" + index,
                nameStatus == 0 ? deviceName.ToString() : $"NVIDIA GPU {index}", "NVIDIA NVML",
                Field(utilStatus, util.Gpu <= 100 ? util.Gpu : double.NaN), Field(tempStatus, temp <= 150 ? temp : double.NaN),
                Field(memoryStatus, validMemory ? mem.Used : double.NaN), Field(memoryStatus, validMemory ? mem.Total : double.NaN), Metric.Missing("NVML does not report Windows shared GPU memory.")));
        }
        return result;
    }
    private T Bind<T>(string symbol) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));
    private static string Error(uint code) => code switch
    {
        2 => "The NVIDIA sensor request was invalid.", 3 => "The installed NVIDIA GPU or driver does not support this sensor.",
        4 => "The NVIDIA driver denied access to this sensor.", 9 => "The NVIDIA driver is not loaded.",
        15 => "The NVIDIA GPU is no longer available.", _ => $"NVIDIA NVML sensor unavailable (code {code})."
    };
    private static bool HasReparsePoint(string path)
    {
        try
        {
            for (var part = new FileInfo(path) as FileSystemInfo; part is not null; part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
                if ((part.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
    public void Dispose()
    {
        if (initialized) { shutdown?.Invoke(); initialized = false; }
        if (library != IntPtr.Zero) { FreeLibrary(library); library = IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct Memory { public ulong Total, Free, Used; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Init();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Shutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint DeviceCount(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint DeviceHandle(uint index, out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate uint DeviceText(IntPtr device, StringBuilder text, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint DeviceTemperature(IntPtr device, uint sensor, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint DeviceUtilization(IntPtr device, out Utilization value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint DeviceMemory(IntPtr device, out Memory value);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr library);
}

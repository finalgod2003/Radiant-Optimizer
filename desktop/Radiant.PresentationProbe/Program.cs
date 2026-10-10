using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Radiant.Monitoring;

// Developer-only integration fixture: a hidden 64x64 D3D11 swapchain presents
// for eight seconds. It does not inspect/control any other app or mutate settings.
if (args.Length != 2) throw new ArgumentException("Pass the verified PresentMon path and output JSON path.");
var window = Native.CreateWindowEx(0x80, "STATIC", "Optimizer capture fixture", 0x80000000,
    0, 0, 64, 64, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
if (window == IntPtr.Zero) Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
IntPtr swapchain = IntPtr.Zero, device = IntPtr.Zero, context = IntPtr.Zero;
try
{
    var description = new SwapChainDescription { BufferDescription = new() { Width = 64, Height = 64, Format = 28,
        RefreshRate = new() { Numerator = 60, Denominator = 1 } }, SampleDescription = new() { Count = 1 },
        BufferUsage = 0x20, BufferCount = 2, OutputWindow = window, Windowed = 1, SwapEffect = 0 };
    Marshal.ThrowExceptionForHR(Native.D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1, IntPtr.Zero, 0,
        IntPtr.Zero, 0, 7, ref description, out swapchain, out device, out _, out context));
    var table = Marshal.ReadIntPtr(swapchain);
    var present = Marshal.GetDelegateForFunctionPointer<Present>(Marshal.ReadIntPtr(table, 8 * IntPtr.Size));
    using var current = Process.GetCurrentProcess();
    var identity = ProcessTargets.Read(current.Id) ?? throw new InvalidOperationException("Could not read probe identity.");
    await using var capture = new PresentMonCapture(args[0]);
    var captureTask = capture.CaptureAsync(identity, TimeSpan.FromSeconds(8));
    var clock = Stopwatch.StartNew();
    int calls = 0, lastResult = 0;
    while (!captureTask.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(18))
    {
        lastResult = present(swapchain, 0, 0); calls++;
        if (lastResult < 0) Marshal.ThrowExceptionForHR(lastResult);
        await Task.Delay(16);
    }
    var result = await captureTask;
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(new { kind = "hidden-d3d11-presentation-fixture", calls,
        lastPresentHresult = lastResult, capturedFrames = result.Samples.Count, result.Summary, result.Reason, result.ExitCode },
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    Console.WriteLine($"D3D11 present calls: {calls}; actual ETW frames: {result.Samples.Count}; helper exit: {result.ExitCode}; reason: {result.Reason ?? "none"}");
    Environment.ExitCode = result.Samples.Count >= 2 && result.Summary is not null ? 0 : 2;
}
finally
{
    if (context != IntPtr.Zero) Marshal.Release(context);
    if (device != IntPtr.Zero) Marshal.Release(device);
    if (swapchain != IntPtr.Zero) Marshal.Release(swapchain);
    Native.DestroyWindow(window);
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Present(IntPtr self, uint syncInterval, uint flags);
[StructLayout(LayoutKind.Sequential)] struct Rational { public uint Numerator, Denominator; }
[StructLayout(LayoutKind.Sequential)] struct ModeDescription { public uint Width, Height; public Rational RefreshRate; public uint Format, ScanlineOrdering, Scaling; }
[StructLayout(LayoutKind.Sequential)] struct SampleDescription { public uint Count, Quality; }
[StructLayout(LayoutKind.Sequential)] struct SwapChainDescription { public ModeDescription BufferDescription; public SampleDescription SampleDescription; public uint BufferUsage, BufferCount; public IntPtr OutputWindow; public int Windowed; public uint SwapEffect, Flags; }
static class Native
{
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr window);
    [DllImport("d3d11.dll")] public static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter, uint driverType, IntPtr software, uint flags, IntPtr levels, uint levelCount, uint sdkVersion, ref SwapChainDescription description, out IntPtr swapchain, out IntPtr device, out uint level, out IntPtr context);
}

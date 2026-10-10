using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Radiant.Core;

namespace Radiant.Windows;

internal sealed class WindowsHardwareReader
{
    public async Task<SystemSnapshot> ReadSystemAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cpuName = ReadCpuName();
        var gpuName = ReadDisplayAdapters();
        var disks = await Task.Run(ReadDisks, cancellationToken);
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!Native.GlobalMemoryStatusEx(ref memory))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read physical memory.");
        var cpu = await ReadCpuActivityAsync(cancellationToken);
        return new SystemSnapshot(cpuName, Environment.ProcessorCount, cpu,
            checked((long)memory.TotalPhysical), checked((long)memory.AvailablePhysical),
            gpuName, ReadOsVersion(), disks);
    }

    public async Task<IReadOnlyList<ProcessSnapshot>> ReadProcessesAsync(CancellationToken cancellationToken)
    {
        var samples = new List<ProcessSample>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                if (cancellationToken.IsCancellationRequested) { process.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
                try
                {
                    // The same open Process object and start time identify the instance.
                    samples.Add(new ProcessSample(process, process.ProcessName, process.StartTime.ToUniversalTime(),
                        process.TotalProcessorTime.TotalMilliseconds, Stopwatch.GetTimestamp()));
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Protected/system processes may deny CPU access; keep their names visible.
                    try { samples.Add(new ProcessSample(process, process.ProcessName, null, -1, Stopwatch.GetTimestamp())); }
                    catch (Exception fallback) when (fallback is Win32Exception or InvalidOperationException or NotSupportedException) { process.Dispose(); }
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            var result = new List<ProcessSnapshot>();
            foreach (var sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    sample.Process.Refresh();
                    if (sample.Process.HasExited) continue;
                    var cpu = -1d;
                    if (sample.StartedAt is not null && sample.StartedAt == sample.Process.StartTime.ToUniversalTime())
                    {
                        var elapsed = Stopwatch.GetElapsedTime(sample.Timestamp).TotalMilliseconds;
                        cpu = Math.Clamp((sample.Process.TotalProcessorTime.TotalMilliseconds - sample.CpuMilliseconds)
                            / elapsed / Environment.ProcessorCount * 100, 0, 100);
                    }
                    result.Add(new ProcessSnapshot(sample.Process.Id, sample.Name, cpu, sample.Process.WorkingSet64));
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    result.Add(new ProcessSnapshot(sample.Process.Id, sample.Name, -1, -1));
                }
            }
            return result.OrderByDescending(process => process.CpuPercent).ThenByDescending(process => process.MemoryBytes).ToArray();
        }
        finally { foreach (var sample in samples) sample.Process.Dispose(); }
    }

    private static async Task<double> ReadCpuActivityAsync(CancellationToken cancellationToken)
    {
        if (!Native.GetSystemTimes(out var idleBefore, out var kernelBefore, out var userBefore)) return -1;
        await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        if (!Native.GetSystemTimes(out var idleAfter, out var kernelAfter, out var userAfter)) return -1;
        var total = (kernelAfter.Value - kernelBefore.Value) + (userAfter.Value - userBefore.Value);
        if (total == 0) return -1;
        var idle = idleAfter.Value - idleBefore.Value;
        // Kernel time includes idle time in the Windows GetSystemTimes contract.
        return Math.Clamp(100d * (total - Math.Min(idle, total)) / total, 0, 100);
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() is { Length: > 0 } name ? name : "Unavailable";
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return "Unavailable"; }
    }

    private static IReadOnlyList<DiskSnapshot> ReadDisks()
    {
        var disks = new List<DiskSnapshot>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                // Avoid waking removable media and blocking on disconnected network shares.
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    disks.Add(new DiskSnapshot(drive.Name, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                disks.Add(new DiskSnapshot(drive.Name, -1, -1));
            }
        }
        return disks;
    }

    private static string ReadDisplayAdapters()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (uint index = 0; index < 64; index++)
        {
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
            if (!Native.EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.StateFlags & 8) == 0 && !string.IsNullOrWhiteSpace(device.DeviceString)) names.Add(device.DeviceString);
        }
        return names.Count == 0 ? "Unavailable" : string.Join(" / ", names);
    }

    private static string ReadOsVersion()
    {
        var os = Environment.OSVersion.Version;
        var name = os.Major >= 10 ? os.Build >= 22000 ? "Windows 11" : "Windows 10" : "Windows";
        return $"{name} {os} ({RuntimeInformation.OSArchitecture})";
    }

    private sealed record ProcessSample(Process Process, string Name, DateTime? StartedAt, double CpuMilliseconds, long Timestamp);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceKey;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayDevices(string? device, uint deviceNumber, ref DisplayDevice displayDevice, uint flags);
    }
}

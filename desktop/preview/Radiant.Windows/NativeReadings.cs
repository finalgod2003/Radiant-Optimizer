using System.Runtime.InteropServices;

namespace Radiant.Windows;

internal static class NativeReadings
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatus
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatus>();
        public uint Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    internal static (ulong? Total, ulong? Available) Memory()
    {
        var status = new MemoryStatus();
        return GlobalMemoryStatusEx(status) ? (status.TotalPhysical, status.AvailablePhysical) : (null, null);
    }

    internal static (ulong Idle, ulong Total)? CpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        return (idle.Value, kernel.Value + user.Value);
    }

    internal static string? ActivePowerPlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero) return null;
        try
        {
            var id = Marshal.PtrToStructure<Guid>(pointer);
            // Keep user-defined names (which can contain personal information) out of reports and web previews.
            if (id == Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")) return "Balanced";
            if (id == Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")) return "High performance";
            if (id == Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a")) return "Power saver";
            return "Custom power plan";
        }
        finally { _ = LocalFree(pointer); }
    }
}

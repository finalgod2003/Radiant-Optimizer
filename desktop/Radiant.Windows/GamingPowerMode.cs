using System.ComponentModel;
using System.Runtime.InteropServices;
using Radiant.Core;

namespace Radiant.Windows;

/// <summary>Documented Windows 11 AC preference. Battery policy is never written.</summary>
internal static class GamingPowerMode
{
    internal const string SettingId = "power.ac-performance-mode";
    internal static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    private static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    internal static SettingValue Read()
    {
        try
        {
            EnsureWindows11();
            Check(PowerGetUserConfiguredACPowerMode(out var mode));
            Validate(mode);
            return SettingValue.String(mode.ToString("D"));
        }
        catch (EntryPointNotFoundException)
        { throw new PlatformNotSupportedException("This Windows build does not expose the supported AC power-mode interface."); }
    }

    internal static void EnsureCanApply()
    {
        _ = Read();
        if (!GetSystemPowerStatus(out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (status.AcLineStatus != 1)
            throw new InvalidOperationException("Connect your PC to AC power before requesting Best Performance. Battery settings will stay unchanged.");
    }

    internal static void Write(SettingValue value)
    {
        EnsureWindows11();
        if (value.Kind != SettingValueKind.String || !Guid.TryParseExact(value.Data, "D", out var mode))
            throw new InvalidDataException("AC power mode requires its saved native GUID.");
        Validate(mode);
        // Recovery is allowed on battery too: it restores only the saved AC preference.
        try { Check(PowerSetUserConfiguredACPowerMode(in mode)); }
        catch (EntryPointNotFoundException)
        { throw new PlatformNotSupportedException("The AC power-mode interface is unavailable. The original backup is retained."); }
    }

    internal static string Describe(SettingValue value) => value.Data switch
    {
        "ded574b5-45a0-4f42-8737-46345c09c238" => "Best performance",
        "961cc777-2547-4f9d-8174-7d86181b8a7a" => "Best power efficiency",
        "00000000-0000-0000-0000-000000000000" => "Balanced",
        _ => "Unsupported power mode"
    };
    private static void Validate(Guid mode)
    {
        if (mode != BestPerformance && mode != BestEfficiency && mode != Guid.Empty)
            throw new InvalidDataException("Windows returned an unsupported power-mode GUID. No change was made.");
    }
    private static void EnsureWindows11()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new PlatformNotSupportedException("AC power-mode adjustment requires Windows 11 with the supported power API.");
    }
    private static void Check(uint error)
    {
        if (error == 0) return;
        if (error == 5) throw new UnauthorizedAccessException("Windows policy denied changing this power preference. No elevation was requested.");
        throw new Win32Exception(unchecked((int)error));
    }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("powrprof.dll")] private static extern uint PowerGetUserConfiguredACPowerMode(out Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerSetUserConfiguredACPowerMode(in Guid mode);
}

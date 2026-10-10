using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Radiant.Core;

namespace Radiant.Windows;

/// <summary>Native power APIs avoid localized CLI parsing and shell execution.</summary>
internal static class PowerPlans
{
    private const uint AccessScheme = 16;
    private const uint ErrorNoMoreItems = 259;
    private const uint ErrorMoreData = 234;

    public static IReadOnlyList<PowerPlan> Read()
    {
        var active = ReadActive();
        var plans = new List<PowerPlan>();
        for (uint index = 0; index < 256; index++)
        {
            var scheme = Guid.Empty;
            uint length = 16;
            var error = Native.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, ref scheme, ref length);
            if (error == ErrorNoMoreItems) return plans;
            ThrowIfError(error, "enumerate power plans");
            if (length != 16) throw new IOException("Windows returned an invalid power plan identifier.");
            plans.Add(new PowerPlan(scheme.ToString("D"), ReadName(scheme), scheme == active));
        }
        throw new IOException("Windows returned too many power plans to enumerate safely.");
    }

    public static Guid ReadActive()
    {
        var error = Native.PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        try
        {
            ThrowIfError(error, "read the active power plan");
            if (pointer == IntPtr.Zero) throw new IOException("Windows did not return an active power plan.");
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally { if (pointer != IntPtr.Zero) Native.LocalFree(pointer); }
    }

    public static void Activate(Guid scheme)
    {
        // Applies to restoration too: a removed plan cannot be recreated implicitly.
        if (!Read().Any(plan => Guid.Parse(plan.Id) == scheme))
            throw new InvalidOperationException("This power plan no longer exists. Refresh the list and choose an existing plan.");
        ThrowIfError(Native.PowerSetActiveScheme(IntPtr.Zero, ref scheme), "switch the active power plan");
        if (ReadActive() != scheme) throw new IOException("Windows did not activate the requested power plan.");
    }

    private static string ReadName(Guid scheme)
    {
        uint size = 0;
        var error = Native.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (error is not (0 or ErrorMoreData)) ThrowIfError(error, "read the power plan name");
        if (size is 0 or > 131072) return scheme.ToString("D");
        var bytes = new byte[size];
        ThrowIfError(Native.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, bytes, ref size), "read the power plan name");
        return Encoding.Unicode.GetString(bytes, 0, checked((int)Math.Min(size, (uint)bytes.Length))).TrimEnd('\0');
    }

    private static void ThrowIfError(uint error, string action)
    {
        if (error == 0) return;
        if (error == 5) throw new UnauthorizedAccessException($"Windows denied permission to {action}. This may require an administrator or be controlled by policy.");
        throw new Win32Exception(unchecked((int)error), $"Cannot {action}: {new Win32Exception(unchecked((int)error)).Message}");
    }

    private static class Native
    {
        [DllImport("powrprof.dll")]
        internal static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subgroupGuid,
            uint accessFlags, uint index, ref Guid buffer, ref uint bufferSize);
        [DllImport("powrprof.dll")]
        internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
        [DllImport("powrprof.dll")]
        internal static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);
        [DllImport("powrprof.dll")]
        internal static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid,
            IntPtr subgroupGuid, IntPtr powerSettingGuid, [Out] byte[]? buffer, ref uint bufferSize);
        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}

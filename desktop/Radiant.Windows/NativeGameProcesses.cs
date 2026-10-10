using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Radiant.Core;

[assembly: InternalsVisibleTo("Radiant.Processes.Tests")]

namespace Radiant.Windows;

/// <summary>Only three game executables, in the current Windows session. A held
/// handle and creation time prevent a PID recycled during an operation being targeted.</summary>
public sealed class NativeGameProcesses : IGameProcessAccess
{
    private readonly bool testSelf;
    public NativeGameProcesses() { }
    private NativeGameProcesses(bool testSelf) { this.testSelf = testSelf; }
    internal static NativeGameProcesses ForCurrentTestProcess() => new(true);
    private const uint QueryInformation = 0x0400, QueryLimited = 0x1000, SetInformation = 0x0200, Synchronize = 0x100000;

    public IReadOnlyList<GameProcessTarget> ListTargets()
    {
        var result = new List<GameProcessTarget>();
        var names = new[] { "cs2", "FortniteClient-Win64-Shipping", "VALORANT-Win64-Shipping" };
        var candidates = testSelf ? new[] { Process.GetCurrentProcess() } : names.SelectMany(Process.GetProcessesByName).ToArray();
        foreach (var process in candidates)
        {
            using (process)
            {
                try
                {
                    using var handle = OpenRaw(process.Id, QueryLimited | Synchronize);
                    EnsureSession(process.Id);
                    var name = testSelf ? "cs2" : ReadName(handle);
                    var target = new GameProcessTarget(name, process.Id, ReadCreation(handle));
                    target.Validate(); EnsureAlive(handle); result.Add(target);
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException) { }
            }
        }
        return result.OrderBy(t => t.GameName).ThenBy(t => t.Pid).ToArray();
    }

    public ProcessTuningState Read(GameProcessTarget target)
    {
        using var handle = OpenVerified(target, QueryInformation | Synchronize);
        var priority = GetPriorityClass(handle);
        if (priority == 0) ThrowNative();
        if (!ProcessPriority.Known(priority)) throw new IOException("Windows returned an unknown priority class.");
        if (GetActiveProcessorGroupCount() != 1)
            return new(priority, 0, 0, "Core selection supports single-group PCs only. Priority control remains available.");
        if (!GetProcessAffinityMask(handle, out var affinity, out var system))
            return new(priority, 0, 0, "Windows did not expose this process's CPU mask. " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        EnsureAlive(handle);
        return new(priority, affinity.ToUInt64(), system.ToUInt64(), affinity == UIntPtr.Zero || system == UIntPtr.Zero
            ? "The game does not expose a usable CPU mask." : null);
    }

    public bool HasExited(GameProcessTarget target)
    {
        try { using var handle = OpenVerified(target, QueryLimited | Synchronize); return false; }
        catch (GameProcessEndedException) { return true; }
    }

    public void Write(GameProcessTarget target, string field, SettingValue value)
    {
        using var handle = OpenVerified(target, QueryInformation | SetInformation | Synchronize);
        if (field == "priority")
        {
            if (value.Kind != SettingValueKind.DWord || !uint.TryParse(value.Data, out var priority) || !ProcessPriority.Known(priority))
                throw new InvalidDataException("Invalid saved priority.");
            if (!SetPriorityClass(handle, priority)) ThrowNative();
        }
        else if (field == "affinity")
        {
            if (value.Kind != SettingValueKind.String || !ulong.TryParse(value.Data, NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out var mask) || mask == 0 || GetActiveProcessorGroupCount() != 1)
                throw new InvalidDataException("Unsupported CPU selection.");
            if (!GetProcessAffinityMask(handle, out _, out var system)) ThrowNative();
            if ((mask & ~system.ToUInt64()) != 0) throw new IOException("The selected processors are no longer available.");
            if (!SetProcessAffinityMask(handle, new UIntPtr(mask))) ThrowNative();
        }
        else throw new InvalidDataException("Unknown process control.");
    }

    private SafeProcessHandle OpenVerified(GameProcessTarget target, uint rights)
    {
        target.Validate();
        if (testSelf && target.Pid != Environment.ProcessId) throw new InvalidOperationException("Native verification is limited to its own process.");
        var handle = OpenRaw(target.Pid, rights);
        try
        {
            EnsureAlive(handle);
            if (ReadCreation(handle) != target.CreatedFileTime) throw new GameProcessEndedException();
            EnsureSession(target.Pid);
            var name = ReadName(handle);
            if (testSelf ? name != Path.GetFileNameWithoutExtension(Environment.ProcessPath)!.ToLowerInvariant() : name != target.Name)
                throw new InvalidDataException("The executable identity does not match the selected game.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    private static SafeProcessHandle OpenRaw(int pid, uint rights)
    {
        var handle = OpenProcess(rights, false, pid);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error(); handle.Dispose();
        if (error == 87) throw new GameProcessEndedException();
        if (error == 5) throw new UnauthorizedAccessException("Windows or the game's protection refused access. No elevation or bypass is attempted.");
        throw new Win32Exception(error);
    }
    private static void EnsureAlive(SafeProcessHandle handle)
    {
        var result = WaitForSingleObject(handle, 0);
        if (result == 0) throw new GameProcessEndedException();
        if (result != 258) ThrowNative();
    }
    private static long ReadCreation(SafeProcessHandle handle)
    {
        if (!GetProcessTimes(handle, out var created, out _, out _, out _)) ThrowNative();
        return created;
    }
    private static string ReadName(SafeProcessHandle handle)
    {
        var buffer = new StringBuilder(32768); var size = buffer.Capacity;
        if (!QueryFullProcessImageName(handle, 0, buffer, ref size)) ThrowNative();
        return Path.GetFileNameWithoutExtension(buffer.ToString()).ToLowerInvariant();
    }
    private static void EnsureSession(int pid)
    {
        if (!ProcessIdToSessionId((uint)pid, out var target)) ThrowNative();
        if (!ProcessIdToSessionId((uint)Environment.ProcessId, out var current)) ThrowNative();
        if (target != current) throw new UnauthorizedAccessException("Only games in this Windows session are supported.");
    }
    private static void ThrowNative() => throw new Win32Exception(Marshal.GetLastWin32Error());
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetPriorityClass(SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetPriorityClass(SafeProcessHandle process, uint priority);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessAffinityMask(SafeProcessHandle process, out UIntPtr processMask, out UIntPtr systemMask);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProcessAffinityMask(SafeProcessHandle process, UIntPtr mask);
    [DllImport("kernel32.dll")] private static extern ushort GetActiveProcessorGroupCount();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
}

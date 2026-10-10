using System.ComponentModel;
using System.Diagnostics;

namespace Radiant.Monitoring;

public static class ProcessTargets
{
    public static IReadOnlyList<ProcessIdentity> List()
    {
        var result = new List<ProcessIdentity>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.Id > 0)
                        result.Add(new(process.Id, process.ProcessName, process.StartTime.ToUniversalTime()));
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToArray();
    }

    public static ProcessIdentity? Read(int pid)
    {
        if (pid <= 0) return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : new(pid, process.ProcessName, process.StartTime.ToUniversalTime());
        }
        catch (Exception error) when (error is ArgumentException or Win32Exception or InvalidOperationException or NotSupportedException) { return null; }
    }

    public static bool Matches(ProcessIdentity target)
    {
        var current = Read(target.Pid);
        return current is not null && current.StartedAt == target.StartedAt
            && string.Equals(current.Name, target.Name, StringComparison.OrdinalIgnoreCase);
    }
}

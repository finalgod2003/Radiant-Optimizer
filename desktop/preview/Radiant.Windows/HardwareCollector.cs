using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Radiant.Core;

namespace Radiant.Windows;

public sealed class HardwareCollector
{
    public Task<HardwareSnapshot> CollectAsync(CancellationToken token = default) => Task.Run(() => Collect(token), token);

    private static HardwareSnapshot Collect(CancellationToken token)
    {
        var issues = new List<string>();
        var watch = Stopwatch.StartNew();
        var before = NativeReadings.CpuTimes();
        var memory = NativeReadings.Memory();
        if (memory.Total is null) issues.Add("Physical memory readings are unavailable.");
        string? cpuName = null, osName = null, osVersion = null, power = null;
        int? cores = null, threads = null;
        bool? gameMode = null;
        var graphics = new List<GraphicsAdapter>();
        var drives = new List<DriveSnapshot>();

        Read("Processor", () => Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor", row =>
        {
            cpuName = string.IsNullOrWhiteSpace(cpuName) ? Text(row["Name"]) : cpuName;
            cores = (cores ?? 0) + Convert.ToInt32(row["NumberOfCores"], CultureInfo.InvariantCulture);
            threads = (threads ?? 0) + Convert.ToInt32(row["NumberOfLogicalProcessors"], CultureInfo.InvariantCulture);
        }));
        Read("Graphics", () => Query("SELECT Name, DriverVersion FROM Win32_VideoController", row =>
        {
            var name = Text(row["Name"]);
            if (name is not null) graphics.Add(new(name, Text(row["DriverVersion"])));
        }));
        Read("Operating system", () => Query("SELECT Caption, Version FROM Win32_OperatingSystem", row =>
        {
            osName = Text(row["Caption"]);
            osVersion = Text(row["Version"]);
        }));
        Read("Storage", () =>
        {
            var system = Path.GetPathRoot(Environment.SystemDirectory);
            foreach (var drive in DriveInfo.GetDrives())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    drives.Add(new(drive.Name.TrimEnd('\\'), string.Equals(drive.Name, system, StringComparison.OrdinalIgnoreCase), checked((ulong)drive.TotalSize), checked((ulong)drive.AvailableFreeSpace)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { issues.Add("One fixed drive could not be read."); }
            }
        });
        Read("Power plan", () => power = NativeReadings.ActivePowerPlan());
        Read("Game Mode", () =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", writable: false);
            gameMode = key?.GetValue("AutoGameModeEnabled") is int flag && flag is 0 or 1 ? flag == 1 : null;
        });

        var remaining = Math.Max(0, 600 - (int)Math.Min(watch.ElapsedMilliseconds, 600));
        if (remaining > 0 && token.WaitHandle.WaitOne(remaining)) token.ThrowIfCancellationRequested();
        var after = NativeReadings.CpuTimes();
        double? cpuLoad = null;
        if (before is { } start && after is { } end && end.Total > start.Total && end.Idle >= start.Idle)
            cpuLoad = Math.Clamp(100d * (1 - (double)(end.Idle - start.Idle) / (end.Total - start.Total)), 0, 100);

        token.ThrowIfCancellationRequested();
        return new HardwareSnapshot
        {
            CollectedAt = DateTimeOffset.Now, CpuName = cpuName, CpuCores = cores, LogicalProcessors = threads,
            CpuLoadPercent = cpuLoad, TotalMemoryBytes = memory.Total, AvailableMemoryBytes = memory.Available,
            Graphics = graphics, Drives = drives, OsName = osName, OsVersion = osVersion,
            Architecture = RuntimeInformation.OSArchitecture.ToString(), Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            PowerPlan = power, GameModeEnabled = gameMode, Issues = issues.Distinct().ToArray(),
        };

        void Read(string category, Action action)
        {
            token.ThrowIfCancellationRequested();
            try { action(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidCastException or OverflowException)
            { issues.Add($"{category} information is unavailable. Windows did not return a usable reading."); }
        }

        void Query(string query, Action<ManagementBaseObject> read)
        {
            using var search = new ManagementObjectSearcher("root\\cimv2", query);
            search.Options.Timeout = TimeSpan.FromSeconds(5);
            using var rows = search.Get();
            foreach (ManagementBaseObject row in rows)
            {
                using (row) { token.ThrowIfCancellationRequested(); read(row); }
            }
        }
    }

    private static string? Text(object? value)
    {
        var text = value?.ToString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}

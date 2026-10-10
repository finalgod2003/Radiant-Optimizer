using System.Globalization;

namespace Radiant.Core;

public static class Checks
{
    public static CheckResult ForMemory(ulong? totalBytes, ulong? availableBytes)
    {
        if (totalBytes is not > 0 || availableBytes is null || availableBytes > totalBytes)
            return new("memory", "Memory headroom", CheckStatus.Unknown,
                "Available physical memory could not be read.",
                "Refresh to try again. An unavailable reading is not a failed check.");
        double availablePercent = 100d * availableBytes.Value / totalBytes.Value;
        return new("memory", "Memory headroom",
            availablePercent < 10 ? CheckStatus.Attention : CheckStatus.Pass,
            $"{GiB(availableBytes.Value)} GiB available of {GiB(totalBytes.Value)} GiB ({availablePercent.ToString("0.0", CultureInfo.InvariantCulture)}%).",
            "This check flags less than 10% available physical memory. It is a momentary reading; running apps can change it.");
    }

    public static CheckResult ForStorage(ulong? totalBytes, ulong? freeBytes)
    {
        if (totalBytes is not > 0 || freeBytes is null || freeBytes > totalBytes)
            return new("storage", "System drive space", CheckStatus.Unknown,
                "Free space on the Windows drive could not be read.",
                "Only the drive containing Windows is evaluated. Other drives do not replace this reading.");
        double freePercent = 100d * freeBytes.Value / totalBytes.Value;
        return new("storage", "System drive space",
            freePercent < 15 ? CheckStatus.Attention : CheckStatus.Pass,
            $"{GiB(freeBytes.Value)} GiB free of {GiB(totalBytes.Value)} GiB ({freePercent.ToString("0.0", CultureInfo.InvariantCulture)}%).",
            "This check flags less than 15% free space to help you plan storage. It does not delete files or measure drive health.");
    }

    private static string GiB(ulong bytes) => (bytes / 1073741824d).ToString("0.0", CultureInfo.InvariantCulture);
}

public static class Diagnostics
{
    public static IReadOnlyList<CheckResult> Evaluate(HardwareSnapshot snapshot)
    {
        var systemDrive = snapshot.Drives.FirstOrDefault(d => d.IsSystem);
        var checks = new List<CheckResult>
        {
            Checks.ForMemory(snapshot.TotalMemoryBytes, snapshot.AvailableMemoryBytes),
            Checks.ForStorage(systemDrive?.TotalBytes, systemDrive?.FreeBytes),
            new("power", "Active power plan", string.IsNullOrWhiteSpace(snapshot.PowerPlan) ? CheckStatus.Unknown : CheckStatus.Info,
                snapshot.PowerPlan ?? "The active plan could not be read.",
                "Shown for context. A suitable plan depends on your device, workload and battery needs. No plan is applied."),
            new("uptime", "Time since Windows started", snapshot.Uptime is null ? CheckStatus.Unknown : CheckStatus.Info,
                snapshot.Uptime is { } uptime ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m" : "Windows uptime could not be read.",
                "Uptime alone does not indicate a problem. Fast Startup can preserve the kernel session across shutdowns."),
            new("game-mode", "Game Mode preference", snapshot.GameModeEnabled is null ? CheckStatus.Unknown : CheckStatus.Info,
                snapshot.GameModeEnabled switch { true => "Explicit preference: on.", false => "Explicit preference: off.", _ => "No explicit preference was readable for this Windows user." },
                "Reads the saved user preference only. An absent value does not establish whether Windows Game Mode is active."),
        };
        if (snapshot.Issues.Count > 0)
            checks.Add(new("collection", "Some readings are unavailable", CheckStatus.Unknown,
                string.Join(" ", snapshot.Issues),
                "The remaining readings are still shown. Refresh to try again; administrator access is not requested."));
        return checks;
    }
}

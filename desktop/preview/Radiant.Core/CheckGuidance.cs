namespace Radiant.Core;

public enum CheckFilter { All, Attention, Unknown }

public static class CheckSelection
{
    public static IReadOnlyList<CheckResult> Filter(IEnumerable<CheckResult> checks, CheckFilter filter) =>
        checks.Where(c => filter switch
        {
            CheckFilter.Attention => c.Status == CheckStatus.Attention,
            CheckFilter.Unknown => c.Status == CheckStatus.Unknown,
            _ => true,
        }).ToArray();
}

public static class CheckGuidance
{
    public static string For(CheckResult check)
    {
        if (check.Status == CheckStatus.Unknown)
            return check.Id == "game-mode"
                ? "Check Settings > Gaming > Game Mode to see what Windows shows. A missing saved preference does not mean Game Mode is off."
                : "Refresh to try again. If this reading stays unavailable, review it in Windows directly. No change is suggested from missing data.";
        return check.Id switch
        {
            "memory" when check.Status == CheckStatus.Attention => "Open Task Manager and review Memory usage. Save your work before closing an app you no longer need, then refresh this snapshot.",
            "memory" => "No action is suggested by this reading. If a game feels slow, take another snapshot while your usual workload is running.",
            "storage" when check.Status == CheckStatus.Attention => "Review Settings > System > Storage. Back up files you want to keep before managing space. This app does not remove files.",
            "storage" => "No action is suggested by this reading. Check space again before a large game download or Windows update.",
            "power" => "Review Settings > System > Power & battery if you want to understand your device's power options. Higher power use is not automatically better for every workload.",
            "uptime" => "Use this as context when comparing snapshots. If you are troubleshooting and choose to restart Windows, save your work first.",
            "game-mode" => "Review Settings > Gaming > Game Mode if you want to change your preference. Test any change in the games you actually play.",
            _ => "Review the evidence before deciding whether anything needs attention.",
        };
    }
}

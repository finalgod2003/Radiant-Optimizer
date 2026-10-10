using Radiant.Core;

namespace Radiant.Windows;

/// <summary>Explicit developer acceptance test. Normal startup and builds never
/// invoke it. Backups are retained and the finally path restores every test change.</summary>
public static class WindowsRoundtripVerification
{
    public static async Task<object> RunAsync(string testDirectory)
    {
        var adapter = new WindowsSettingsAdapter();
        var engine = new OptimizationEngine(adapter, new FileChangeJournal(Path.Combine(testDirectory, "recovery")));
        if ((await engine.ReadHistoryAsync()).Count > 0) throw new InvalidOperationException("Use an empty verification directory; existing records must never be included in a test.");
        var plans = PowerPlans.Read();
        var alternative = plans.FirstOrDefault(p => !p.Active);
        var original = new Dictionary<string, SettingValue>();
        var actions = new List<(string Id, string? Option)> { ("client-animations", null), ("window-animations", null) };
        if (alternative is not null) actions.Add(("power-plan", alternative.Id));
        foreach (var (id, option) in actions)
            foreach (var target in (await adapter.PrepareAsync(id, option, default)).Targets)
                original[target.SettingId] = await adapter.ReadAsync(target.SettingId, default);
        var applied = new List<ChangeRecord>();
        IReadOnlyList<ChangeRecord> restored = [];
        Exception? failure = null;
        try
        {
            foreach (var (id, option) in actions)
            {
                var result = await engine.ApplyAsync(id, option);
                applied.Add(result);
                if (result.Status is not (ChangeStatus.Applied or ChangeStatus.Unchanged))
                    throw new IOException($"{id}: {result.Status}. {result.Error}");
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            var restarted = new OptimizationEngine(new WindowsSettingsAdapter(), new FileChangeJournal(Path.Combine(testDirectory, "recovery")));
            restored = await restarted.RestoreAllAsync();
            if (restored.Any(r => r.Status != ChangeStatus.Restored))
                throw new IOException("Native recovery needs attention. Preserve the original backup at " + testDirectory);
        }
        foreach (var (id, expected) in original)
            if (await adapter.ReadAsync(id, default) != expected)
                throw new IOException($"{id} does not match its original; inspect {testDirectory}.");
        if (failure is not null) throw failure;
        return new
        {
            capturedAt = DateTimeOffset.Now, originalsMatchAfterRestore = true, reloadedFromDisk = true,
            immutableBackups = Directory.EnumerateFiles(Path.Combine(testDirectory, "recovery"), "*.original").Count(),
            applied = applied.Select(r => new { r.TweakId, r.Status, r.Before, r.After }),
            restored = restored.Select(r => new { r.TweakId, r.Status }),
            powerPlanSwitchTested = alternative is not null,
            journalDirectory = Path.Combine(testDirectory, "recovery"),
        };
    }
}

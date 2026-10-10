using System.Globalization;
using System.IO;
using System.Windows;
using Radiant.Core;
using Radiant.Games;
using Radiant.Windows;

namespace Radiant.App;

public sealed partial class MainWindow
{
    private async Task RecoveryAsync(CancellationToken token)
    {
        IReadOnlyList<ChangeRecord> history = [];
        IReadOnlyList<GameChangeRecord> gameHistory = [];
        IReadOnlyList<ChangeRecord> cpuHistory = [];
        var readIssues = new List<string>();
        try { history = await optimizer.ReadHistoryAsync(token); }
        catch (Exception error) when (error is not OperationCanceledException) { readIssues.Add("Windows recovery: " + error.Message); }
        try { gameHistory = await games.ReadHistoryAsync(token); }
        catch (Exception error) when (error is not OperationCanceledException) { readIssues.Add("Game-file recovery: " + error.Message); }
        try { cpuHistory = await cpuTuning.ReadHistoryAsync(token); }
        catch (Exception error) when (error is not OperationCanceledException) { readIssues.Add("CPU recovery: " + error.Message); }
        token.ThrowIfCancellationRequested();
        Heading("A way back from every change.", "每次更改都有退路。",
            "Restore Windows settings, original game files and running-game CPU settings together. Recovery always stays Free.",
            "一起恢复 Windows 设置和原始游戏文件。恢复始终免费。");
        var windowsPending = history.Count(item => item.Status is not (ChangeStatus.Restored or ChangeStatus.Unchanged));
        var gamesPending = gameHistory.Count(item => item.Status is not (GameChangeStatus.Restored or GameChangeStatus.Unchanged));
        var cpuPending = cpuHistory.Count(item => item.Status is not (ChangeStatus.Restored or ChangeStatus.Unchanged));
        var defaultData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerPreview");
        var backupLabel = string.Equals(Path.GetFullPath(dataDirectory), Path.GetFullPath(defaultData), StringComparison.OrdinalIgnoreCase)
            ? @"%LOCALAPPDATA%\OptimizerPreview\recovery and \game-profiles" : "Custom local data folder / recovery and / game-profiles";
        var restoreAll = AsyncButton("Restore all changes", async () =>
        {
            var result = await ChangeSettingsAsync(() => RestoreEverythingAsync(token));
            await NavigateAsync("recovery");
            Notice(result, result.Contains("attention", StringComparison.Ordinal));
        }, true);
        restoreAll.IsEnabled = windowsPending + gamesPending + cpuPending > 0;
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text("WINDOWS + GAME FILES + CPU / ONE CLICK", 11, Ui.Accent, true),
            Ui.Text($"{windowsPending} Windows · {gamesPending} game files · {cpuPending} CPU changes to restore", 21, bold: true),
            Ui.Text("Close games first. Each history is restored newest first. External edits and running games are protected; other independent changes can still recover. Original backups remain after restoration.", 13, Ui.Muted),
            Ui.Row(restoreAll, Ui.Button("Open backup folder", () => System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(dataDirectory) { UseShellExecute = true }))), Ui.Text("Backups: " + backupLabel
                + " and / cpu-recovery\nKeep the backup folders. They contain recorded settings and game files, not a full Windows image.", 11, Ui.Muted))));
        foreach (var issue in readIssues) page.Children.Add(Ui.Text(issue, 13, Ui.Brush("#F0AE7C")));
        page.Children.Add(Ui.Text("WINDOWS SETTINGS", 12, Ui.Accent, true));
        if (history.Count == 0 && readIssues.Count == 0)
            page.Children.Add(Ui.Text("Original settings will appear after your first adjustment.", 13, Ui.Muted));
        foreach (var change in history.OrderByDescending(item => item.CreatedAt).Take(60))
        {
            var body = Ui.Stack(Ui.Text(RecoveryTitle(change.TweakId), 19, bold: true),
                Ui.Text(change.CreatedAt.ToLocalTime().ToString("MMM d, yyyy · HH:mm:ss zzz", CultureInfo.InvariantCulture)
                    + " · " + (change.Status == ChangeStatus.Restored ? "Restored · verified" : change.Status), 12, Ui.Muted),
                Ui.Text("Original: " + change.Before, 12), Ui.Text("Changed to: " + change.After, 12, Ui.Muted));
            if (change.Error is not null) body.Children.Add(Ui.Text(change.Error, 12, Ui.Brush("#F0AE7C")));
            if (change.Status is not (ChangeStatus.Restored or ChangeStatus.Unchanged))
                body.Children.Add(AsyncButton("Restore original", async () =>
                {
                    var result = await ChangeSettingsAsync(() => optimizer.RestoreAsync(change.Id, cancellationToken: token));
                    await NavigateAsync("recovery"); ShowChangeResult(result);
                }));
            if (change.Status == ChangeStatus.Conflict)
                body.Children.Add(AsyncButton("Review conflicting restore…", async () =>
                {
                    if (MessageBox.Show("Another value was found outside this change record.\n\n" + change.Error
                        + "\n\nSaved original: " + change.Before + "\n\nBack up the external values, then restore these originals?",
                        settings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    var result = await ChangeSettingsAsync(() => optimizer.RestoreAsync(change.Id, force: true, cancellationToken: token));
                    await NavigateAsync("recovery"); ShowChangeResult(result);
                }));
            page.Children.Add(Ui.Card(body));
        }
        try { await AddGameRecoveryAsync(token); }
        catch (Exception error) when (error is not OperationCanceledException) { page.Children.Add(Ui.Text(error.Message, 13, Ui.Brush("#F0AE7C"))); }
        AddCpuRecovery(cpuHistory, token);
    }

    private async Task<string> RestoreEverythingAsync(CancellationToken token)
    {
        var windowsRestored = 0; var gamesRestored = 0; var cpuRestored = 0; var pending = 0; var errors = new List<string>();
        // The independent journals do not share settings. Failure of one must not
        // prevent restoring the other, and no UI navigation can cancel between them.
        try
        {
            var results = await optimizer.RestoreAllAsync(token);
            windowsRestored = results.Count(r => r.Status == ChangeStatus.Restored);
            pending += results.Count - windowsRestored;
        }
        catch (Exception error) when (error is not OperationCanceledException) { errors.Add("Windows: " + error.Message); }
        try
        {
            var results = await games.RestoreAllAsync(token);
            gamesRestored = results.Count(r => r.Status == GameChangeStatus.Restored);
            pending += results.Count - gamesRestored;
        }
        catch (Exception error) when (error is not OperationCanceledException) { errors.Add("Game files: " + error.Message); }
        try
        {
            var results = await cpuTuning.RestoreAllAsync(token);
            cpuRestored = results.Count(r => r.Status == ChangeStatus.Restored && r.Error != ProcessTuningService.EndedNotice);
            pending += results.Count(r => r.Status != ChangeStatus.Restored);
        }
        catch (Exception error) when (error is not OperationCanceledException) { errors.Add("CPU: " + error.Message); }
        var summary = $"Restored and verified: {windowsRestored} Windows changes · {gamesRestored} game files · {cpuRestored} CPU changes. Exited process settings need no persistent undo.";
        return pending == 0 && errors.Count == 0 ? summary
            : summary + $" {pending} records still need attention. " + string.Join(" ", errors);
    }

    private static string RecoveryTitle(string id) => id switch
    {
        "gaming-ready" => "Gaming essentials", "gaming-power" => "Plugged-in performance",
        "client-animations" => "Reduce interface animations", "window-animations" => "Instant window transitions",
        "power-plan" => "Power plan", "game-mode" => "Game Mode", "game-recording" => "Game recording",
        "background-capture" => "Background capture", _ => id
    };
}

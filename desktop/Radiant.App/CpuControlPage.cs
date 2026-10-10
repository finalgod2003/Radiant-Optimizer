using System.Windows;
using System.Windows.Controls;
using Radiant.Core;
using Radiant.Windows;

namespace Radiant.App;

public sealed partial class MainWindow
{
    private async Task CpuControlAsync(CancellationToken token)
    {
        Heading("Give your game room to run.", "为游戏分配处理资源。",
            "Inspect a running game, choose its priority and logical processors, then review the exact change. Premium controls, free recovery.",
            "查看运行中的游戏，选择优先级与逻辑处理器，再预览更改。Premium 调节，免费恢复。");
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text("CPU CONTROL / PREMIUM", 11, Ui.Accent, true),
            Ui.Text("One game. One running instance.", 23, bold: true),
            Ui.Text("CS2 · Fortnite · VALORANT. Priority can change which process gets CPU time under contention. Core selection limits where this game can run; fewer CPUs can make performance worse. Start with Windows' current choice and measure one change at a time.", 14, Ui.Muted),
            Ui.Text("Every change is backed up and verified. Restore it while the game runs, or let the process exit: these controls do not persist to the next launch.", 13, Ui.Accent))));
        var targets = await Task.Run(gameProcesses.ListTargets, token); token.ThrowIfCancellationRequested();
        var selection = new ComboBox { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 12, 12) };
        foreach (var target in targets) selection.Items.Add(target);
        if (targets.Count > 0) selection.SelectedIndex = 0;
        var detail = Ui.Stack();
        selection.SelectionChanged += (_, _) => detail.Children.Clear();
        page.Children.Add(Ui.Row(AsyncButton("Refresh running games", () => NavigateAsync("cpu")),
            Ui.Button("Open Recovery", () => _ = NavigateAsync("recovery"))));
        if (targets.Count == 0)
            page.Children.Add(Ui.Card(Ui.Stack(Ui.Text("No accessible supported game is running.", 19, bold: true),
                Ui.Text("Start CS2, Fortnite or VALORANT, then refresh this page. Protected games may refuse access; that is reported without bypassing their protection. Only detected game processes in this Windows session are listed.", 13, Ui.Muted))));
        else
        {
            page.Children.Add(selection);
            page.Children.Add(AsyncButton("Inspect selected game", async () =>
            {
                if (selection.SelectedItem is not GameProcessTarget selected) return;
                detail.Children.Clear();
                var state = await Task.Run(() => gameProcesses.Read(selected), token); token.ThrowIfCancellationRequested();
                if (!Equals(selection.SelectedItem, selected)) return;
                detail.Children.Add(CpuEditor(selected, state, token));
            }, true));
        }
        page.Children.Add(detail);
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text("CHOOSE → BACK UP → VERIFY → RESTORE", 11, Ui.Accent, true),
            Ui.Text("A failed or partial adjustment keeps its original values in Recovery. Changing pages or losing Premium does not remove the backup. Access denied stays visible as a recovery issue; it is never treated as a successful undo.", 13, Ui.Muted))));
        var history = await cpuTuning.ReadHistoryAsync(token); token.ThrowIfCancellationRequested();
        if (history.Count > 0) AddCpuRecovery(history.Take(5).ToArray(), token);
    }

    private Border CpuEditor(GameProcessTarget target, ProcessTuningState state, CancellationToken token)
    {
        var body = Ui.Stack(Ui.Text(target.ToString(), 21, bold: true),
            Ui.Text("Current priority: " + ProcessPriority.Label(state.Priority), 14, Ui.Accent));
        var priority = new ComboBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        priority.Items.Add(new ComboBoxItem { Content = "Keep current priority" });
        priority.Items.Add(new ComboBoxItem { Content = "Normal", Tag = ProcessPriority.Normal });
        priority.Items.Add(new ComboBoxItem { Content = "Above normal", Tag = ProcessPriority.AboveNormal }); priority.SelectedIndex = 0;
        body.Children.Add(priority);
        var changeCpus = new CheckBox { Content = "Choose logical processors for this game", IsEnabled = state.CanSetAffinity,
            Foreground = Ui.Foreground, Margin = new Thickness(0, 0, 0, 12) };
        body.Children.Add(changeCpus);
        var checks = new List<(CheckBox Box, ulong Bit)>();
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6, IsEnabled = false };
        if (state.CanSetAffinity)
        {
            body.Children.Add(Ui.Text("Current logical CPUs: " + ProcessTuningState.CpuList(state.AffinityMask), 13, Ui.Muted));
            for (var i = 0; i < 64; i++)
            {
                var bit = 1UL << i; if ((state.SystemMask & bit) == 0) continue;
                var check = new CheckBox { Content = "CPU " + i, IsChecked = (state.AffinityMask & bit) != 0,
                    Foreground = Ui.Foreground, Margin = new Thickness(0, 6, 12, 6) };
                checks.Add((check, bit)); grid.Children.Add(check);
            }
            changeCpus.Click += (_, _) => grid.IsEnabled = changeCpus.IsChecked == true;
            body.Children.Add(grid);
            body.Children.Add(Ui.Text("These are Windows logical processor numbers, including SMT threads where present. They are not a ranking of core speed. No cores are disabled for other applications.", 12, Ui.Muted));
        }
        else body.Children.Add(Ui.Text(state.AffinityUnavailableReason ?? "CPU selection is unavailable.", 13, Ui.Muted));
        body.Children.Add(AsyncButton("Review, back up & apply · Premium", async () =>
        {
            if (!licenses.CachedStatus().IsPremium) { await NavigateAsync("account"); return; }
            uint? chosenPriority = priority.SelectedItem is ComboBoxItem { Tag: uint value } ? value : null;
            ulong? chosenMask = changeCpus.IsChecked == true ? checks.Where(c => c.Box.IsChecked == true).Aggregate(0UL, (mask, c) => mask | c.Bit) : null;
            if (chosenPriority is null && chosenMask is null) throw new InvalidOperationException("Choose at least one change.");
            if (chosenMask == 0) throw new InvalidOperationException("Select at least one logical processor.");
            var review = target + "\n\nPriority: " + ProcessPriority.Label(state.Priority) + " → " + ProcessPriority.Label(chosenPriority ?? state.Priority)
                + (chosenMask is ulong mask ? "\nLogical CPUs: " + ProcessTuningState.CpuList(state.AffinityMask) + " → " + ProcessTuningState.CpuList(mask) : "\nCPU selection: unchanged")
                + "\n\nSave original values, apply and verify? Results depend on the game and your PC. Restore through Recovery; this instance's settings also end when the process exits.";
            if (MessageBox.Show(review, settings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            token.ThrowIfCancellationRequested(); if (!licenses.CachedStatus().IsPremium) { await NavigateAsync("account"); return; }
            var result = await ChangeSettingsAsync(() => cpuTuning.ApplyAsync(new(target, state, chosenPriority, chosenMask), token));
            await NavigateAsync("cpu"); ShowChangeResult(result);
        }, true));
        return Ui.Card(body);
    }

    private void AddCpuRecovery(IReadOnlyList<ChangeRecord> history, CancellationToken token)
    {
        page.Children.Add(Ui.Text("CPU PROCESS BACKUPS", 12, Ui.Accent, true));
        if (history.Count == 0) page.Children.Add(Ui.Text("Priority and processor originals appear here after your first CPU adjustment.", 13, Ui.Muted));
        foreach (var change in history.OrderByDescending(c => c.CreatedAt).Take(60))
        {
            var ended = change.Error == ProcessTuningService.EndedNotice;
            var body = Ui.Stack(Ui.Text(change.TweakId, 19, bold: true),
                Ui.Text(change.CreatedAt.ToLocalTime().ToString("g") + " · " + (ended ? "Process ended · no persistent change" : change.Status), 12, Ui.Accent),
                Ui.Text("Original: " + change.Before, 13), Ui.Text("Changed to: " + change.After, 13, Ui.Muted));
            if (change.Error is not null) body.Children.Add(Ui.Text(change.Error, 12, ended ? Ui.Muted : Ui.Brush("#F0AE7C")));
            if (change.Status is not (ChangeStatus.Restored or ChangeStatus.Unchanged))
                body.Children.Add(AsyncButton("Restore original CPU settings", async () =>
                {
                    var result = await ChangeSettingsAsync(() => cpuTuning.RestoreAsync(change.Id, token: token));
                    if (result.Status == ChangeStatus.Conflict && MessageBox.Show("Another program changed these CPU settings.\n\n" + result.Error
                        + "\n\nBack up the external values, then restore the saved originals?", settings.AppName,
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                        result = await ChangeSettingsAsync(() => cpuTuning.RestoreAsync(change.Id, true, token));
                    await NavigateAsync("recovery"); ShowChangeResult(result);
                }));
            page.Children.Add(Ui.Card(body));
        }
    }
}

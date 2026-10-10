using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Radiant.Games;

namespace Radiant.App;

public sealed partial class MainWindow
{
    private async Task GamesAsync(CancellationToken token)
    {
        Heading("Tune the game. Keep the original.", "调整游戏，保留原件。",
            "CS2 · Fortnite · VALORANT. Detect, preview, then apply a supported profile while the game is closed.",
            "CS2 · Fortnite · VALORANT。检测并预览配置，关闭游戏后再应用受支持的预设。");
        var enable = new CheckBox { Content = L("Enable experimental configuration writes for this session", "在本次会话启用实验性配置写入"),
            IsChecked = experimentalEnabled, Foreground = Ui.Foreground, Margin = new Thickness(0, 0, 0, 14) };
        enable.Click += (_, _) => { experimentalEnabled = enable.IsChecked == true; };
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("Preview is free. Original files are backed up before every write.",
                "预览免费。每次写入前备份原始文件。"), 17, bold: true),
            Ui.Text(L("Apply requires Premium. Recovery remains free. Unknown formats, running games and changed files are blocked. Presets leave bindings, sensitivity and resolution alone.",
                "应用需要 Premium，恢复始终免费。未知格式、运行中的游戏或文件变化会阻止写入。预设保留按键、灵敏度和分辨率。"), 13, Ui.Muted), enable)));
        var chooseGame = new ComboBox { Width = 180, SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 12) };
        foreach (var game in Enum.GetValues<GameId>())
            chooseGame.Items.Add(new ComboBoxItem { Content = GameName(game), Tag = game });
        var results = Ui.Stack();
        page.Children.Add(Ui.Row(AsyncButton(L("Scan local profiles", "扫描本地配置"), () => NavigateAsync("games")), chooseGame,
            AsyncButton(L("Choose config file…", "选择配置文件…"), async () =>
            {
                if (chooseGame.SelectedItem is not ComboBoxItem { Tag: GameId selectedGame }) return;
                var dialog = new OpenFileDialog { Title = L("Choose an existing game configuration", "选择已有的游戏配置"),
                    Filter = "Game configuration|cs2_video.txt;GameUserSettings.ini;RiotUserSettings.ini", CheckFileExists = true, Multiselect = false };
                if (dialog.ShowDialog(this) != true) return;
                var inspected = await games.InspectAsync(selectedGame, dialog.FileName, token);
                token.ThrowIfCancellationRequested(); results.Children.Clear(); results.Children.Add(GameFileCard(inspected, token));
            })));
        page.Children.Add(results);
        var files = await games.DiscoverAsync(token);
        token.ThrowIfCancellationRequested();
        if (files.Count == 0)
            results.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("No saved game configurations found.", "尚未发现已保存的游戏配置。"), 19, bold: true),
                Ui.Text(L("Start one of the supported games and save its video settings, then close it and scan again. You can select a supported file manually. Steam discovery reads local userdata; it does not scan every drive.",
                    "先运行受支持的游戏并保存画面设置，退出后重新扫描，也可以手动选择受支持的文件。Steam 检测只读取本地 userdata，不扫描所有磁盘。"), 13, Ui.Muted))));
        foreach (var file in files.Take(40)) results.Children.Add(GameFileCard(file, token));
        page.Children.Add(Ui.Text(L("Compatibility: CS2 video.cfg v15/16; Fortnite release schema 14; VALORANT local settings v5/7. Later or unknown schemas are read-only until validated.",
            "支持格式：CS2 video.cfg v15/16；Fortnite 配置格式 14；VALORANT 本地设置 v5/7。后续或未知格式需完成验证后才开放写入。"), 12, Ui.Muted));
    }

    private static string GameName(GameId game) => game switch
    { GameId.CounterStrike2 => "Counter-Strike 2", GameId.Fortnite => "Fortnite", GameId.Valorant => "VALORANT", _ => game.ToString() };

    private Border GameFileCard(GameConfigFile file, CancellationToken token)
    {
        var body = Ui.Stack(Ui.Text(file.GameName, 21, bold: true), Ui.Text(file.Path, 12, Ui.Muted),
            Ui.Text(file.Format, 12, Ui.Accent));
        if (!file.IsSupported)
        {
            body.Children.Add(Ui.Text(L("Read-only / unsupported: ", "只读／不支持：") + file.Reason, 13, Ui.Brush("#F0AE7C")));
            return Ui.Card(body);
        }
        var previewPanel = Ui.Stack();
        var options = Ui.Row();
        foreach (var preset in GameProfiles.GetPresets(file.Game))
        {
            var id = preset.Id;
            var label = id == "competitive" ? L("Competitive preview", "竞技预设预览") : L("Balanced preview", "均衡预设预览");
            options.Children.Add(AsyncButton(label, async () =>
            {
                var preview = await games.PreviewAsync(file, id, token);
                token.ThrowIfCancellationRequested();
                previewPanel.Children.Clear();
                previewPanel.Children.Add(Ui.Text(id == "competitive" ? L("Lower visual cost; reduced detail or smoothness can be visible.", "降低画面开销，细节或画面平滑度可能下降。")
                    : L("Moderate visual detail; VSync where supported can add latency.", "保留适中画面细节；受支持的垂直同步可能增加延迟。"), 13, Ui.Muted));
                foreach (var change in preview.Changes)
                    previewPanel.Children.Add(Ui.Text($"{change.Section} / {change.Key}\n{change.Before}  →  {change.After}", 13, Ui.Accent));
                foreach (var warning in preview.Warnings) previewPanel.Children.Add(Ui.Text(warning, 12, Ui.Muted));
                previewPanel.Children.Add(AsyncButton(L("Back up & apply profile", "备份并应用预设"), async () =>
                {
                    if (!experimentalEnabled) throw new InvalidOperationException(L("Enable experimental writes before applying.", "请先启用本次会话的实验性写入。"));
                    if (!licenses.CachedStatus().IsPremium) { await NavigateAsync("account"); return; }
                    if (MessageBox.Show(L("Back up the exact original file and apply these changes? Close the game before continuing.",
                            "备份原始文件并应用这些更改？请先关闭游戏。") + "\n\n" + preview.Path + "\n\n" +
                            string.Join("\n", preview.Changes.Select(change => $"{change.Key}: {change.Before} → {change.After}")),
                            settings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    token.ThrowIfCancellationRequested();
                    if (!experimentalEnabled || !licenses.CachedStatus().IsPremium) return;
                    var result = await games.ApplyAsync(preview, token);
                    await NavigateAsync("games"); ShowGameChange(result);
                }, true));
            }));
        }
        body.Children.Add(options); body.Children.Add(previewPanel);
        return Ui.Card(body);
    }

    private void ShowGameChange(GameChangeRecord change)
    {
        var status = change.Status.ToString();
        Notice(status + (change.Error is null ? "" : ": " + change.Error),
            !new[] { "applied", "restored", "unchanged" }.Contains(status.ToLowerInvariant()));
    }

    private async Task AddGameRecoveryAsync(CancellationToken token)
    {
        var history = await games.ReadHistoryAsync(token);
        token.ThrowIfCancellationRequested();
        page.Children.Add(Ui.Text(L("GAME FILE BACKUPS", "游戏配置备份"), 13, Ui.Accent, true));
        if (history.Count == 0) page.Children.Add(Ui.Text(L("No game profiles have been applied. Exact originals will appear here after your first change.",
            "尚未应用游戏预设。首次应用后，这里会显示原始文件备份。"), 13, Ui.Muted));
        foreach (var change in history.OrderByDescending(item => item.CreatedAt).Take(60))
        {
            var body = Ui.Stack(Ui.Text(change.GameName + " · " + change.PresetId, 19, bold: true),
                Ui.Text(change.Path, 12, Ui.Muted), Ui.Text(change.CreatedAt.ToLocalTime().ToString("g") + " · " + change.Status, 12, Ui.Accent));
            if (change.Error is not null) body.Children.Add(Ui.Text(change.Error, 12, Ui.Brush("#F0AE7C")));
            if (change.Status.ToString().ToLowerInvariant() is not "restored" and not "unchanged")
                body.Children.Add(AsyncButton(L("Restore original game file", "恢复原始游戏配置"), async () =>
                {
                    if (MessageBox.Show(L("Restore the backed-up original file? The game must be closed. External changes will be checked.",
                            "恢复备份的原始文件？请先关闭游戏。会检查文件是否被其他程序更改。"), settings.AppName,
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    var result = await games.RestoreAsync(change.Id, cancellationToken: token);
                    if (result.Status.ToString().Equals("Conflict", StringComparison.OrdinalIgnoreCase) &&
                        MessageBox.Show(L("This file has changed since the profile was applied. Replacing it discards those later edits. Restore the saved original anyway?",
                            "此文件在应用预设后又发生了变化。替换将丢弃后来的编辑，仍要恢复备份原件吗？") + "\n\n" + change.Path,
                            settings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        result = await games.RestoreAsync(change.Id, force: true, cancellationToken: token);
                    await NavigateAsync("recovery"); ShowGameChange(result);
                }));
            page.Children.Add(Ui.Card(body));
        }
    }
}

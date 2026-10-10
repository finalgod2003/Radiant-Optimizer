using System.Windows;
using System.Windows.Controls;
using Radiant.Core;

namespace Radiant.App;

public sealed partial class MainWindow
{
    private async Task OptimizeAsync(CancellationToken token)
    {
        var tweaks = await optimizer.ReadTweaksAsync(token);
        token.ThrowIfCancellationRequested();
        Heading("Set up for the next game.", "为下一局做好准备。",
            "Reduce recording work, choose plugged-in performance, then tune your game. Every write starts with a backup.",
            "减少录制开销、选择插电性能策略并调整游戏。每项修改都会先备份。");
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text("GAME BOOST / FREE", 11, Ui.Accent, true),
            Ui.Text("Review an adjustment below. Original values are saved before applying; Restore all changes in Recovery returns Windows settings and supported game files. Results depend on your hardware and workload.", 13, Ui.Muted))));
        foreach (var id in new[] { "gaming-ready", "gaming-power" })
            page.Children.Add(GamingTweakCard(tweaks.Single(t => t.Id == id), token));

        page.Children.Add(Ui.Card(Ui.Stack(Ui.Row(Ui.Text("Lower the render workload.", 20, bold: true), Ui.Text("FREE", 11, Ui.Accent)),
            Ui.Text("CS2: VSync and MSAA off. Fortnite: lighter shadows and effects. VALORANT: lower material and detail quality. Choose a sustainable FPS cap, preview the exact changes, and keep the original file.", 13, Ui.Muted),
            Ui.Button("Choose a game performance preset →", () => _ = NavigateAsync("games"), true))));

        var individual = Ui.Stack();
        foreach (var tweak in tweaks.Where(t => t.Category == "Gaming" && t.Id != "gaming-ready"))
            individual.Children.Add(GamingTweakCard(tweak, token));
        page.Children.Add(new Expander { Header = "Individual Game Mode and recording controls", Foreground = Ui.Foreground,
            Content = individual, Margin = new Thickness(0, 10, 0, 12) });

        var plansPanel = Ui.Stack(Ui.Text("PREMIUM / INSTALLED POWER PLANS", 11, Ui.Muted, true),
            Ui.Text("Switch an existing Windows power plan. This is separate from the Free AC performance preference above. Higher power use, heat and fan noise are possible.", 13, Ui.Muted));
        try
        {
            var plans = await optimizer.ReadPowerPlansAsync(token);
            token.ThrowIfCancellationRequested();
            foreach (var plan in plans)
            {
                var button = AsyncButton((plan.Active ? "● " : "○ ") + plan.Name, async () =>
                {
                    if (!licenses.CachedStatus().IsPremium) { await NavigateAsync("account"); return; }
                    if (MessageBox.Show("Save the current plan and switch to " + plan.Name + "?", settings.AppName,
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    var result = await ChangeSettingsAsync(() => optimizer.ApplyAsync("power-plan", plan.Id, token));
                    await NavigateAsync("optimize"); ShowChangeResult(result);
                });
                button.IsEnabled = !plan.Active;
                plansPanel.Children.Add(button);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException) { plansPanel.Children.Add(Ui.Text(error.Message, 12, Ui.Muted)); }
        page.Children.Add(new Expander { Header = "Advanced power plans · Premium", Foreground = Ui.Foreground,
            Content = Ui.Card(plansPanel), Margin = new Thickness(0, 0, 0, 12) });

        var animations = Ui.Stack();
        foreach (var tweak in tweaks.Where(t => t.Category == "Interface")) animations.Children.Add(GamingTweakCard(tweak, token));
        page.Children.Add(new Expander { Header = "Desktop animation preferences", Foreground = Ui.Muted,
            Content = animations, Margin = new Thickness(0, 0, 0, 12) });
    }

    private Border GamingTweakCard(TweakStatus tweak, CancellationToken token)
    {
        var body = Ui.Stack(Ui.Row(Ui.Text(TweakTitle(tweak), 21, bold: true), Ui.Text("FREE", 10, Ui.Accent)),
            Ui.Text(tweak.Description, 13, Ui.Muted),
            Ui.Text("Current: " + tweak.CurrentValue, 12), Ui.Text("Proposed: " + tweak.TargetValue, 13, Ui.Accent));
        if (!tweak.Supported) body.Children.Add(Ui.Text(tweak.UnavailableReason ?? "Unavailable on this PC.", 12, Ui.Brush("#F0AE7C")));
        var apply = AsyncButton("Review, back up & apply", async () =>
        {
            var review = tweak.Title + "\n\n" + tweak.Description + "\n\nCurrent: " + tweak.CurrentValue
                + "\n\nProposed: " + tweak.TargetValue + "\n\nSave all original values, then apply this change?";
            if (MessageBox.Show(review, settings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var result = await ChangeSettingsAsync(() => optimizer.ApplyAsync(tweak.Id, cancellationToken: token));
            await NavigateAsync("optimize"); ShowChangeResult(result);
        }, true);
        apply.IsEnabled = tweak.Supported;
        body.Children.Add(apply);
        return Ui.Card(body);
    }
}

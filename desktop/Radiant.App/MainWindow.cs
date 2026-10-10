using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Radiant.Core;
using Radiant.Licensing;
using Radiant.Windows;
using Radiant.Games;

namespace Radiant.App;

public sealed partial class MainWindow : Window
{
    private readonly ProductSettings settings;
    private readonly WindowsOptimizer optimizer;
    private readonly GameProfileEngine games;
    private readonly LicenseClient licenses;
    private readonly string dataDirectory;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource pageCancellation = new();
    private readonly StackPanel page = new();
    private readonly StackPanel navigation = new();
    private readonly TextBlock title = Ui.Text("", 34, bold: true);
    private readonly TextBlock subtitle = Ui.Text("", 14, Ui.Muted);
    private readonly TextBlock notice = Ui.Text("", 13, Ui.Accent);
    private readonly TextBlock edition = Ui.Text("FREE / PREVIEW", 11, Ui.Accent, true);
    private string currentPage = "overview";
    private bool chinese;
    private bool experimentalEnabled;
    private bool changingSettings;
    private bool closeReady;
    private bool closePending;
    private readonly Grid shell;

    public MainWindow(ProductSettings settings, WindowsOptimizer optimizer, string dataDirectory)
    {
        this.settings = settings;
        this.optimizer = optimizer;
        this.dataDirectory = dataDirectory;
        games = new GameProfileEngine(Path.Combine(dataDirectory, "game-profiles"));
        licenses = new LicenseClient(settings.ServerUrl, dataDirectory, ProductSettings.TrustedKeys());
        chinese = File.Exists(Path.Combine(dataDirectory, "language.zh"));
        Title = settings.AppName;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        Width = 1220; Height = 840; MinWidth = 960; MinHeight = 660;
        Background = Ui.Background; Foreground = Ui.Foreground;
        FontFamily = new FontFamily("Segoe UI");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        shell = new Grid { Background = Ui.Background };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(228) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new DockPanel { Background = Ui.Brush("#172338"), Margin = new Thickness(0) };
        var brandMark = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/BrandMark.png")),
            Width = 54, Height = 54, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };
        RenderOptions.SetBitmapScalingMode(brandMark, BitmapScalingMode.HighQuality);
        var brand = Ui.Stack(brandMark, Ui.Text(settings.AppName, 18, bold: true), edition);
        brand.Margin = new Thickness(24, 28, 18, 36);
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var bottom = Ui.Stack(Ui.Text("0.3.0 · " + settings.Channel, 11, Ui.Muted),
            Ui.Button("EN / 中文", () => _ = ToggleLanguageAsync()));
        bottom.Margin = new Thickness(24, 20, 24, 24);
        DockPanel.SetDock(bottom, Dock.Bottom); sidebar.Children.Add(bottom);
        navigation.Margin = new Thickness(14, 0, 14, 0);
        sidebar.Children.Add(navigation);
        shell.Children.Add(sidebar);
        var main = new DockPanel { Margin = new Thickness(34, 28, 34, 24) };
        Grid.SetColumn(main, 1); shell.Children.Add(main);
        var header = Ui.Stack(Ui.Text("YOUR SETUP. YOUR CONTROL.", 10, Ui.Accent, true), title, subtitle, notice);
        header.Margin = new Thickness(0, 0, 0, 20);
        DockPanel.SetDock(header, Dock.Top); main.Children.Add(header);
        main.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) });
        Content = shell;
        Closing += async (_, eventArgs) =>
        {
            if (changingSettings)
            {
                eventArgs.Cancel = true;
                Notice("Finishing the current settings operation. Your backup is retained; close the app when the result appears.");
                return;
            }
            if (closeReady) return;
            eventArgs.Cancel = true;
            if (closePending) return;
            closePending = true; IsEnabled = false;
            lifetime.Cancel(); pageCancellation.Cancel(); StopMonitoring();
            await AwaitMonitoringShutdownAsync();
            closeReady = true; Close();
        };
        Closed += (_, _) => { lifetime.Cancel(); pageCancellation.Cancel(); StopMonitoring(); licenses.Dispose(); };
        BuildNavigation();
    }

    private string L(string en, string zh) => chinese ? zh : en;
    public async Task InitializeAsync(bool refreshLicense = true, string initialPage = "overview")
    {
        if (initialPage is not ("overview" or "optimize" or "games" or "monitoring" or "processes" or "network" or "tools" or "recovery" or "account"))
            throw new ArgumentException("Unknown preview page.");
        currentPage = initialPage;
        UpdateEdition();
        await NavigateAsync(currentPage);
        if (licenses.CacheWarning is string cacheWarning) Notice(cacheWarning, error: true);
        if (refreshLicense && ProductSettings.TrustedKeys().Length > 0)
        {
            try { await licenses.RefreshAsync(lifetime.Token); UpdateEdition(); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is not OperationCanceledException) { Notice(error.Message, error: true); }
        }
    }

    private void BuildNavigation()
    {
        navigation.Children.Clear();
        (string Id, string En, string Zh, string Number)[] items = [
            ("overview", "Overview", "电脑总览", "01"), ("optimize", "Windows settings", "Windows 设置", "02"),
            ("games", "Game profiles", "游戏配置", "03"), ("monitoring", "Live monitoring", "高级监控", "04"),
            ("processes", "Processes", "进程监控", "05"), ("network", "Connection", "网络诊断", "06"),
            ("tools", "Input lab", "输入实验室", "07"), ("recovery", "Recovery", "恢复中心", "08"),
            ("account", "Your license", "我的授权", "09")];
        foreach (var item in items)
        {
            var button = Ui.Button(item.Number + "   " + L(item.En, item.Zh), () => _ = NavigateAsync(item.Id));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 7);
            button.Padding = new Thickness(14, 10, 8, 10);
            button.BorderThickness = new Thickness(0);
            button.Background = currentPage == item.Id ? Ui.Brush("#2B3D5D") : Brushes.Transparent;
            button.Foreground = currentPage == item.Id ? Ui.Accent : Ui.Muted;
            navigation.Children.Add(button);
        }
    }

    private async Task ToggleLanguageAsync()
    {
        chinese = !chinese;
        var preferencePath = Path.Combine(dataDirectory, "language.zh");
        if (chinese) await File.WriteAllTextAsync(preferencePath, "zh");
        else if (File.Exists(preferencePath)) File.Delete(preferencePath);
        await NavigateAsync(currentPage);
    }

    private void UpdateEdition() => edition.Text = licenses.CachedStatus().IsPremium ? "PREMIUM / PREVIEW" : "FREE / PREVIEW";
    private void Notice(string message, bool error = false)
    { notice.Text = message; notice.Foreground = error ? Ui.Brush("#F0AE7C") : Ui.Accent; }

    private Button AsyncButton(string label, Func<Task> action, bool primary = false)
    {
        var button = Ui.Button(label, () => { }, primary);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { Notice(""); await action(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Notice(error.Message, error: true); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private async Task NavigateAsync(string target)
    {
        if (changingSettings) return;
        StopMonitoring();
        pageCancellation.Cancel(); pageCancellation.Dispose();
        pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = pageCancellation.Token;
        currentPage = target; BuildNavigation(); page.Children.Clear(); Notice("");
        title.Text = L("Loading your setup…", "正在读取电脑信息…"); subtitle.Text = "";
        try
        {
            switch (target)
            {
                case "overview": await OverviewAsync(token); break;
                case "optimize": await OptimizeAsync(token); break;
                case "games": await GamesAsync(token); break;
                case "monitoring": await MonitoringAsync(token); break;
                case "processes": await ProcessesAsync(token); break;
                case "network": NetworkPage(token); break;
                case "tools": ToolsPage(token); break;
                case "recovery": await RecoveryAsync(token); break;
                case "account": AccountPage(token); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Notice(error.Message, error: true); }
    }

    private void Heading(string en, string zh, string detailEn, string detailZh)
    { title.Text = L(en, zh); subtitle.Text = L(detailEn, detailZh); }

    private async Task OverviewAsync(CancellationToken token)
    {
        var system = await optimizer.ReadSystemAsync(token);
        token.ThrowIfCancellationRequested();
        Heading("A clearer view of your PC.", "清楚了解你的电脑。", "Live readings from this computer. Start here, then change one thing at a time.",
            "读取这台电脑的真实信息。从这里开始，每次验证一项更改。");
        var metrics = new Grid();
        for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
        Border[] cards = [Ui.Metric(L("CPU activity", "CPU 活动"), system.CpuUsagePercent < 0 ? L("Unavailable", "不可用") : $"{system.CpuUsagePercent:0.0}%", L("Sampled utilization", "采样利用率")),
            Ui.Metric(L("Memory in use", "已用内存"), Ui.Bytes(system.TotalMemoryBytes < 0 || system.AvailableMemoryBytes < 0 ? -1 : system.TotalMemoryBytes - system.AvailableMemoryBytes),
                L("of ", "总计 ") + Ui.Bytes(system.TotalMemoryBytes)),
            Ui.Metric(L("Logical processors", "逻辑处理器"), system.LogicalProcessors.ToString(CultureInfo.InvariantCulture),
                L("Detected by Windows", "由 Windows 检测"))];
        for (var i = 0; i < cards.Length; i++)
        { Grid.SetColumn(cards[i], i); cards[i].Margin = new Thickness(0, 0, i < 2 ? 12 : 0, 16); metrics.Children.Add(cards[i]); }
        page.Children.Add(metrics);
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("YOUR HARDWARE", "硬件信息"), 11, Ui.Muted, true),
            Ui.Text(system.CpuName, 23, bold: true), Ui.Text(system.GpuName, 16),
            Ui.Text(system.OsVersion, 13, Ui.Muted), Ui.Text(L("Open Live monitoring for GPU sensors and frame captures. Availability depends on your driver and permissions.",
                "在高级监控中查看显卡传感器和帧采集。可用指标取决于驱动与权限。"), 12, Ui.Muted))));
        foreach (var disk in system.Disks)
        {
            var used = disk.TotalBytes <= 0 || disk.FreeBytes < 0 ? 0 : (disk.TotalBytes - disk.FreeBytes) * 100d / disk.TotalBytes;
            page.Children.Add(Ui.Card(Ui.Stack(Ui.Row(Ui.Text(disk.Name, 18, bold: true),
                    Ui.Text($"{Ui.Bytes(disk.FreeBytes)} " + L("free", "可用"), 15, Ui.Accent)),
                new ProgressBar { Value = used, Height = 5, Maximum = 100, Foreground = Ui.Accent, Background = Ui.BorderColor },
                Ui.Text(L("Capacity: ", "容量：") + Ui.Bytes(disk.TotalBytes), 12, Ui.Muted))));
        }
        page.Children.Add(Ui.Row(AsyncButton(L("Refresh readings", "刷新读数"), () => NavigateAsync("overview")),
            Ui.Button(L("Review Windows settings →", "查看 Windows 设置 →"), () => _ = NavigateAsync("optimize"), true)));
    }

    private string TweakTitle(TweakStatus tweak) => tweak.Id switch
    {
        "game-mode" => L("Game Mode", "游戏模式"), "game-recording" => L("Game recording", "游戏录制"),
        "background-capture" => L("Background capture", "后台录制"), "power-plan" => L("Power plan", "电源计划"),
        "client-animations" => "Reduce interface animations", "window-animations" => "Instant window transitions",
        _ => tweak.Title
    };

    private async Task OptimizeAsync(CancellationToken token)
    {
        var tweaks = await optimizer.ReadTweaksAsync(token);
        var plans = await optimizer.ReadPowerPlansAsync(token);
        token.ThrowIfCancellationRequested();
        Heading("Choose your next change.", "选择下一项更改。", "Review current values and save the original before applying a supported setting.",
            "查看当前值，先保存原值，再应用支持的设置。");
        var enableChanges = new CheckBox { Content = L("Enable experimental changes for this session", "为本次会话启用实验性设置更改"),
            IsChecked = experimentalEnabled, Foreground = Ui.Foreground, Margin = new Thickness(0, 8, 0, 0) };
        enableChanges.Checked += (_, _) => { experimentalEnabled = true; _ = NavigateAsync("optimize"); };
        enableChanges.Unchecked += (_, _) => { experimentalEnabled = false; _ = NavigateAsync("optimize"); };
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text("BACK UP → APPLY → VERIFY → RESTORE", 11, Ui.Accent, true),
            Ui.Text("Animation adjustments use Windows interfaces. Every adjustment saves its original before writing. One-click recovery is available on the Recovery page. Game preferences and power-plan changes remain opt-in for this session.", 13, Ui.Muted), enableChanges)));
        foreach (var tweak in tweaks.Where(item => item.Id != "power-plan"))
        {
            var text = L(tweak.Description, tweak.Id switch
            {
                "game-mode" => "控制 Windows 游戏模式。它的效果依赖当前系统和游戏。",
                "game-recording" => "控制游戏录制功能。关闭后会影响使用此功能的录制工作流。",
                "background-capture" => "控制后台游戏录像。关闭后无法保留之前时刻的自动录制。",
                _ => tweak.Description
            });
            var body = Ui.Stack(Ui.Row(Ui.Text(TweakTitle(tweak), 19, bold: true), Ui.Text("FREE", 10, Ui.Accent)),
                Ui.Text(text, 13, Ui.Muted), Ui.Text(L("Current: ", "当前：") + tweak.CurrentValue, 13),
                Ui.Text(L("Proposed: ", "拟设置：") + tweak.TargetValue, 13, Ui.Accent));
            if (tweak.RestartRequired) body.Children.Add(Ui.Text(L("Sign out or restart may be required.", "可能需要注销或重启。"), 12, Ui.Muted));
            if (!tweak.Supported) body.Children.Add(Ui.Text(tweak.UnavailableReason ?? L("Not supported on this PC", "此电脑不支持"), 12, Ui.Muted));
            var apply = AsyncButton(L("Review & apply", "确认并应用"), async () =>
            {
                if (MessageBox.Show(L("Save the original value and apply this change?", "保存原值并应用这项更改？")
                        + "\n\n" + TweakTitle(tweak) + "\n" + tweak.CurrentValue + " → " + tweak.TargetValue,
                        settings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var result = await ChangeSettingsAsync(() => optimizer.ApplyAsync(tweak.Id, cancellationToken: token));
                await NavigateAsync("optimize"); ShowChangeResult(result);
            }, true);
            apply.IsEnabled = tweak.Supported && (tweak.Category == "Interface" || experimentalEnabled);
            body.Children.Add(apply); page.Children.Add(Ui.Card(body));
        }
        var power = Ui.Stack(Ui.Row(Ui.Text(L("Power plans", "电源计划"), 20, bold: true), Ui.Text("PREMIUM", 10, Ui.Accent)),
            Ui.Text(L("Choose an existing Windows plan. Power use, fan noise and battery life can change.",
                "切换已有的 Windows 电源计划。耗电、风扇噪音和续航可能随之改变。"), 13, Ui.Muted));
        foreach (var plan in plans)
        {
            var button = AsyncButton((plan.Active ? "● " : "○ ") + plan.Name, async () =>
            {
                if (!licenses.CachedStatus().IsPremium) { await NavigateAsync("account"); return; }
                if (MessageBox.Show(L("Switch the active power plan to ", "将当前电源计划切换为 ") + plan.Name + "?",
                    settings.AppName, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                var result = await ChangeSettingsAsync(() => optimizer.ApplyAsync("power-plan", plan.Id, token));
                await NavigateAsync("optimize"); ShowChangeResult(result);
            });
            button.Margin = new Thickness(0, 6, 0, 0);
            button.IsEnabled = experimentalEnabled && !plan.Active;
            power.Children.Add(button);
        }
        if (!licenses.CachedStatus().IsPremium) power.Children.Add(Ui.Text(L("Activate Premium to switch plans. Existing backups can always be restored.",
            "激活 Premium 后可切换计划。恢复已有备份始终免费。"), 12, Ui.Muted));
        page.Children.Add(Ui.Card(power));
    }

    private void ShowChangeResult(ChangeRecord result)
    {
        var success = result.Status is "applied" or "restored" or "unchanged";
        Notice(result.Status + (result.Error is null ? "" : ": " + result.Error), error: !success);
    }

    private async Task ProcessesAsync(CancellationToken token)
    {
        var processes = await optimizer.ReadProcessesAsync(token);
        token.ThrowIfCancellationRequested();
        Heading("Find the busy processes.", "找出繁忙的进程。", "CPU is sampled; memory is the process working set. This view makes no changes.",
            "CPU 为采样值；内存显示进程工作集。此页面仅用于查看。");
        page.Children.Add(AsyncButton(L("Refresh processes", "刷新进程"), () => NavigateAsync("processes")));
        page.Children.Add(Ui.Text(L("PROCESS                         CPU             MEMORY", "进程                             CPU             内存"), 11, Ui.Muted));
        foreach (var process in processes.OrderByDescending(item => item.CpuPercent).ThenByDescending(item => item.MemoryBytes).Take(60))
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.Children.Add(Ui.Text(process.Name + " · " + process.Id, 14));
            var cpu = Ui.Text(process.CpuPercent < 0 ? "—" : $"{process.CpuPercent:0.0}%", 14, Ui.Accent); Grid.SetColumn(cpu, 1); row.Children.Add(cpu);
            var memory = Ui.Text(Ui.Bytes(process.MemoryBytes), 14, Ui.Muted); Grid.SetColumn(memory, 2); row.Children.Add(memory);
            var card = Ui.Card(row, 12); card.Margin = new Thickness(0, 0, 0, 5); page.Children.Add(card);
        }
    }

    private async Task RecoveryAsync(CancellationToken token)
    {
        var history = await optimizer.ReadHistoryAsync(token);
        token.ThrowIfCancellationRequested();
        Heading("Every change has a record.", "每次更改都有记录。", "Restore saved original values. This remains available without Premium.",
            "恢复已保存的原始值。此能力始终免费。");
        var outstanding = history.Count(item => item.Status is not ("restored" or "unchanged"));
        var defaultData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerPreview");
        var backupLabel = string.Equals(Path.GetFullPath(dataDirectory), Path.GetFullPath(defaultData), StringComparison.OrdinalIgnoreCase)
            ? @"%LOCALAPPDATA%\OptimizerPreview\recovery" : "Custom local data folder / recovery";
        var restoreAll = AsyncButton("Restore all Windows changes", async () =>
        {
            var results = await ChangeSettingsAsync(() => optimizer.RestoreAllAsync(token));
            await NavigateAsync("recovery");
            var remaining = results.Count(r => r.Status != ChangeStatus.Restored);
            Notice(remaining == 0 ? "All recorded Windows changes restored and verified."
                : $"{results.Count - remaining} restored; {remaining} still need attention. Review the records below. External changes were not overwritten.", remaining > 0);
        }, true);
        restoreAll.IsEnabled = outstanding > 0;
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text("A way back, in one click.", 22, bold: true),
            Ui.Text($"{outstanding} Windows changes still need restoration. Saved originals are retained after recovery. Restore runs newest first; game-file backups remain separate below.", 13, Ui.Muted),
            restoreAll, Ui.Text("Backups: " + backupLabel + "\nKeep this folder to retain recovery. These are setting backups, not a full Windows image.", 11, Ui.Muted))));
        if (history.Count == 0) page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("Your history starts with your first change.", "应用第一项设置后，这里就会出现记录。"), 20),
            Ui.Text(L("Original values are saved before supported settings are written.", "支持的设置会在写入前保存原始值。"), 13, Ui.Muted))));
        foreach (var change in history.OrderByDescending(item => item.CreatedAt).Take(60))
        {
            var body = Ui.Stack(Ui.Text(change.TweakId switch { "client-animations" => "Reduce interface animations", "window-animations" => "Instant window transitions", "power-plan" => "Power plan", "game-mode" => "Game Mode", "game-recording" => "Game recording", "background-capture" => "Background capture", _ => change.TweakId }, 19, bold: true),
                Ui.Text(change.CreatedAt.ToLocalTime().ToString("MMM d, yyyy · HH:mm:ss zzz", CultureInfo.InvariantCulture) + " · " + (change.Status == "restored" ? "Restored · verified" : change.Status), 12, Ui.Muted),
                Ui.Text(L("Original: ", "原始值：") + change.Before, 12), Ui.Text(L("Changed to: ", "修改为：") + change.After, 12, Ui.Muted));
            if (change.Error is not null) body.Children.Add(Ui.Text(change.Error, 12, Ui.Brush("#F0AE7C")));
            if (change.Status is not "restored" and not "unchanged")
                body.Children.Add(AsyncButton(L("Restore original", "恢复原值"), async () =>
                {
                    if (MessageBox.Show(L("Restore the saved original value? External changes will be checked first.",
                        "恢复保存的原值？操作前会检查是否有其他程序更改过设置。"), settings.AppName, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    var result = await ChangeSettingsAsync(() => optimizer.RestoreAsync(change.Id, cancellationToken: token));
                    await NavigateAsync("recovery"); ShowChangeResult(result);
                }));
            if (change.Status == ChangeStatus.Conflict)
                body.Children.Add(AsyncButton("Review conflicting restore…", async () =>
                {
                    if (MessageBox.Show("Windows has a value outside this change record.\n\n" + change.Error + "\n\nSaved original: " + change.Before
                        + "\n\nBack up the current external values, then restore these originals?", settings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    var result = await ChangeSettingsAsync(() => optimizer.RestoreAsync(change.Id, force: true, cancellationToken: token));
                    await NavigateAsync("recovery"); ShowChangeResult(result);
                }));
            page.Children.Add(Ui.Card(body));
        }
        await AddGameRecoveryAsync(token);
    }

    private void OpenTrustedBrowser(Uri destination)
    {
        if (destination.GetLeftPart(UriPartial.Authority) != licenses.ServerUri.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("Only this product's configured website can be opened.");
        Process.Start(new ProcessStartInfo(destination.AbsoluteUri) { UseShellExecute = true });
    }

    private void AccountPage(CancellationToken token)
    {
        if (ProductSettings.TrustedKeys().Length == 0)
        {
            Heading("Free local preview", "免费本地预览", "System checks, interface adjustments and recovery are ready to use.", "可使用系统检测、界面调整与恢复。");
            page.Children.Add(Ui.Card(Ui.Stack(Ui.Text("Activation is not configured in this build.", 22, bold: true),
                Ui.Text("This package has no license verification keys. Premium activation becomes available in a release configured for your product's licensing service. Existing local backups can always be restored.", 14, Ui.Muted))));
            return;
        }
        var status = licenses.CachedStatus();
        Heading("One PC. Your toolkit.", "一台电脑，一套工具。", "Use your website account to approve this device. Your browser password stays in the browser.",
            "在网站账号中确认此设备。网页登录凭据不会传入客户端。");
        var activation = Ui.Stack();
        var stateText = Ui.Text(status.IsPremium ? L("Premium is active", "Premium 已激活") : L("You are using Free", "当前使用免费版"), 24, Ui.Accent, true);
        var detail = Ui.Text(status.Message, 13, Ui.Muted);
        if (status.ExpiresAt is not null) detail.Text += "\n" + L("Offline access until ", "离线授权截至 ") + status.ExpiresAt.Value.ToLocalTime().ToString("g");
        var activate = AsyncButton(L("Activate this PC", "激活这台电脑"), async () =>
        {
            activation.Children.Clear();
            var request = await licenses.StartAsync(token);
            activation.Children.Add(Ui.Text(L("ENTER THIS CODE ON THE WEBSITE", "在网站核对以下验证码"), 11, Ui.Muted));
            activation.Children.Add(Ui.Text(request.UserCode, 36, Ui.Accent, true));
            activation.Children.Add(Ui.Text(L("Waiting for browser approval. This request expires in 5 minutes.", "正在等待浏览器确认。请求在 5 分钟后过期。"), 13, Ui.Muted));
            var destination = licenses.VerificationUri(request);
            activation.Children.Add(Ui.Button(L("Open activation page ↗", "打开激活页面 ↗"), () => OpenTrustedBrowser(destination)));
            OpenTrustedBrowser(destination);
            while (DateTimeOffset.UtcNow < request.ExpiresAt)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(request.IntervalSeconds, 3, 10)), token);
                var result = await licenses.PollAsync(request.DeviceCode, token);
                if (result is null) continue;
                UpdateEdition(); await NavigateAsync("account"); Notice(L("This PC is now activated.", "这台电脑已激活。")); return;
            }
            throw new LicenseApiException(L("Activation expired. Please start again.", "激活请求已过期，请重新开始。"));
        }, true);
        page.Children.Add(Ui.Card(Ui.Stack(stateText, detail, Ui.Row(activate,
            AsyncButton(L("Refresh license", "刷新授权"), async () => { await licenses.RefreshAsync(token); UpdateEdition(); await NavigateAsync("account"); })), activation)));
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("Manage your device online", "在线管理设备"), 19, bold: true),
            Ui.Text(L("Unbinding on the website prevents this PC from refreshing its license. Offline access expires within seven days.",
                "在网站解绑后，此电脑无法刷新授权。离线授权最长在七天内到期。"), 13, Ui.Muted),
            Ui.Button(L("Account & licenses ↗", "账号与授权 ↗"), () => OpenTrustedBrowser(new Uri(licenses.ServerUri, "/settings/licenses"))))));
        page.Children.Add(Ui.Text(L("Connected to: ", "连接至：") + settings.ServerUrl, 12, Ui.Muted));
    }
}

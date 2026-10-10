using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Radiant.App;

public sealed partial class MainWindow
{
    private void NetworkPage(CancellationToken token)
    {
        Heading("Look beyond download speed.", "从延迟开始了解网络。", "Measure ICMP responses and DNS lookup time. These are not in-game latency measurements.",
            "测量 ICMP 响应和 DNS 查询耗时。这些读数不是游戏内延迟。");
        var host = new TextBox { Text = "1.1.1.1", MaxLength = 253, Margin = new Thickness(0, 8, 0, 14) };
        var domain = new TextBox { Text = new Uri(settings.ServerUrl).Host, MaxLength = 253, Margin = new Thickness(0, 8, 0, 14) };
        var results = Ui.Stack();
        var run = AsyncButton(L("Run connection check", "运行连接测试"), async () =>
        {
            results.Children.Clear();
            var destination = host.Text.Trim();
            var dnsTarget = domain.Text.Trim();
            if (Uri.CheckHostName(destination) == UriHostNameType.Unknown || Uri.CheckHostName(dnsTarget) == UriHostNameType.Unknown)
                throw new ArgumentException(L("Enter only a hostname or IP address, without a scheme, port or path.", "请输入域名或 IP 地址，不要包含协议、端口或路径。"));
            results.Children.Add(Ui.Text(L("Testing up to eight ICMP responses…", "正在测试，最多发送八次 ICMP 请求…"), 14, Ui.Accent));
            var times = new List<long>();
            var failures = 0;
            using var ping = new Ping();
            for (var i = 0; i < 8; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var reply = await ping.SendPingAsync(destination, 1000).WaitAsync(token);
                    if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime); else failures++;
                }
                catch (PingException) { failures++; }
                if (i < 7) await Task.Delay(150, token);
            }
            results.Children.Clear();
            results.Children.Add(Ui.Text(times.Count == 0 ? L("No ICMP response. The destination or network may filter ICMP.", "没有收到 ICMP 响应。目标或网络可能过滤 ICMP。")
                : L("Average round trip: ", "平均往返时间：") + $"{times.Average():0.0} ms", 22, Ui.Accent, true));
            results.Children.Add(Ui.Text(L("Missed responses: ", "未收到响应：") + $"{failures}/8", 13, Ui.Muted));
            if (times.Count > 1) results.Children.Add(Ui.Text(L("Sample range: ", "采样范围：") + $"{times.Min()}–{times.Max()} ms", 13, Ui.Muted));
            var timer = Stopwatch.StartNew();
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(dnsTarget, token).WaitAsync(TimeSpan.FromSeconds(5), token);
                timer.Stop();
                results.Children.Add(Ui.Text(L("DNS resolution: ", "DNS 解析：") + $"{timer.Elapsed.TotalMilliseconds:0.0} ms · {addresses.Length} "
                    + L("addresses", "个地址"), 15));
                results.Children.Add(Ui.Text(L("Uses the Windows resolver and may use its cache. This does not compare public DNS providers.",
                    "使用 Windows 解析器，可能命中缓存。此测试不比较各公共 DNS 服务商。"), 12, Ui.Muted));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { results.Children.Add(Ui.Text(L("DNS lookup did not complete: ", "DNS 查询未完成：") + error.Message, 13, Ui.Muted)); }
        }, true);
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("ICMP destination", "ICMP 目标"), 13, bold: true), host,
            Ui.Text(L("Domain for DNS lookup", "DNS 查询域名"), 13, bold: true), domain, run, results)));
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("What's next", "后续能力"), 18, bold: true),
            Ui.Text(L("Bandwidth, loaded latency and route tracing will be released after the dedicated test infrastructure is validated.",
                "带宽、负载延迟和路由追踪将在专用测试基础设施验证后发布。"), 13, Ui.Muted))));
    }

    private void ToolsPage(CancellationToken token)
    {
        Heading("Get a feel for your inputs.", "了解你的输入设备。", "Local, interactive checks. Results include display and application timing.",
            "本地交互测试。计时结果包含显示和应用处理时间。");
        MouseTool(); KeyboardTool(); ReactionTool(token); AimTool(); DisplayTool(); ControllerTool();
    }

    private void MouseTool()
    {
        var clicks = 0;
        var timer = new Stopwatch();
        var reading = Ui.Text(L("No clicks yet", "尚未点击"), 16, Ui.Accent);
        var target = Ui.Button(L("Click here", "点击这里"), () =>
        {
            if (!timer.IsRunning) timer.Start();
            clicks++;
            reading.Text = L("Clicks: ", "点击数：") + clicks + " · " +
                (timer.Elapsed.TotalSeconds >= 1 ? $"{clicks / timer.Elapsed.TotalSeconds:0.00} " + L("clicks/s", "次/秒") : L("measuring…", "计时中…"));
        }, true);
        target.MinWidth = 150;
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("01 / Mouse clicks", "01 / 鼠标点击"), 18, bold: true), reading,
            Ui.Row(target, Ui.Button(L("Reset", "重置"), () => { clicks = 0; timer.Reset(); reading.Text = "0"; })))));
    }

    private void KeyboardTool()
    {
        var input = new TextBox { Text = "", Margin = new Thickness(0, 8, 0, 10), MaxLength = 120 };
        var reading = Ui.Text(L("Click the field, then press a key.", "点击输入框，再按任意键。"), 13, Ui.Muted);
        input.PreviewKeyDown += (_, e) => { reading.Text = L("Detected: ", "检测到：") + e.Key + " · " + Keyboard.Modifiers; };
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("02 / Keyboard", "02 / 键盘"), 18, bold: true),
            Ui.Text(L("Only this focused field is observed. No keystrokes are saved.", "只检测此输入框获得焦点时的按键，不保存按键记录。"), 12, Ui.Muted), input, reading)));
    }

    private void ReactionTool(CancellationToken token)
    {
        var armed = false; var waiting = false; var round = 0;
        var timer = new Stopwatch();
        var reading = Ui.Text(L("Start, wait for green, then click the target.", "点击开始，等到绿色出现后再点击目标。"), 13, Ui.Muted);
        var target = Ui.Button(L("Wait for green", "等待绿色"), () => { });
        target.MinWidth = 200;
        target.Click += (_, _) =>
        {
            if (!waiting) return;
            if (!armed) { round++; waiting = false; reading.Text = L("Too early. Start a new round.", "太早了，请重新开始。" ); return; }
            timer.Stop(); armed = false; waiting = false;
            reading.Text = $"{timer.Elapsed.TotalMilliseconds:0} ms · " + L("application response time", "应用响应时间");
            target.Background = Ui.Surface;
        };
        var start = AsyncButton(L("Start round", "开始一轮"), async () =>
        {
            var thisRound = ++round; armed = false; waiting = true; target.Background = Ui.Brush("#613C30");
            reading.Text = L("Wait…", "等待…");
            await Task.Delay(RandomNumberGenerator.GetInt32(1500, 4000), token);
            if (thisRound != round) return;
            target.Background = Ui.Accent; target.Foreground = Ui.Background; target.Content = L("CLICK", "点击");
            armed = true; timer.Restart();
        });
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("03 / Reaction", "03 / 反应速度"), 18, bold: true), reading, Ui.Row(start, target))));
    }

    private void AimTool()
    {
        var hits = 0;
        var canvas = new Canvas { Width = 460, Height = 170, Background = Ui.Background, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        var dot = new Ellipse { Width = 28, Height = 28, Fill = Ui.Accent, Cursor = Cursors.Cross };
        var reading = Ui.Text(L("Targets hit: 0", "击中目标：0"), 13, Ui.Muted);
        void MoveTarget() { Canvas.SetLeft(dot, RandomNumberGenerator.GetInt32(10, 420)); Canvas.SetTop(dot, RandomNumberGenerator.GetInt32(10, 132)); }
        dot.MouseLeftButtonDown += (_, e) => { hits++; reading.Text = L("Targets hit: ", "击中目标：") + hits; MoveTarget(); e.Handled = true; };
        MoveTarget(); canvas.Children.Add(dot);
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("04 / Aim practice", "04 / 瞄准练习"), 18, bold: true), reading, canvas)));
    }

    private void DisplayTool()
    {
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("05 / Display colors", "05 / 屏幕颜色"), 18, bold: true),
            Ui.Text(L("Inspect solid colors visually. Click to cycle; Esc exits. This is not an automated defect diagnosis.",
                "用纯色画面目视检查。点击切换颜色，Esc 退出。此工具不自动判定屏幕缺陷。"), 13, Ui.Muted),
            Ui.Button(L("Open full-screen colors", "打开全屏颜色测试"), () =>
            {
                Brush[] colors = [Brushes.Black, Brushes.White, Brushes.Red, Brushes.Green, Brushes.Blue]; var index = 0;
                var window = new Window { Owner = this, WindowStyle = WindowStyle.None, WindowState = WindowState.Maximized,
                    Background = colors[0], Topmost = true, Cursor = Cursors.Hand };
                window.Content = new Border { Background = Ui.Brush("#AA111111"), Padding = new Thickness(20),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                    Child = Ui.Text(L("Click: next color · Esc: exit", "点击：下一种颜色 · Esc：退出"), 14, Brushes.White) };
                window.MouseLeftButtonDown += (_, _) => window.Background = colors[++index % colors.Length];
                window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
                window.Show();
            }))));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LeftX, LeftY, RightX, RightY; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ControllerState { public uint Packet; public Gamepad Pad; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint index, out ControllerState state);

    private void ControllerTool()
    {
        var reading = Ui.Text(L("Connect an XInput controller and refresh while holding a control.", "连接 XInput 手柄，按住按键或移动摇杆时点击刷新。"), 13, Ui.Muted);
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("06 / Controller", "06 / 手柄"), 18, bold: true), reading,
            Ui.Button(L("Read controller", "读取手柄"), () =>
            {
                try
                {
                    for (uint i = 0; i < 4; i++)
                    {
                        if (XInputGetState(i, out var state) != 0) continue;
                        var pad = state.Pad;
                        reading.Text = L("Controller ", "手柄 ") + (i + 1) + $"\nButtons 0x{pad.Buttons:X4} · LT {pad.LeftTrigger} · RT {pad.RightTrigger}"
                            + $"\nLeft ({pad.LeftX}, {pad.LeftY}) · Right ({pad.RightX}, {pad.RightY})";
                        return;
                    }
                    reading.Text = L("No XInput controller detected.", "未检测到 XInput 手柄。");
                }
                catch (DllNotFoundException) { reading.Text = L("XInput is unavailable on this system.", "此系统无法使用 XInput。"); }
            }))));
    }
}

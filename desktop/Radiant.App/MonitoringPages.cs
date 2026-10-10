using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Radiant.Monitoring;

namespace Radiant.App;

public sealed partial class MainWindow
{
    private CancellationTokenSource? monitorCancellation;
    private CancellationTokenSource? captureCancellation;
    private MonitorOverlay? monitorOverlay;
    private readonly BoundedHistory<MonitorSample> sensorHistory = new(600);
    private CaptureResult? lastCapture;
    private FrameSummary? liveFrames;
    private MonitorSample? liveSensors;
    private readonly HashSet<Task> monitoringWork = new();

    private void TrackMonitoring(Task task)
    {
        monitoringWork.Add(task);
        _ = ObserveAsync();
        async Task ObserveAsync()
        {
            try { await task; }
            catch (Exception) { /* The page handler reports its own result; this observes shutdown tasks. */ }
            finally { monitoringWork.Remove(task); }
        }
    }

    private async Task AwaitMonitoringShutdownAsync()
    {
        try { await Task.WhenAll(monitoringWork.ToArray()); }
        catch (Exception) { /* CaptureAsync has already run its owned-process/ETW cleanup in finally. */ }
    }

    private void StopMonitoring()
    {
        monitorCancellation?.Cancel(); monitorCancellation = null;
        captureCancellation?.Cancel(); captureCancellation = null;
        monitorOverlay?.Close(); monitorOverlay = null;
        liveFrames = null; liveSensors = null;
    }

    private async Task MonitoringAsync(CancellationToken pageToken)
    {
        Heading("Measure the session.", "记录真实表现。",
            "Live sensors and PresentMon presentation timing. Missing readings stay missing; no inferred FPS or CPU temperature.",
            "实时传感器与 PresentMon 呈现帧时间。缺失读数明确显示，不推算 FPS 或 CPU 温度。");
        var sensors = new SystemMonitor(optimizer);
        var session = CancellationTokenSource.CreateLinkedTokenSource(pageToken);
        monitorCancellation = session;
        var token = session.Token;
        var values = Enumerable.Range(0, 4).Select(_ => Ui.Text("—", 28, Ui.Accent, true)).ToArray();
        var metrics = new Grid();
        for (var i = 0; i < values.Length; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
        var names = new[] { L("CPU", "CPU"), L("RAM used", "已用内存"), L("GPU activity", "GPU 活动"), L("GPU temperature", "显卡温度") };
        for (var i = 0; i < values.Length; i++)
        {
            var card = Ui.Card(Ui.Stack(Ui.Text(names[i], 11, Ui.Muted, true), values[i]), 16);
            card.Margin = new Thickness(0, 0, i < 3 ? 10 : 0, 12); Grid.SetColumn(card, i); metrics.Children.Add(card);
        }
        page.Children.Add(metrics);
        var adapter = new ComboBox { Width = 340, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 12, 8) };
        var sensorDetails = Ui.Text("", 12, Ui.Muted);
        var gpuMemory = Ui.Text("—", 13, Ui.Accent);
        var chart = new Canvas { Height = 90, Background = Ui.Background, ClipToBounds = true };
        var status = Ui.Text(L("Reading sensors…", "正在读取传感器…"), 12, Ui.Muted);
        bool paused = false;
        bool painting = false;
        var pause = Ui.Button(L("Pause readings", "暂停读数"), () => { });
        pause.Click += (_, _) =>
        {
            paused = !paused;
            pause.Content = paused ? L("Resume readings", "恢复读数") : L("Pause readings", "暂停读数");
            if (paused) { liveSensors = null; values.ToList().ForEach(value => value.Opacity = 0.45); status.Text = L("Readings paused; displayed values are historical.", "采样已暂停，当前显示为历史值。"); monitorOverlay?.Update(null, liveFrames, null); }
            else values.ToList().ForEach(value => value.Opacity = 1);
        };
        string? SelectedAdapter() => (adapter.SelectedItem as ComboBoxItem)?.Tag as string;
        void PaintSensors(MonitorSample sample)
        {
            if (token.IsCancellationRequested || painting) return;
            painting = true;
            try
            {
            var old = SelectedAdapter();
            var ids = adapter.Items.Cast<ComboBoxItem>().Select(item => (string)item.Tag).ToArray();
            if (!ids.SequenceEqual(sample.Gpus.Select(gpu => gpu.Id)))
            {
                adapter.Items.Clear();
                foreach (var gpu in sample.Gpus) adapter.Items.Add(new ComboBoxItem { Content = gpu.Name + " · " + gpu.Source, Tag = gpu.Id });
                var selected = adapter.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == old);
                var preferred = sample.Gpus.FirstOrDefault(gpu => gpu.TemperatureCelsius.Value is not null)
                    ?? sample.Gpus.OrderByDescending(gpu => gpu.DedicatedTotalBytes.Value ?? 0).FirstOrDefault();
                adapter.SelectedItem = selected ?? adapter.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == preferred?.Id)
                    ?? adapter.Items.Cast<ComboBoxItem>().FirstOrDefault();
            }
            var selectedGpu = sample.Gpus.FirstOrDefault(gpu => gpu.Id == SelectedAdapter());
            values[0].Text = MetricText(sample.CpuPercent, "%");
            values[1].Text = sample.MemoryUsedBytes.Value is double bytes ? Ui.Bytes((long)bytes) : "—";
            values[2].Text = selectedGpu is null ? "—" : MetricText(selectedGpu.UsagePercent, "%");
            values[3].Text = selectedGpu is null ? "—" : MetricText(selectedGpu.TemperatureCelsius, " °C");
            sensorDetails.Text = selectedGpu is null ? L("No supported GPU counters were returned.", "没有获取到受支持的显卡计数器。")
                : string.Join(" · ", new[] { selectedGpu.UsagePercent.UnavailableReason, selectedGpu.TemperatureCelsius.UnavailableReason,
                    selectedGpu.DedicatedUsedBytes.UnavailableReason }.Where(reason => reason is not null).Distinct());
            gpuMemory.Text = L("Dedicated GPU memory: ", "专用显存：") + MemoryText(selectedGpu?.DedicatedUsedBytes)
                + " / " + MemoryText(selectedGpu?.DedicatedTotalBytes) + L("    Shared: ", "    共享：") + MemoryText(selectedGpu?.SharedUsedBytes);
            status.Text = L("Latest sample ", "最近采样 ") + sample.At.ToLocalTime().ToString("HH:mm:ss")
                + L(" · CPU trend, last 60 samples · history capped at 600 samples", " · CPU 最近 60 点趋势 · 最多保留 600 点");
            RenderTrend(chart, sensorHistory.Snapshot().TakeLast(60).Select(item => item.CpuPercent.Value).ToArray());
            monitorOverlay?.Update(sample, liveFrames, SelectedAdapter());
            }
            finally { painting = false; }
        }
        adapter.SelectionChanged += (_, _) => { if (liveSensors is not null && !paused) PaintSensors(liveSensors); };
        chart.SizeChanged += (_, _) => RenderTrend(chart, sensorHistory.Snapshot().TakeLast(60).Select(item => item.CpuPercent.Value).ToArray());
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Row(adapter, pause), gpuMemory, sensorDetails, chart, status,
            AsyncButton(L("Export sensor history…", "导出传感器记录…"), async () =>
            {
                var samples = sensorHistory.Snapshot();
                if (samples.Length == 0) throw new InvalidOperationException(L("No readings yet.", "尚无采样数据。"));
                var path = ChooseCsv("optimizer-sensors"); if (path is null) return;
                await MonitoringCsv.WriteSensorsAsync(path, samples); Notice(L("Saved sensor history: ", "已保存传感器记录：") + path);
            }))));
        page.Children.Add(Ui.Text(L("GPU load is the busiest measured engine, not a sum. NVML sensors are available only on supported NVIDIA drivers. CPU temperature is unavailable.",
            "GPU 活动取最繁忙的已测引擎，不累加各引擎。NVML 传感器仅在受支持的 NVIDIA 驱动上可用。CPU 温度不提供。"), 12, Ui.Muted));
        AddFrameCapture(token, SelectedAdapter);
        try
        {
            var sample = await sensors.SampleAsync(token); token.ThrowIfCancellationRequested(); liveSensors = sample;
            sensorHistory.Add(liveSensors); PaintSensors(liveSensors);
        }
        catch { sensors.Dispose(); session.Dispose(); if (ReferenceEquals(monitorCancellation, session)) monitorCancellation = null; throw; }
        TrackMonitoring(RunSensorsAsync());
        async Task RunSensorsAsync()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                while (!token.IsCancellationRequested && watch.Elapsed < TimeSpan.FromMinutes(30))
                {
                    await Task.Delay(1000, token);
                    if (monitorOverlay is not null && !licenses.CachedStatus().IsPremium)
                    { monitorOverlay.Close(); monitorOverlay = null; }
                    if (paused) continue;
                    var sample = await sensors.SampleAsync(token); token.ThrowIfCancellationRequested(); if (paused) continue; liveSensors = sample;
                    sensorHistory.Add(liveSensors); PaintSensors(liveSensors);
                }
                if (!token.IsCancellationRequested)
                { status.Text = L("30-minute session ended. Reopen monitoring to start again.", "30 分钟会话已结束，重新打开监控页可再次开始。"); liveSensors = null; monitorOverlay?.Update(null, liveFrames, null); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            { if (!token.IsCancellationRequested) { liveSensors = null; monitorOverlay?.Update(null, liveFrames, null); Notice(error.Message, true); } }
            finally { sensors.Dispose(); session.Dispose(); if (ReferenceEquals(monitorCancellation, session)) monitorCancellation = null; }
        }
    }

    private void AddFrameCapture(CancellationToken pageToken, Func<string?> selectedAdapter)
    {
        var target = new ComboBox { Width = 300, Margin = new Thickness(0, 0, 12, 8) };
        void LoadTargets()
        {
            var selected = target.SelectedItem as ProcessIdentity;
            target.Items.Clear();
            foreach (var identity in ProcessTargets.List().Take(500)) target.Items.Add(identity);
            target.SelectedItem = target.Items.Cast<ProcessIdentity>().FirstOrDefault(item => item == selected);
        }
        LoadTargets();
        var duration = new ComboBox { Width = 105, Margin = new Thickness(0, 0, 12, 8) };
        foreach (var seconds in new[] { 15, 30, 60, 120, 300 }) duration.Items.Add(new ComboBoxItem { Content = seconds + " s", Tag = seconds });
        duration.SelectedIndex = 1;
        var summary = Ui.Text(L("Choose a running game process, then start capture.", "选择正在运行的游戏进程，然后开始采集。"), 15, Ui.Accent);
        var captureState = Ui.Text(L("PresentMon 2.6.0 · waiting · up to 5 minutes / 60,000 frames", "PresentMon 2.6.0 · 等待开始 · 最长 5 分钟／60,000 帧"), 12, Ui.Muted);
        var stop = Ui.Button(L("Stop capture", "停止采集"), () => captureCancellation?.Cancel()); stop.IsEnabled = false;
        var export = AsyncButton(L("Export captured frames…", "导出采集帧…"), async () =>
        {
            if (!licenses.CachedStatus().IsPremium) { await NavigateAsync("account"); return; }
            if (lastCapture is null || lastCapture.Samples.Count == 0) return;
            var path = ChooseCsv("optimizer-frames"); if (path is null) return;
            await MonitoringCsv.WriteFramesAsync(path, lastCapture); Notice(L("Saved frame capture: ", "已保存帧数据：") + path);
        }); export.IsEnabled = lastCapture?.Samples.Count > 0;
        var begin = AsyncButton(L("Start FPS capture · Premium", "开始 FPS 采集 · Premium"), async () =>
        {
            if (!licenses.CachedStatus().IsPremium) { await NavigateAsync("account"); return; }
            if (target.SelectedItem is not ProcessIdentity identity)
                throw new InvalidOperationException(L("Choose a process first.", "请先选择进程。"));
            var seconds = (int)((ComboBoxItem)duration.SelectedItem).Tag;
            using var capture = CancellationTokenSource.CreateLinkedTokenSource(pageToken);
            captureCancellation = capture; stop.IsEnabled = true; target.IsEnabled = false; duration.IsEnabled = false; export.IsEnabled = false;
            lastCapture = null; liveFrames = null;
            summary.Text = L("Waiting for presentation events…", "等待真实呈现事件…");
            captureState.Text = identity + L(" · collecting", " · 正在采集");
            try
            {
                await using var collector = PresentMonCapture.CreateBundled();
                var captureTask = collector.CaptureAsync(identity, TimeSpan.FromSeconds(seconds), update =>
                {
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        if (pageToken.IsCancellationRequested || capture.IsCancellationRequested || !ReferenceEquals(captureCancellation, capture)) return;
                        if (!licenses.CachedStatus().IsPremium) { capture.Cancel(); return; }
                        liveFrames = update.Summary;
                        summary.Text = FrameText(update.Summary);
                        captureState.Text = update.Reason ?? $"{update.AcceptedFrames} " + L("frames · ", "帧 · ") + update.RejectedRows + L(" rejected rows", " 行被拒绝");
                        monitorOverlay?.Update(liveSensors, liveFrames, selectedAdapter());
                    });
                }, capture.Token);
                TrackMonitoring(captureTask);
                var result = await captureTask;
                if (pageToken.IsCancellationRequested) return;
                lastCapture = result;
                summary.Text = L("Capture result · ", "采集结果 · ") + FrameText(result.Summary);
                captureState.Text = result.Reason ?? L("Capture complete. Results describe this sample only.", "采集完成，结果仅描述本次样本。");
                export.IsEnabled = result.Samples.Count > 0;
            }
            catch (OperationCanceledException)
            { if (!pageToken.IsCancellationRequested) captureState.Text = L("Capture stopped.", "采集已停止。"); }
            finally
            {
                if (ReferenceEquals(captureCancellation, capture))
                { captureCancellation = null; liveFrames = null; monitorOverlay?.Update(liveSensors, null, selectedAdapter()); }
                stop.IsEnabled = false; target.IsEnabled = true; duration.IsEnabled = true;
            }
        }, true);
        var overlay = new CheckBox { Content = L("Show floating monitor · Premium", "显示监控浮层 · Premium"), Foreground = Ui.Foreground, Margin = new Thickness(0, 0, 12, 8) };
        var clickThrough = new CheckBox { Content = L("Mouse passes through", "鼠标穿透"), Foreground = Ui.Foreground, Margin = new Thickness(0, 0, 12, 8) };
        overlay.Click += async (_, _) =>
        {
            if (overlay.IsChecked == true)
            {
                if (!licenses.CachedStatus().IsPremium) { overlay.IsChecked = false; await NavigateAsync("account"); return; }
                try
                {
                    monitorOverlay = new MonitorOverlay(chinese); monitorOverlay.Show();
                    monitorOverlay.SetClickThrough(clickThrough.IsChecked == true);
                    monitorOverlay.Update(liveSensors, liveFrames, selectedAdapter());
                }
                catch (Exception error) { monitorOverlay?.Close(); monitorOverlay = null; overlay.IsChecked = false; Notice(error.Message, true); }
            }
            else { monitorOverlay?.Close(); monitorOverlay = null; }
        };
        clickThrough.Click += (_, _) =>
        {
            try { monitorOverlay?.SetClickThrough(clickThrough.IsChecked == true); }
            catch (Exception error) { Notice(error.Message, true); }
        };
        page.Children.Add(Ui.Card(Ui.Stack(Ui.Text(L("FPS & FRAME TIMES", "FPS 与帧时间"), 13, Ui.Accent, true),
            Ui.Row(target, duration, Ui.Button(L("Reload processes", "刷新进程"), LoadTargets)), summary, captureState,
            Ui.Row(begin, stop), export, Ui.Row(overlay, clickThrough),
            Ui.Text(L("FPS is application presentation cadence, including dropped frames, not physical display FPS or input latency. A single busiest swapchain is summarized. P95/P99 are nearest-rank frame times; 1% low is 1000 / mean of the slowest 1% intervals.",
                "FPS 为应用呈现频率（含丢弃帧），不是显示器实际帧率或输入延迟。统计取单个最繁忙交换链；P95/P99 为帧时间分位数，1% low 为 1000 除以最慢 1% 帧间隔的均值。"), 12, Ui.Muted),
            Ui.Text(L("ETW capture may require permission. Run the client as administrator yourself only if required. No automatic elevation, injection or driver installation. Overlay supports windowed/borderless games; exclusive fullscreen may hide it.",
                "ETW 采集可能需要权限；如有权限提示，可自行以管理员身份打开客户端。不自动提权、不注入游戏、不安装驱动。浮层适用于窗口／无边框模式，独占全屏可能不可见。"), 12, Ui.Muted))));
    }

    private static string MetricText(Metric metric, string suffix) => metric.Value is double number ? $"{number:0.0}{suffix}" : "—";
    private static string MemoryText(Metric? metric) => metric?.Value is double bytes ? Ui.Bytes((long)bytes) : "—";
    private string FrameText(FrameSummary? summary) => summary is null ? L("No current frame samples", "暂无当前帧样本")
        : $"{summary.AverageFps:0.0} FPS   ·   1% low {summary.OnePercentLowFps:0.0}\nP95 {summary.P95Milliseconds:0.00} ms   /   P99 {summary.P99Milliseconds:0.00} ms   ·   {summary.FrameCount} " + L("frames", "帧");
    private string? ChooseCsv(string prefix)
    {
        var dialog = new SaveFileDialog { FileName = prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv",
            Filter = "CSV (*.csv)|*.csv", DefaultExt = ".csv", AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static void RenderTrend(Canvas canvas, double?[] samples)
    {
        canvas.Children.Clear();
        var width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 760;
        var line = new Polyline { Stroke = Ui.Accent, StrokeThickness = 2 };
        for (var index = 0; index < samples.Length; index++)
        {
            if (samples[index] is not double value)
            { if (line.Points.Count > 1) canvas.Children.Add(line); line = new Polyline { Stroke = Ui.Accent, StrokeThickness = 2 }; continue; }
            line.Points.Add(new Point(index * width / Math.Max(1, samples.Length - 1), 84 - Math.Clamp(value, 0, 100) * 0.78));
        }
        if (line.Points.Count > 1) canvas.Children.Add(line);
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Radiant.Monitoring;

namespace Radiant.App;

internal sealed class MonitorOverlay : Window
{
    private readonly TextBlock metrics = Ui.Text("—", 14, Ui.Accent, true);
    private readonly TextBlock hint;
    private bool clickThrough;
    private const int ExtendedStyle = -20;
    private const long TransparentStyle = 0x20, NoActivateStyle = 0x08000000, ToolWindowStyle = 0x80;

    public MonitorOverlay(bool chinese)
    {
        Title = "Optimizer · monitor overlay";
        Width = 300; Height = 175; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        Left = SystemParameters.WorkArea.Right - Width - 24; Top = SystemParameters.WorkArea.Top + 24;
        hint = Ui.Text(chinese ? "拖动调整位置 · 在监控页关闭" : "Drag to move · close from the monitor page", 10, Ui.Muted);
        var content = Ui.Stack(Ui.Text("R / LIVE", 10, Ui.Muted, true), metrics, hint);
        var card = Ui.Card(content, 16); card.Margin = new Thickness(0); card.Background = Ui.Brush("#E619201B");
        Content = card;
        MouseLeftButtonDown += (_, eventArgs) =>
        {
            if (clickThrough || eventArgs.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        SourceInitialized += (_, _) => ApplyMouseMode();
    }

    public void SetClickThrough(bool value)
    {
        clickThrough = value;
        hint.Opacity = value ? 0.45 : 1;
        ApplyMouseMode();
    }

    private void ApplyMouseMode()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = Native.GetWindowLongPtr(handle, ExtendedStyle).ToInt64() | ToolWindowStyle;
        style = clickThrough ? style | TransparentStyle | NoActivateStyle : style & ~(TransparentStyle | NoActivateStyle);
        Marshal.SetLastPInvokeError(0);
        if (Native.SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style)) == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public void Update(MonitorSample? sample, FrameSummary? frames, string? gpuId)
    {
        static string N(double? value, string format = "0.0") => value is null ? "—" : value.Value.ToString(format);
        var gpu = sample?.Gpus.FirstOrDefault(item => item.Id == gpuId) ?? sample?.Gpus.FirstOrDefault();
        metrics.Text = $"CPU {N(sample?.CpuPercent.Value)}%   GPU {N(gpu?.UsagePercent.Value)}%\n"
            + $"RAM {N(sample?.MemoryUsedBytes.Value / 1073741824)} GB   GPU {N(gpu?.TemperatureCelsius.Value)} °C\n"
            + $"FPS {N(frames?.AverageFps, "0")}   P99 {N(frames?.P99Milliseconds)} ms";
    }

    private static class Native
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    }
}

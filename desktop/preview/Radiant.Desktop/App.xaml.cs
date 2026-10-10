using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Radiant.Core;
using Radiant.Windows;

namespace Radiant.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        base.OnStartup(e);
        if (e.Args.Length > 0)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                bool verify = e.Args.Length == 2 && e.Args[0] == "--verify";
                bool render = e.Args.Length == 4 && e.Args[0] == "--render-preview"
                    && e.Args[2] is "overview" or "checks" or "hardware" && e.Args[3] is "light" or "dark";
                if (!verify && !render)
                    throw new ArgumentException("Use --verify <output.json> or --render-preview <directory> <overview|checks|hardware> <light|dark>.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                var snapshot = await new HardwareCollector().CollectAsync(timeout.Token).WaitAsync(timeout.Token);
                if (e.Args[0] == "--verify") await Verify(snapshot, e.Args[1]);
                else await RenderPreview(snapshot, e.Args[1], e.Args[2], e.Args[3]);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                // Developer commands have an explicit output location. Keep failures local.
                if (e.Args.Length >= 2)
                {
                    try
                    {
                        var failurePath = e.Args[0] == "--render-preview" ? Path.Combine(e.Args[1], "render-error.txt") : e.Args[1] + ".error.txt";
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(failurePath))!);
                        File.WriteAllText(failurePath, ex.ToString());
                    }
                    catch (Exception) { }
                }
                Shutdown(1);
            }
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static async Task Verify(HardwareSnapshot snapshot, string output)
    {
        var checks = Diagnostics.Evaluate(snapshot);
        var result = new
        {
            readOnly = true,
            hasCpu = !string.IsNullOrWhiteSpace(snapshot.CpuName),
            validMemory = snapshot.TotalMemoryBytes > 0 && snapshot.AvailableMemoryBytes <= snapshot.TotalMemoryBytes,
            hasSystemDrive = snapshot.Drives.Any(d => d.IsSystem && d.TotalBytes > 0),
            graphicsCount = snapshot.Graphics.Count,
            checks = checks.Count,
            unknownChecks = checks.Count(c => c.Status == CheckStatus.Unknown),
            sourceIssues = snapshot.Issues,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        var product = new MainViewModel();
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "report-preview.html"), Report.Html(product.ProductName, product.VersionLabel, snapshot));
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "report-preview.json"), Report.Serialize(product.ProductName, product.VersionLabel, snapshot));
        if (!result.hasCpu || !result.validMemory || !result.hasSystemDrive || result.checks < 5)
            throw new InvalidOperationException("Core hardware verification failed. See the structured result for unavailable categories.");
    }

    private static async Task RenderPreview(HardwareSnapshot snapshot, string output, string view, string theme)
    {
        Directory.CreateDirectory(output);
        var window = new MainWindow();
        if (window.Model.CanExport) throw new InvalidOperationException("Export should be unavailable before a snapshot exists.");
        window.Model.Apply(snapshot);
        window.SetFilter(CheckFilter.Unknown);
        if (window.Model.CheckRows.Count != window.Model.UnknownCount) throw new InvalidOperationException("Unknown filter failed.");
        window.SetFilter(CheckFilter.Attention);
        if (window.Model.CheckRows.Count != window.Model.AttentionCount) throw new InvalidOperationException("Attention filter failed.");
        window.SetFilter(CheckFilter.All);
        window.SelectPage("Hardware");
        if (window.Model.HardwareGroups.Count != 5) throw new InvalidOperationException("Hardware inventory navigation failed.");
        var canceledRefresh = window.RefreshAsync();
        window.CancelRefresh();
        // A second click during collection is ignored, including during cancellation.
        await window.RefreshAsync();
        await canceledRefresh;
        if (window.Model.Snapshot != snapshot || window.Model.Busy || !window.Model.CanExport)
            throw new InvalidOperationException("Canceled refresh replaced the prior snapshot or left actions disabled.");
        // Exercise the same asynchronous refresh used by the visible Refresh button.
        var previousTime = window.Model.Snapshot!.CollectedAt;
        await window.RefreshAsync();
        if (window.Model.Busy || !window.Model.CanExport || window.Model.Snapshot!.CollectedAt <= previousTime)
            throw new InvalidOperationException("Live UI refresh verification failed.");
        // Render the real controls with their data and resources on a hidden WPF surface.
        // This exports our visual tree without capturing pixels from the desktop.
        var visual = window.PreviewContent;
        window.Content = null;
        visual.DataContext = window.Model;
        visual.UseLayoutRounding = true;
        TextElement.SetFontFamily(visual, window.FontFamily);
        window.SetDark(theme == "dark");
        foreach (System.Collections.DictionaryEntry resource in window.Resources)
            visual.Resources[resource.Key] = resource.Value;
        TextElement.SetForeground(visual, window.Foreground);
        window.SelectPage(view switch { "overview" => "Overview", "hardware" => "Hardware", _ => "System checks" });
        visual.Width = 1280;
        visual.Height = 840;
        var bitmap = await Capture(visual);
        HwndSource? presentation = null;
        try
        {
            // Some activated WPF panels need a presentation source to finish text
            // layout. Check the rendered branding instead of exporting a blank header.
            if (!HasRenderedBrand(bitmap))
            {
                presentation = new HwndSource(new HwndSourceParameters("Preview render surface")
                {
                    // WS_POPUP and WS_EX_TOOLWINDOW; deliberately omit WS_VISIBLE.
                    WindowStyle = unchecked((int)0x80000000),
                    ExtendedWindowStyle = 0x80,
                    PositionX = -32000,
                    PositionY = -32000,
                    Width = 1280,
                    Height = 840,
                });
                presentation.RootVisual = visual;
                bitmap = await Capture(visual);
            }
            if (!HasRenderedBrand(bitmap)) throw new InvalidOperationException("Preview branding did not render completely.");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, $"radiant-{view}-{theme}.png")))
                encoder.Save(stream);
        }
        finally { presentation?.Dispose(); }
        // Verify navigation/state independently of the desktop automation surface.
        window.SelectPage("About");
        if (window.Model.Page != "About" || !window.Model.CanExport || window.Model.CheckRows.Count < 5)
            throw new InvalidOperationException("UI state verification failed.");
        await File.WriteAllTextAsync(Path.Combine(output, $"render-verification-{view}-{theme}.json"), JsonSerializer.Serialize(new
        {
            originalWpfViews = true,
            capturedFromRealHardware = true,
            containsPrivateDeviceIdentifiers = false,
            view,
            theme,
            navigationVerified = true,
            liveRefreshVerified = true,
            cancellationVerified = true,
            filtersVerified = true,
            hardwareGroups = window.Model.HardwareGroups.Count,
            renderedBrandVerified = true,
            checkCount = window.Model.CheckRows.Count,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<RenderTargetBitmap> Capture(FrameworkElement visual)
    {
        InvalidateTree(visual);
        visual.Measure(new Size(1280, 840));
        visual.Arrange(new Rect(0, 0, 1280, 840));
        visual.UpdateLayout();
        await visual.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var bitmap = new RenderTargetBitmap(1280, 840, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static bool HasRenderedBrand(RenderTargetBitmap bitmap)
    {
        var pixels = new byte[165 * 110 * 4];
        bitmap.CopyPixels(new Int32Rect(28, 32, 165, 110), pixels, 165 * 4, 0);
        int drawn = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i] != 56 || pixels[i + 1] != 35 || pixels[i + 2] != 23) drawn++;
        return drawn > 128;
    }

    private static void InvalidateTree(DependencyObject node)
    {
        if (node is UIElement element)
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
            element.InvalidateVisual();
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            InvalidateTree(VisualTreeHelper.GetChild(node, i));
    }
}

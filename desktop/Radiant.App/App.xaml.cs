using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Radiant.Windows;
using Radiant.Monitoring;
using Radiant.Games;

namespace Radiant.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var settings = ProductSettings.Load();
            string? Argument(string name)
            {
                var index = Array.IndexOf(e.Args, name);
                return index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
            }
            var dataDirectory = Path.GetFullPath(Argument("--data-directory") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerPreview"));
            var engine = new WindowsOptimizer(dataDirectory);
            if (Argument("--verify-gaming-roundtrip") is string gamingReport)
            {
                var result = await Task.Run(() => WindowsRoundtripVerification.RunAsync(
                    Path.Combine(dataDirectory, "gaming-verification-" + Guid.NewGuid().ToString("N")), gaming: true));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(gamingReport))!);
                await File.WriteAllTextAsync(gamingReport, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
                return;
            }
            if (Argument("--restore-settings-output") is string recoveryPath)
            {
                var result = await Task.Run(() => engine.RestoreAllAsync());
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(recoveryPath))!);
                await File.WriteAllTextAsync(recoveryPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(result.All(r => r.Status == Radiant.Core.ChangeStatus.Restored) ? 0 : 1);
                return;
            }
            if (Argument("--verify-settings-roundtrip") is string roundtripPath)
            {
                var result = await Task.Run(() => WindowsRoundtripVerification.RunAsync(Path.Combine(dataDirectory, "verification-" + Guid.NewGuid().ToString("N"))));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(roundtripPath))!);
                await File.WriteAllTextAsync(roundtripPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
                return;
            }
            if (Argument("--diagnostics-output") is string diagnosticsPath)
            {
                using var monitor = new SystemMonitor(engine);
                await using var frameTool = PresentMonCapture.CreateBundled();
                var gameProfiles = new GameProfileEngine(Path.Combine(dataDirectory, "game-profiles"));
                var diagnostic = new { capturedAt = DateTimeOffset.UtcNow,
                    system = await engine.ReadSystemAsync(), tweaks = await engine.ReadTweaksAsync(),
                    powerPlans = await engine.ReadPowerPlansAsync(), processes = await engine.ReadProcessesAsync(),
                    monitoring = await monitor.SampleAsync(), gameConfigurations = await gameProfiles.DiscoverAsync(),
                    presentMonToolIssue = await frameTool.CheckAvailabilityAsync() };
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(diagnosticsPath))!);
                await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostic,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
                Shutdown(0);
                return;
            }
            var window = new MainWindow(settings, engine, dataDirectory);
            MainWindow = window;
            if (Argument("--snapshot") is string snapshotPath)
            {
                await window.InitializeAsync(refreshLicense: false, initialPage: Argument("--snapshot-page") ?? "overview");
                if (Argument("--snapshot-page") == "monitoring") await Task.Delay(2000);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1200, 820));
                content.Arrange(new Rect(0, 0, 1200, 820));
                content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1200, 820, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(snapshotPath))!);
                using (var output = File.Create(snapshotPath)) encoder.Save(output);
                Shutdown(0);
                return;
            }
            window.Show();
            await window.InitializeAsync();
        }
        catch (Exception error)
        {
            if (e.Args.Length > 0)
            {
                var errorPath = Path.Combine(AppContext.BaseDirectory, "diagnostics-error.txt");
                File.WriteAllText(errorPath, error.ToString());
            }
            else MessageBox.Show(error.Message, "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

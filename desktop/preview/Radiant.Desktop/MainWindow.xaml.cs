using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Radiant.Core;
using Radiant.Windows;

namespace Radiant.Desktop;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource closing = new();
    private readonly HardwareCollector collector = new();
    private bool dark;
    private bool loaded;
    private CancellationTokenSource? activeRequest;
    private Task<HardwareSnapshot>? activeCollection;
    public MainViewModel Model { get; } = new();
    public FrameworkElement PreviewContent => Shell;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = Model;
        SetDark(true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded) return;
        loaded = true;
        await RefreshAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    public async Task RefreshAsync()
    {
        if (!Model.CanRefresh || activeCollection is { IsCompleted: false }) return;
        Model.Busy = true;
        Model.CancelRequested = false;
        Model.Message = "";
        using var request = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        activeRequest = request;
        request.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            activeCollection = collector.CollectAsync(request.Token);
            var snapshot = await activeCollection.WaitAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            Model.Apply(snapshot);
        }
        catch (OperationCanceledException)
        {
            if (!closing.IsCancellationRequested)
                Model.Message = Model.CancelRequested
                    ? "Refresh canceled. Any previous snapshot keeps its original collection time."
                    : "Windows took too long to respond. Any previous snapshot remains unchanged. Refresh to try again.";
        }
        catch (Exception)
        {
            Model.Message = Model.Snapshot is null
                ? "Windows information could not be collected. Refresh to try again. No settings were changed."
                : "The refresh failed. The previous snapshot is still shown with its original collection time. No settings were changed.";
        }
        finally
        {
            // WMI may finish its current native query after cancellation. Keep the
            // refresh guard until it exits so retries never stack physical scans.
            if (activeCollection is { IsCompleted: false } && !closing.IsCancellationRequested)
            {
                Model.CancelRequested = true;
                try { await activeCollection; } catch (Exception) { }
            }
            activeRequest = null;
            Model.Busy = false;
            Model.CancelRequested = false;
        }
    }

    public void CancelRefresh()
    {
        if (!Model.CanCancel) return;
        Model.CancelRequested = true;
        activeRequest?.Cancel();
    }
    private void OnCancelRefresh(object sender, RoutedEventArgs e) => CancelRefresh();
    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { e.Handled = true; await RefreshAsync(); }
        else if (e.Key == Key.Escape && Model.CanCancel) { e.Handled = true; CancelRefresh(); }
    }

    private void OnClosing(object? sender, CancelEventArgs e) => closing.Cancel();
    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (OverviewPage is null) return;
        if (sender is RadioButton { Tag: string page }) SelectPage(page);
    }

    public void SelectPage(string page)
    {
        if (page is not ("Overview" or "System checks" or "Hardware" or "About")) throw new ArgumentException("Unknown page.", nameof(page));
        Model.Page = page;
        OverviewPage.Visibility = page == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        ChecksPage.Visibility = page == "System checks" ? Visibility.Visible : Visibility.Collapsed;
        HardwarePage.Visibility = page == "Hardware" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;
        OverviewNav.IsChecked = page == "Overview";
        ChecksNav.IsChecked = page == "System checks";
        HardwareNav.IsChecked = page == "Hardware";
        AboutNav.IsChecked = page == "About";
        PageScroll.ScrollToTop();
    }

    private void OnViewChecks(object sender, RoutedEventArgs e) => SelectPage("System checks");
    private void OnViewHardware(object sender, RoutedEventArgs e) => SelectPage("Hardware");
    private void OnFilter(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string value } && Enum.TryParse<CheckFilter>(value, out var filter)) SetFilter(filter);
    }
    public void SetFilter(CheckFilter filter)
    {
        Model.Filter = filter;
        if (AllFilter is null || AttentionFilter is null || UnknownFilter is null) return;
        AllFilter.IsChecked = filter == CheckFilter.All;
        AttentionFilter.IsChecked = filter == CheckFilter.Attention;
        UnknownFilter.IsChecked = filter == CheckFilter.Unknown;
    }
    private void OnToggleTheme(object sender, RoutedEventArgs e) => SetDark(!dark);

    public void SetDark(bool value)
    {
        dark = value;
        Model.DarkTheme = value;
        var palette = dark ? new Dictionary<string, string>
        {
            ["CanvasBrush"] = "#182234",
            ["SurfaceBrush"] = "#202D41",
            ["InkBrush"] = "#E4ECF8",
            ["MutedBrush"] = "#A2AFC3",
            ["LineBrush"] = "#354359",
            ["AccentBrush"] = "#547AE1",
            ["AccentSoftBrush"] = "#2B3D60",
        } : new Dictionary<string, string>
        {
            ["CanvasBrush"] = "#F5F6F8",
            ["SurfaceBrush"] = "#FFFFFF",
            ["InkBrush"] = "#202B3D",
            ["MutedBrush"] = "#6B7789",
            ["LineBrush"] = "#E1E6ED",
            ["AccentBrush"] = "#375FD2",
            ["AccentSoftBrush"] = "#EAF0FF",
        };
        foreach (var (key, color) in palette) Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        ThemeButton.Content = dark ? "Light mode" : "Dark mode";
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (!Model.CanExport || Model.Snapshot is null) return;
        var snapshot = Model.Snapshot;
        Model.Exporting = true;
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save local hardware report", Filter = "Readable HTML report (*.html)|*.html|JSON report (*.json)|*.json",
                FileName = $"radiant-report-{snapshot.CollectedAt:yyyy-MM-dd-HHmmss}", DefaultExt = ".html", AddExtension = true,
            };
            if (dialog.ShowDialog(this) != true) return;
            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            if (extension is not (".html" or ".htm" or ".json"))
            { Model.Message = "Choose an .html or .json file so the report opens in the right application."; return; }
            var report = extension == ".json" ? Report.Serialize(Model.ProductName, Model.VersionLabel, snapshot) : Report.Html(Model.ProductName, Model.VersionLabel, snapshot);
            await File.WriteAllTextAsync(dialog.FileName, report);
            Model.Message = "Report saved to your chosen file. Nothing was uploaded.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { Model.Message = "The report could not be saved. Choose a writable location and try again."; }
        finally { Model.Exporting = false; }
    }
}

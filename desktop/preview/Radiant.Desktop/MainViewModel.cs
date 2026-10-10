using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Media;
using Radiant.Core;

namespace Radiant.Desktop;

public sealed record DetailRow(string Label, string Value);
public sealed record CheckRow(string Title, string StatusLabel, Brush StatusBrush, string Evidence, string Explanation, string NextStep);

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private HardwareSnapshot? snapshot;
    private bool busy;
    private bool exporting;
    private bool cancelRequested;
    private CheckFilter filter;
    private bool darkTheme;
    private string message = "";
    private string page = "Overview";
    public string ProductName => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Desktop client";
    public string VersionLabel => $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} · Preview";
    public HardwareSnapshot? Snapshot => snapshot;
    public IReadOnlyList<CheckResult> Checks { get; private set; } = [];
    public IReadOnlyList<DetailRow> HardwareRows { get; private set; } = [];
    public IReadOnlyList<HardwareGroup> HardwareGroups { get; private set; } = [];
    public IReadOnlyList<HardwareGroup> PrimaryHardwareGroups => HardwareGroups.Where((_, index) => index is 0 or 2 or 4).ToArray();
    public IReadOnlyList<HardwareGroup> SecondaryHardwareGroups => HardwareGroups.Where((_, index) => index is 1 or 3).ToArray();
    public IReadOnlyList<CheckRow> CheckRows => CheckSelection.Filter(Checks, Filter).Select(c => new CheckRow(c.Title, c.Status.ToString().ToUpperInvariant(), StatusColor(c.Status), c.Evidence, c.Explanation, c.NextStep)).ToArray();
    public CheckFilter Filter { get => filter; set { filter = value; Notify(); } }
    public string Page { get => page; set { page = value; Notify(); } }
    public bool Busy { get => busy; set { busy = value; Notify(); } }
    public bool Exporting { get => exporting; set { exporting = value; Notify(); } }
    public bool CancelRequested { get => cancelRequested; set { cancelRequested = value; Notify(); } }
    public bool DarkTheme { get => darkTheme; set { darkTheme = value; Notify(); } }
    public string Message { get => message; set { message = value; Notify(); } }
    public bool HasMessage => Message.Length > 0;
    public bool CanRefresh => !Busy && !Exporting;
    public bool CanExport => snapshot is not null && !Busy && !Exporting;
    public bool CanCancel => Busy && !CancelRequested;
    public bool HasSnapshot => snapshot is not null;
    public string RefreshLabel => Busy ? CancelRequested ? "Finishing current read…" : "Reading Windows…" : "Refresh snapshot";
    public string CpuValue => snapshot?.CpuLoadPercent is { } v ? $"{v.ToString("0", CultureInfo.InvariantCulture)}%" : "—";
    public double CpuPercent => snapshot?.CpuLoadPercent ?? 0;
    public string CpuDetail => snapshot?.CpuLoadPercent is not null ? "Sampled across all logical processors" : "CPU activity reading unavailable";
    private bool MemoryKnown => snapshot?.TotalMemoryBytes is > 0 && snapshot.AvailableMemoryBytes is not null && snapshot.AvailableMemoryBytes <= snapshot.TotalMemoryBytes;
    public string MemoryValue => MemoryKnown ? $"{GiB(snapshot!.TotalMemoryBytes!.Value - snapshot.AvailableMemoryBytes!.Value, "0.0")} GiB" : "—";
    public double MemoryPercent => MemoryKnown ? 100d * (snapshot!.TotalMemoryBytes!.Value - snapshot.AvailableMemoryBytes!.Value) / snapshot.TotalMemoryBytes.Value : 0;
    public string MemoryDetail => MemoryKnown ? $"of {GiB(snapshot!.TotalMemoryBytes!.Value, "0.0")} GiB visible to Windows" : "Physical memory reading unavailable";
    private DriveSnapshot? SystemDrive => snapshot?.Drives.FirstOrDefault(d => d.IsSystem && d.TotalBytes > 0 && d.FreeBytes <= d.TotalBytes);
    public string StorageValue => SystemDrive is { } d ? $"{GiB(d.FreeBytes, "0")} GiB" : "—";
    public double StoragePercent => SystemDrive is { } d ? 100d * d.FreeBytes / d.TotalBytes : 0;
    public string StorageDetail => SystemDrive is { } d ? $"{StoragePercent.ToString("0", CultureInfo.InvariantCulture)}% of {GiB(d.TotalBytes, "0")} GiB available" : "System drive reading unavailable";
    public string CollectedLabel => snapshot is null ? "Waiting for a real Windows snapshot." : $"Collected {snapshot.CollectedAt.ToString("MMM d, yyyy · HH:mm:ss zzz", CultureInfo.InvariantCulture)} · Refresh to read again.";
    public int AttentionCount => Checks.Count(c => c.Status == CheckStatus.Attention);
    public int UnknownCount => Checks.Count(c => c.Status == CheckStatus.Unknown);
    public string AllFilterLabel => $"All checks · {Checks.Count}";
    public string AttentionFilterLabel => $"To review · {AttentionCount}";
    public string UnknownFilterLabel => $"Unavailable · {UnknownCount}";
    public bool ChecksEmpty => CheckRows.Count == 0;
    public string EmptyTitle => snapshot is null ? "Your first snapshot is on its way." : Filter == CheckFilter.Attention ? "Nothing to review in this snapshot." : "No unavailable readings.";
    public string EmptyDetail => snapshot is null ? "Refresh to read Windows information. The checks will appear here when collection finishes." : Filter == CheckFilter.Attention ? "None of the checks crossed an attention threshold. Open All checks to review the evidence and any unavailable readings." : "Open All checks to see the complete results.";
    public string SummaryTitle => snapshot is null ? "Let's take a look." : AttentionCount > 0 ? "A few things to review." : UnknownCount > 0 ? "Some readings unavailable." : "Your snapshot is ready.";
    public string SummaryDetail => snapshot is null ? "Read your hardware and run a small set of checks. Your Windows settings stay untouched." : "A readable check of memory, storage and Windows settings. Each finding includes its evidence.";
    public string AttentionSummary => snapshot is null ? "No scan has completed." : $"{AttentionCount} {(AttentionCount == 1 ? "item" : "items")} to review";
    public string UnknownSummary => snapshot is null ? "Real readings appear after collection." : $"{UnknownCount} unavailable · {Checks.Count} checks in total";
    public string StatusLabel => Busy ? CancelRequested ? "Finishing current Windows query…" : "Reading local Windows information…" : Exporting ? "Saving your local report…" : snapshot is null ? "Ready to read this PC" : snapshot.Issues.Count > 0 ? "Snapshot complete · Some sources unavailable" : "Snapshot complete · Read-only";

    public void Apply(HardwareSnapshot value)
    {
        snapshot = value;
        Checks = Diagnostics.Evaluate(value);
        HardwareGroups = HardwareDetails.Create(value);
        HardwareRows = [
            new("Processor", value.CpuName ?? "Unavailable"),
            new("CPU layout", value.CpuCores is > 0 && value.LogicalProcessors is > 0 ? $"{value.CpuCores} cores / {value.LogicalProcessors} logical processors" : "Unavailable"),
            new("Graphics", value.Graphics.Count > 0 ? string.Join("\n", value.Graphics.Select(g => g.Name)) : "Unavailable"),
            new("Memory", value.TotalMemoryBytes is { } total ? $"{GiB(total, "0.0")} GiB visible to Windows" : "Unavailable"),
            new("Windows", value.OsVersion is { } version ? $"Version {version} · {value.Architecture}" : "Unavailable"),
        ];
        Message = value.Issues.Count > 0 ? string.Join(" ", value.Issues) : "";
        Notify();
    }

    private Brush StatusColor(CheckStatus status) => new SolidColorBrush((Color)ColorConverter.ConvertFromString((status, DarkTheme) switch
    {
        (CheckStatus.Pass, true) => "#9BCBB7", (CheckStatus.Pass, false) => "#28654F",
        (CheckStatus.Attention, true) => "#F0C07A", (CheckStatus.Attention, false) => "#8A5611",
        (CheckStatus.Info, true) => "#A8BEF3", (CheckStatus.Info, false) => "#3A5BA8",
        (_, true) => "#AFBCCF", _ => "#586579",
    }));
    private static string GiB(ulong bytes, string format) => (bytes / 1073741824d).ToString(format, CultureInfo.InvariantCulture);
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

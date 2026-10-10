using Radiant.Core;
using System.Text.Json;

int passed = 0;
void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}

Assert(Checks.ForMemory(100, 9).Status == CheckStatus.Attention, "memory below 10 percent");
Assert(Checks.ForMemory(100, 10).Status == CheckStatus.Pass, "memory exact threshold");
Assert(Checks.ForMemory(null, null).Status == CheckStatus.Unknown, "missing memory is unknown");
Assert(Checks.ForMemory(0, 0).Status == CheckStatus.Unknown, "zero total memory is unknown");
Assert(Checks.ForMemory(100, 101).Status == CheckStatus.Unknown, "invalid memory is unknown");
Assert(Checks.ForStorage(100, 14).Status == CheckStatus.Attention, "storage below 15 percent");
Assert(Checks.ForStorage(100, 15).Status == CheckStatus.Pass, "storage exact threshold");
Assert(Checks.ForStorage(100, 0).Status == CheckStatus.Attention, "full disk is attention");
Assert(Checks.ForStorage(null, null).Status == CheckStatus.Unknown, "missing disk is unknown");
Assert(Checks.ForStorage(0, 0).Status == CheckStatus.Unknown, "zero disk is unknown");
Assert(Checks.ForStorage(100, 101).Status == CheckStatus.Unknown, "invalid disk is unknown");
var empty = Diagnostics.Evaluate(new HardwareSnapshot());
Assert(empty.All(c => c.Status == CheckStatus.Unknown), "empty snapshot has no false passes");
var snapshot = new HardwareSnapshot
{
    TotalMemoryBytes = 100,
    AvailableMemoryBytes = 50,
    Drives = [new("System drive", true, 100, 30)],
    PowerPlan = "Balanced",
    GameModeEnabled = false,
    Uptime = TimeSpan.FromDays(1),
    Issues = ["Graphics information is unavailable."]
};
var results = Diagnostics.Evaluate(snapshot);
Assert(results.Count(c => c.Id == "collection") == 1, "collection failures retained");
Assert(results.Single(c => c.Id == "collection").Status == CheckStatus.Unknown, "collection failure is not a pass");
Assert(results.Single(c => c.Id == "power").Status == CheckStatus.Info, "power plan has no arbitrary best score");
Assert(results.Single(c => c.Id == "game-mode").Status == CheckStatus.Info, "disabled Game Mode is not called a fault");
Assert(Diagnostics.Evaluate(snapshot with { Uptime = TimeSpan.FromDays(8) }).Single(c => c.Id == "uptime").Status == CheckStatus.Info, "uptime alone is not a fault");
Assert(Diagnostics.Evaluate(snapshot with { Drives = [new("Other drive", false, 100, 80)] }).Single(c => c.Id == "storage").Status == CheckStatus.Unknown, "only system drive is evaluated");
using var report = JsonDocument.Parse(Report.Serialize("Test product", "0.1", snapshot));
Assert(report.RootElement.GetProperty("readOnly").GetBoolean(), "export identifies read-only scope");
Assert(report.RootElement.GetProperty("checks")[0].GetProperty("Status").GetString() == "Pass", "export uses readable statuses");
Assert(report.RootElement.GetProperty("snapshot").GetProperty("AvailableMemoryBytes").GetUInt64() == 50, "export preserves actual readings");
Assert(!report.RootElement.GetProperty("snapshot").TryGetProperty("MachineName", out _), "report excludes machine identity");
Assert(CheckSelection.Filter(results, CheckFilter.Attention).Count == 0, "attention filter can be empty without inventing findings");
Assert(CheckSelection.Filter(results, CheckFilter.Unknown).All(c => c.Status == CheckStatus.Unknown), "unknown filter excludes informational checks");
Assert(CheckSelection.Filter(results, CheckFilter.All).Count == results.Count, "all filter preserves collection findings");
Assert(CheckGuidance.For(Checks.ForMemory(100, 9)).Contains("Task Manager"), "low memory has a practical next step");
Assert(CheckGuidance.For(Checks.ForMemory(null, null)).Contains("Refresh"), "unknown memory asks for another reading");
Assert(!CheckGuidance.For(Checks.ForMemory(100, 50)).Contains("Task Manager"), "healthy reading does not recommend unnecessary work");
var detailed = snapshot with
{
    CpuName = "<script>alert('x')</script>",
    CpuCores = 8, LogicalProcessors = 16,
    Graphics = [new("Graphics & Display", "32.1.2.3"), new("Second GPU", null)],
    Drives = [new("C:", true, 107374182400, 53687091200), new("D:", false, 214748364800, 107374182400)],
    OsVersion = "10.0.26200", Architecture = "X64",
};
var details = HardwareDetails.Create(detailed).SelectMany(g => g.Rows).ToArray();
Assert(details.Any(r => r.Value.Contains("32.1.2.3")), "hardware detail includes GPU driver version");
Assert(details.Any(r => r.Label.Contains("D:")), "hardware detail includes non-system fixed drives");
Assert(details.Any(r => r.Value.Contains("Unavailable")), "missing GPU driver is explicit");
Assert(HardwareDetails.Create(new HardwareSnapshot()).SelectMany(g => g.Rows).Any(r => r.Value == "Unavailable"), "empty hardware inventory contains unknown labels");
var html = Report.Html("Test & product", "0.2.0", detailed);
Assert(html.Contains("&lt;script&gt;") && !html.Contains("<script>"), "HTML report encodes malicious hardware text");
Assert(html.Contains("Test &amp; product") && html.Contains("Graphics &amp; Display"), "HTML report escapes titles and adapters");
Assert(html.Contains("D:") && html.Contains("32.1.2.3"), "HTML report includes drives and drivers");
Assert(html.Contains("Next step") && html.Contains(CheckGuidance.For(results[0])), "HTML report includes guidance");
Assert(!html.Contains("https://") && !html.Contains("http://") && !html.Contains("<script"), "HTML report has no scripts or external dependencies");
Assert(html.Contains("Content-Security-Policy"), "offline report restricts resource loading");
using var report2 = JsonDocument.Parse(Report.Serialize("Test product", "0.2.0", detailed));
Assert(report2.RootElement.GetProperty("checks")[0].GetProperty("NextStep").GetString()?.Length > 0, "JSON report also includes next steps");
Assert(report2.RootElement.GetProperty("snapshot").GetProperty("Drives").GetArrayLength() == 2, "JSON preserves full drive inventory");
Console.WriteLine($"{passed} core checks passed.");

using System.Globalization;
using System.Text.Json;
using Radiant.Core;

namespace Radiant.Windows;

public sealed class ProcessTuningService
{
    public const string EndedNotice = "Process ended — no persistent change. The saved process instance is gone; no replacement process was modified.";
    private readonly IChangeJournal journal;
    private readonly IGameProcessAccess access;
    private readonly OptimizationEngine engine;
    private readonly SemaphoreSlim gate = new(1, 1);
    public ProcessTuningService(string directory) : this(new FileChangeJournal(directory), new NativeGameProcesses()) { }
    public ProcessTuningService(IChangeJournal journal, IGameProcessAccess access)
    { this.journal = journal; this.access = access; engine = new(new Adapter(access), journal); }

    public Task<ChangeRecord> ApplyAsync(ProcessTuningRequest request, CancellationToken token = default)
        => Exclusive(async () =>
        {
            await ReconcileExited(token);
            // Each application owns its inspection baseline; it cannot leak into later recovery.
            var applying = new OptimizationEngine(new Adapter(access), journal);
            var result = await applying.ApplyAsync("cpu-tuning", JsonSerializer.Serialize(request), token);
            await ReconcileExited(CancellationToken.None);
            return (await Details(CancellationToken.None)).Single(r => r.Id == result.Id);
        }, token);
    public Task<IReadOnlyList<ChangeRecord>> ReadHistoryAsync(CancellationToken token = default)
        => Exclusive<IReadOnlyList<ChangeRecord>>(async () =>
        {
            await ReconcileExited(token); await engine.ReadHistoryAsync(token); return await Details(token);
        }, token);
    public Task<IReadOnlyList<ChangeRecord>> RestoreAllAsync(CancellationToken token = default)
        => Exclusive<IReadOnlyList<ChangeRecord>>(async () =>
        {
            await ReconcileExited(token);
            var result = await engine.RestoreAllAsync(token); await ReconcileExited(CancellationToken.None);
            var ids = result.Select(r => r.Id).ToHashSet();
            return (await Details(CancellationToken.None)).Where(r => ids.Contains(r.Id)).ToArray();
        }, token);
    public Task<ChangeRecord> RestoreAsync(string id, bool force = false, CancellationToken token = default)
        => Exclusive(async () =>
        {
            await ReconcileExited(token); await engine.RestoreAsync(id, force, token);
            await ReconcileExited(CancellationToken.None);
            return (await Details(CancellationToken.None)).Single(r => r.Id == id);
        }, token);
    private async Task<T> Exclusive<T>(Func<Task<T>> work, CancellationToken token)
    { await gate.WaitAsync(token); try { return await work(); } finally { gate.Release(); } }

    private async Task ReconcileExited(CancellationToken token)
    {
        await using var lease = await journal.AcquireAsync(token);
        var entries = await journal.ReadAllAsync(token);
        foreach (var entry in entries) ValidateEntry(entry);
        foreach (var entry in entries.Where(e => e.Status is not (ChangeStatus.Restored or ChangeStatus.Unchanged)))
        {
            var target = GameProcessTarget.Parse(entry.Changes[0].SettingId).Target;
            bool ended;
            try { ended = access.HasExited(target); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
            { continue; } // An inaccessible game may still be running. Keep its originals outstanding.
            if (ended) await journal.SaveAsync(entry with { Status = ChangeStatus.Restored, Error = EndedNotice }, token);
        }
    }
    private async Task<IReadOnlyList<ChangeRecord>> Details(CancellationToken token)
    {
        await using var lease = await journal.AcquireAsync(token);
        var entries = await journal.ReadAllAsync(token);
        foreach (var entry in entries) ValidateEntry(entry);
        return entries.OrderByDescending(e => e.CreatedAt).Select(e => e.ToRecord() with {
            TweakId = GameProcessTarget.Parse(e.Changes[0].SettingId).Target.ToString(),
            Before = string.Join(" · ", e.Changes.Select(c => Describe(c.SettingId, c.Before))),
            After = string.Join(" · ", e.Changes.Select(c => Describe(c.SettingId, c.After))) }).ToArray();
    }
    private static string Describe(string id, SettingValue value) => GameProcessTarget.Parse(id).Field == "priority"
        ? "Priority: " + ProcessPriority.Label(uint.Parse(value.Data, CultureInfo.InvariantCulture))
        : "Logical CPUs: " + ProcessTuningState.CpuList(ulong.Parse(value.Data, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
    private static void ValidateEntry(JournalEntry entry)
    {
        entry.Validate();
        if (entry.TweakId != "cpu-tuning" || entry.Changes.Count > 2) throw new InvalidDataException("Invalid CPU recovery record.");
        var target = GameProcessTarget.Parse(entry.Changes[0].SettingId).Target;
        foreach (var change in entry.Changes)
        {
            var parsed = GameProcessTarget.Parse(change.SettingId);
            if (parsed.Target != target) throw new InvalidDataException("CPU recovery records cannot span process instances.");
            ValidateValue(parsed.Field, change.Before); ValidateValue(parsed.Field, change.After);
            if (change.RestoreExpected is not null) ValidateValue(parsed.Field, change.RestoreExpected);
        }
    }
    private static void ValidateValue(string field, SettingValue value)
    {
        if (field == "priority" && value.Kind == SettingValueKind.DWord
            && uint.TryParse(value.Data, NumberStyles.None, CultureInfo.InvariantCulture, out var priority) && ProcessPriority.Known(priority)) return;
        if (field == "affinity" && value.Kind == SettingValueKind.String && value.Data.Length == 16
            && ulong.TryParse(value.Data, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var mask) && mask != 0) return;
        throw new InvalidDataException("Invalid CPU backup value.");
    }
    private sealed class Adapter(IGameProcessAccess access) : ISettingsAdapter
    {
        private readonly Dictionary<string, SettingValue> firstReadBaseline = new(StringComparer.Ordinal);
        public Task<OptimizationPlan> PrepareAsync(string tweakId, string? option, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (tweakId != "cpu-tuning" || option is null || option.Length > 4096) throw new InvalidDataException("Unknown CPU action.");
            var request = JsonSerializer.Deserialize<ProcessTuningRequest>(option) ?? throw new InvalidDataException("Missing CPU request.");
            request.Target.Validate();
            if (request.Priority is not (null or ProcessPriority.Normal or ProcessPriority.AboveNormal)
                || request.Priority is null && request.AffinityMask is null) throw new InvalidDataException("Choose a supported CPU adjustment.");
            var current = access.Read(request.Target);
            if (current != request.Expected) throw new IOException("CPU settings changed after inspection. Refresh and review again.");
            var targets = new List<SettingTarget>();
            if (request.Priority is uint priority) targets.Add(new(request.Target.SettingId("priority"), SettingValue.DWord((int)priority)));
            if (request.AffinityMask is ulong mask)
            {
                if (!current.CanSetAffinity || mask == 0 || (mask & ~current.SystemMask) != 0)
                    throw new IOException("Choose at least one supported logical processor. Core selection may be unavailable on this PC.");
                targets.Add(new(request.Target.SettingId("affinity"), Mask(mask)));
            }
            foreach (var target in targets)
                firstReadBaseline[target.SettingId] = GameProcessTarget.Parse(target.SettingId).Field == "priority"
                    ? SettingValue.DWord((int)current.Priority) : Mask(current.AffinityMask);
            return Task.FromResult(new OptimizationPlan(tweakId, targets, false));
        }
        public Task<SettingValue> ReadAsync(string id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var (target, field) = GameProcessTarget.Parse(id); var state = access.Read(target);
            if (field == "affinity" && !state.CanSetAffinity) throw new IOException(state.AffinityUnavailableReason ?? "CPU mask unavailable.");
            var value = field == "priority" ? SettingValue.DWord((int)state.Priority) : Mask(state.AffinityMask);
            if (firstReadBaseline.Remove(id, out var inspected) && value != inspected)
                throw new IOException("CPU settings changed before the backup. Refresh and review again; no write was attempted.");
            return Task.FromResult(value);
        }
        public Task WriteAsync(string id, SettingValue value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var (target, field) = GameProcessTarget.Parse(id); ValidateValue(field, value);
            access.Write(target, field, value); return Task.CompletedTask;
        }
        private static SettingValue Mask(ulong value) => SettingValue.String(value.ToString("X16", CultureInfo.InvariantCulture));
    }
}

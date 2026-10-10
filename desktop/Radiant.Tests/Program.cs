using System.Text.Json;
using Radiant.Core;

var tests = new (string Name, Func<Task> Run)[]
{
    ("durable original-value journal precedes every write", PrewriteJournal),
    ("journal failure prevents any setting write", JournalFailure),
    ("partial multi-value failure remains restorable", PartialFailure),
    ("permission failure is explicit and retains original values", PermissionFailure),
    ("missing original value is restored as missing, not zero", MissingValue),
    ("registry types and raw contents survive serialization and restoration", TypedValues),
    ("a target that already matches performs no write", Noop),
    ("external restore conflict is not overwritten", RestoreConflict),
    ("explicit forced restore returns the exact original value", ForcedRestore),
    ("change after snapshot is blocked before apply", ApplyRace),
    ("forced restore still blocks a later racing change", RestoreRace),
    ("write readback mismatch cannot report success", VerificationFailure),
    ("interrupted mixed application is classified without writes", InterruptedPartial),
    ("interrupted completed application is verified without writes", InterruptedApplied),
    ("interrupted unstarted application is detected without writes", InterruptedUnchanged),
    ("interrupted restoration is recoverable", InterruptedRestore),
    ("unreadable interrupted setting reports attention required", InterruptedUnreadable),
    ("cancelled application records already-applied values", CancelledApply),
    ("failed restoration can be retried", RestoreFailure),
    ("on-disk journal persists exact snapshots and locks another instance", FileJournalRoundtrip),
    ("corrupt journal prevents mutation", CorruptJournal),
    ("unknown action is rejected before journaling or mutation", UnknownAction),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed. All setting writes used in-memory adapters.");
return failures == 0 ? 0 : 1;

static (MemoryAdapter Adapter, MemoryJournal Journal, OptimizationEngine Engine) Fixture(int count = 1)
{
    var adapter = new MemoryAdapter(Enumerable.Range(0, count).Select(index => $"setting-{index}").ToArray());
    var journal = new MemoryJournal();
    return (adapter, journal, new OptimizationEngine(adapter, journal));
}

static async Task PrewriteJournal()
{
    var (adapter, journal, engine) = Fixture(2);
    adapter.BeforeWrite = (id, _) =>
    {
        Equal(1, journal.Entries.Count);
        var entry = journal.Entries.Values.Single();
        Equal(ChangeStatus.Pending, entry.Status);
        Equal(2, entry.Changes.Count);
        Equal(SettingValue.DWord(0), entry.Changes.Single(change => change.SettingId == id).Before);
        Equal("writing", entry.Changes.Single(change => change.SettingId == id).State);
    };
    Equal(ChangeStatus.Applied, (await engine.ApplyAsync("test")).Status);
    Equal(2, adapter.WriteCount);
}

static async Task JournalFailure()
{
    var (adapter, journal, engine) = Fixture();
    journal.SaveError = new IOException("disk is full");
    await Throws<IOException>(() => engine.ApplyAsync("test"));
    Equal(0, adapter.WriteCount);
    Equal(SettingValue.DWord(0), adapter.Values["setting-0"]);
}

static async Task PartialFailure()
{
    var (adapter, journal, engine) = Fixture(2);
    adapter.BeforeWrite = (id, _) => { if (id == "setting-1") throw new IOException("second value failed"); };
    var applied = await engine.ApplyAsync("test");
    Equal(ChangeStatus.Partial, applied.Status);
    Contains("second value failed", applied.Error);
    Equal(SettingValue.DWord(1), adapter.Values["setting-0"]);
    Equal(SettingValue.DWord(0), adapter.Values["setting-1"]);
    Equal(2, journal.Entries[applied.Id].Changes.Count);
    adapter.BeforeWrite = null;
    Equal(ChangeStatus.Restored, (await engine.RestoreAsync(applied.Id)).Status);
    Equal(SettingValue.DWord(0), adapter.Values["setting-0"]);
}

static async Task PermissionFailure()
{
    var (adapter, journal, engine) = Fixture();
    adapter.BeforeWrite = (_, _) => throw new UnauthorizedAccessException("policy denied the change");
    var record = await engine.ApplyAsync("test");
    Equal(ChangeStatus.Failed, record.Status);
    Contains("Permission denied", record.Error);
    Equal(SettingValue.DWord(0), journal.Entries[record.Id].Changes[0].Before);
}

static async Task MissingValue()
{
    var (adapter, journal, engine) = Fixture();
    adapter.Values["setting-0"] = SettingValue.Missing;
    var record = await engine.ApplyAsync("test");
    Equal(false, journal.Entries[record.Id].Changes[0].Before.Exists);
    Equal(ChangeStatus.Restored, (await engine.RestoreAsync(record.Id)).Status);
    Equal(SettingValue.Missing, adapter.Values["setting-0"]);
}

static async Task TypedValues()
{
    SettingValue[] originals =
    [
        SettingValue.Missing, SettingValue.DWord(0), SettingValue.DWord(int.MinValue),
        SettingValue.QWord(long.MaxValue), SettingValue.String(""),
        SettingValue.String("%USERPROFILE%\\a", expandable: true),
        SettingValue.MultiString(["alpha", "", "中文"]),
        SettingValue.Binary([0, 255, 10, 0]), SettingValue.Binary([5, 2], untyped: true),
        SettingValue.PowerPlan(Guid.NewGuid()),
    ];
    foreach (var original in originals)
    {
        var (adapter, journal, engine) = Fixture();
        adapter.Values["setting-0"] = original;
        var record = await engine.ApplyAsync("test");
        // Emulate a restart by round-tripping the journal, then use a fresh engine.
        var persisted = JsonSerializer.Deserialize<JournalEntry>(JsonSerializer.Serialize(journal.Entries[record.Id]))!;
        persisted.Validate();
        journal.Entries[record.Id] = persisted;
        var restarted = new OptimizationEngine(adapter, journal);
        Equal(ChangeStatus.Restored, (await restarted.RestoreAsync(record.Id)).Status);
        Equal(original, adapter.Values["setting-0"]);
    }
}

static async Task Noop()
{
    var (adapter, _, engine) = Fixture();
    adapter.Values["setting-0"] = SettingValue.DWord(1);
    var record = await engine.ApplyAsync("test");
    Equal(ChangeStatus.Unchanged, record.Status);
    Equal(0, adapter.WriteCount);
    Equal(ChangeStatus.Restored, (await engine.RestoreAsync(record.Id)).Status);
    Equal(0, adapter.WriteCount);
}

static async Task RestoreConflict()
{
    var (adapter, _, engine) = Fixture();
    var record = await engine.ApplyAsync("test");
    adapter.Values["setting-0"] = SettingValue.DWord(27);
    var restored = await engine.RestoreAsync(record.Id);
    Equal(ChangeStatus.Conflict, restored.Status);
    Equal(SettingValue.DWord(27), adapter.Values["setting-0"]);
    Equal(1, adapter.WriteCount);
    Contains("27", restored.Error);
}

static async Task ForcedRestore()
{
    var (adapter, _, engine) = Fixture();
    var record = await engine.ApplyAsync("test");
    adapter.Values["setting-0"] = SettingValue.String("external value");
    Equal(ChangeStatus.Restored, (await engine.RestoreAsync(record.Id, force: true)).Status);
    Equal(SettingValue.DWord(0), adapter.Values["setting-0"]);
}

static async Task ApplyRace()
{
    var (adapter, _, engine) = Fixture();
    adapter.BeforeRead = id => { if (adapter.ReadCount == 2) adapter.Values[id] = SettingValue.DWord(99); };
    Equal(ChangeStatus.Conflict, (await engine.ApplyAsync("test")).Status);
    Equal(0, adapter.WriteCount);
    Equal(SettingValue.DWord(99), adapter.Values["setting-0"]);
}

static async Task RestoreRace()
{
    var (adapter, _, engine) = Fixture();
    var record = await engine.ApplyAsync("test");
    adapter.Values["setting-0"] = SettingValue.DWord(8);
    var readsBefore = adapter.ReadCount;
    adapter.BeforeRead = id => { if (adapter.ReadCount == readsBefore + 2) adapter.Values[id] = SettingValue.DWord(99); };
    Equal(ChangeStatus.Conflict, (await engine.RestoreAsync(record.Id, force: true)).Status);
    Equal(SettingValue.DWord(99), adapter.Values["setting-0"]);
    Equal(1, adapter.WriteCount);
}

static async Task VerificationFailure()
{
    var (adapter, _, engine) = Fixture();
    adapter.AfterWrite = (id, _) => adapter.Values[id] = SettingValue.DWord(7);
    var record = await engine.ApplyAsync("test");
    True(record.Status != ChangeStatus.Applied, "A failed readback must not be labelled applied.");
    Contains("Verification failed", record.Error);
}

static async Task InterruptedPartial()
{
    var (adapter, journal, engine) = Fixture(2);
    var record = PendingEntry(2);
    journal.Entries[record.Id] = record;
    adapter.Values["setting-0"] = SettingValue.DWord(1);
    Equal(ChangeStatus.Partial, (await engine.ReadHistoryAsync()).Single().Status);
    Equal(0, adapter.WriteCount);
    Equal(ChangeStatus.Restored, (await engine.RestoreAsync(record.Id)).Status);
}

static async Task InterruptedApplied()
{
    var (adapter, journal, engine) = Fixture();
    var record = PendingEntry();
    journal.Entries[record.Id] = record;
    adapter.Values["setting-0"] = SettingValue.DWord(1);
    Equal(ChangeStatus.Applied, (await engine.ReadHistoryAsync()).Single().Status);
    Equal(0, adapter.WriteCount);
}

static async Task InterruptedUnchanged()
{
    var (adapter, journal, engine) = Fixture();
    var record = PendingEntry();
    journal.Entries[record.Id] = record;
    Equal(ChangeStatus.Interrupted, (await engine.ReadHistoryAsync()).Single().Status);
    Equal(0, adapter.WriteCount);
}

static async Task InterruptedRestore()
{
    var (adapter, journal, engine) = Fixture(2);
    var record = PendingEntry(2) with { Status = ChangeStatus.Restoring };
    journal.Entries[record.Id] = record;
    adapter.Values["setting-0"] = SettingValue.DWord(1);
    Equal(ChangeStatus.RestoreFailed, (await engine.ReadHistoryAsync()).Single().Status);
    Equal(0, adapter.WriteCount);
    Equal(ChangeStatus.Restored, (await engine.RestoreAsync(record.Id)).Status);
}

static async Task InterruptedUnreadable()
{
    var (adapter, journal, engine) = Fixture();
    var record = PendingEntry();
    journal.Entries[record.Id] = record;
    adapter.BeforeRead = _ => throw new IOException("setting unavailable");
    var recovered = (await engine.ReadHistoryAsync()).Single();
    Equal(ChangeStatus.NeedsAttention, recovered.Status);
    Contains("setting unavailable", recovered.Error);
    Equal(0, adapter.WriteCount);
}

static async Task CancelledApply()
{
    var (adapter, _, engine) = Fixture(2);
    using var cancellation = new CancellationTokenSource();
    adapter.AfterWrite = (_, _) => cancellation.Cancel();
    var record = await engine.ApplyAsync("test", cancellationToken: cancellation.Token);
    Equal(ChangeStatus.Partial, record.Status);
    Contains("cancelled", record.Error);
    adapter.AfterWrite = null;
    Equal(ChangeStatus.Restored, (await engine.RestoreAsync(record.Id)).Status);
}

static async Task RestoreFailure()
{
    var (adapter, journal, engine) = Fixture(2);
    var record = await engine.ApplyAsync("test");
    adapter.BeforeWrite = (id, _) => { if (id == "setting-0") throw new IOException("restore failed"); };
    Equal(ChangeStatus.RestoreFailed, (await engine.RestoreAsync(record.Id)).Status);
    Equal(SettingValue.DWord(0), adapter.Values["setting-1"]);
    Equal(SettingValue.DWord(1), adapter.Values["setting-0"]);
    Equal(SettingValue.DWord(0), journal.Entries[record.Id].Changes[0].Before);
    adapter.BeforeWrite = null;
    Equal(ChangeStatus.Restored, (await engine.RestoreAsync(record.Id)).Status);
}

static async Task FileJournalRoundtrip()
{
    await InTemporaryDirectory(async directory =>
    {
        var first = new FileChangeJournal(directory);
        var second = new FileChangeJournal(directory);
        var record = PendingEntry() with
        {
            Changes = [new SettingChange("setting-0", SettingValue.String("%TEMP%", expandable: true), SettingValue.DWord(1))],
        };
        await using (var lease = await first.AcquireAsync(default))
        {
            await first.SaveAsync(record, default);
            var parsed = (await first.ReadAllAsync(default)).Single();
            Equal(record.Id, parsed.Id);
            Equal(record.Changes[0].Before, parsed.Changes[0].Before);
            await Throws<IOException>(async () => { await using var blocked = await second.AcquireAsync(default); });
            await first.SaveAsync(record with { Status = ChangeStatus.Applied }, default);
        }
        await using (var lease = await second.AcquireAsync(default))
        {
            Equal(ChangeStatus.Applied, (await second.ReadAllAsync(default)).Single().Status);
        }
        Equal(0, Directory.EnumerateFiles(directory, "*.tmp").Count());
    });
}

static async Task CorruptJournal()
{
    await InTemporaryDirectory(async directory =>
    {
        await File.WriteAllTextAsync(Path.Combine(directory, $"{Guid.NewGuid():N}.json"), "{not valid json");
        var adapter = new MemoryAdapter(["setting-0"]);
        var engine = new OptimizationEngine(adapter, new FileChangeJournal(directory));
        await Throws<InvalidDataException>(() => engine.ApplyAsync("test"));
        Equal(0, adapter.WriteCount);
    });
}

static async Task UnknownAction()
{
    var (adapter, journal, engine) = Fixture();
    await Throws<ArgumentException>(() => engine.ApplyAsync("run-arbitrary-command"));
    Equal(0, journal.Entries.Count);
    Equal(0, adapter.WriteCount);
}

static JournalEntry PendingEntry(int count = 1) => new(1, Guid.NewGuid().ToString("N"), "test", DateTimeOffset.UtcNow, false,
    ChangeStatus.Pending, null,
    Enumerable.Range(0, count).Select(index => new SettingChange($"setting-{index}", SettingValue.DWord(0), SettingValue.DWord(1))).ToArray());

static async Task InTemporaryDirectory(Func<string, Task> action)
{
    var root = Path.GetFullPath(Path.GetTempPath());
    var directory = Path.Combine(root, $"RadiantCoreTests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try { await action(directory); }
    finally
    {
        // Delete only this exact, verified test-owned directory.
        var resolved = Path.GetFullPath(directory);
        if (resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(resolved).StartsWith("RadiantCoreTests-", StringComparison.Ordinal))
            Directory.Delete(resolved, recursive: true);
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
}

static void Contains(string expected, string? actual)
{
    if (actual?.Contains(expected, StringComparison.Ordinal) != true) throw new Exception($"Expected text containing {expected}, got {actual}.");
}

static void True(bool value, string message)
{
    if (!value) throw new Exception(message);
}

static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class MemoryAdapter(string[] ids) : ISettingsAdapter
{
    public Dictionary<string, SettingValue> Values { get; } = ids.ToDictionary(id => id, _ => SettingValue.DWord(0));
    public int WriteCount { get; private set; }
    public int ReadCount { get; private set; }
    public Action<string, SettingValue>? BeforeWrite { get; set; }
    public Action<string, SettingValue>? AfterWrite { get; set; }
    public Action<string>? BeforeRead { get; set; }

    public Task<OptimizationPlan> PrepareAsync(string tweakId, string? option, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tweakId != "test" || option is not null) throw new ArgumentException("Unknown action");
        return Task.FromResult(new OptimizationPlan("test", ids.Select(id => new SettingTarget(id, SettingValue.DWord(1))).ToArray(), false));
    }

    public Task<SettingValue> ReadAsync(string settingId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        BeforeRead?.Invoke(settingId);
        return Task.FromResult(Values[settingId]);
    }

    public Task WriteAsync(string settingId, SettingValue value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteCount++;
        BeforeWrite?.Invoke(settingId, value);
        Values[settingId] = value;
        AfterWrite?.Invoke(settingId, value);
        return Task.CompletedTask;
    }
}

sealed class MemoryJournal : IChangeJournal
{
    public Dictionary<string, JournalEntry> Entries { get; } = new();
    public Exception? SaveError { get; set; }
    public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken) => Task.FromResult<IAsyncDisposable>(new MemoryLease());
    public Task<IReadOnlyList<JournalEntry>> ReadAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<JournalEntry>>(Entries.Values.ToArray());
    }
    public Task SaveAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SaveError is not null) throw SaveError;
        entry.Validate();
        Entries[entry.Id] = entry;
        return Task.CompletedTask;
    }
    private sealed class MemoryLease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}

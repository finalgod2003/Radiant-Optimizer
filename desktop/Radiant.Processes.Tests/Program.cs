using Radiant.Core;
using Radiant.Windows;

var checks = new (string Name, Func<Task> Run)[] {
    ("durable originals exist before CPU writes; restart restores exactly", DurableRestore),
    ("backup failure blocks every CPU write", BackupFailure),
    ("preview race blocks writes", StalePreview),
    ("changes between inspection and backup also block writes", BeforeBackupRace),
    ("only bounded priority and nonempty supported affinity accepted", Bounds),
    ("process exit and PID reuse never tune a replacement", ProcessEnded),
    ("access denied stays outstanding instead of being treated as exit", AccessDenied),
    ("partial CPU change remains recoverable", PartialFailure),
    ("external values block restore and explicit force preserves them", ExternalConflict),
    ("newest first recovery restores the first original", OrderedRestore),
    ("native process roundtrip changes only this test process and restores", NativeRoundtrip)
};
var failures = 0;
foreach (var (name, run) in checks)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} CPU checks passed.");
return failures == 0 ? 0 : 1;

static async Task DurableRestore()
{
    using var f = new Fixture();
    f.Access.BeforeWrite = () => True(Directory.EnumerateFiles(f.Root, "*.original").Any());
    var result = await f.Service.ApplyAsync(f.Request()); Equal(ChangeStatus.Applied, result.Status);
    Equal(ProcessPriority.AboveNormal, f.Access.State.Priority); Equal(3UL, f.Access.State.AffinityMask);
    Equal(ChangeStatus.Restored, (await f.NewService().RestoreAllAsync()).Single().Status);
    Equal(f.Original, f.Access.State);
}
static async Task BackupFailure()
{
    using var f = new Fixture(); var blocked = Path.Combine(f.Root, "not-a-folder"); File.WriteAllText(blocked, "fixture");
    var service = new ProcessTuningService(new FileChangeJournal(blocked), f.Access);
    await Throws(() => service.ApplyAsync(f.Request())); Equal(0, f.Access.Writes); Equal(f.Original, f.Access.State);
}
static async Task StalePreview()
{
    using var f = new Fixture(); var request = f.Request();
    f.Access.State = f.Access.State with { Priority = ProcessPriority.AboveNormal };
    await Throws(() => f.Service.ApplyAsync(request)); Equal(0, f.Access.Writes);
}
static async Task Bounds()
{
    using var f = new Fixture();
    foreach (var request in new[] { f.Request() with { Priority = 256 }, f.Request() with { AffinityMask = 0 },
        f.Request() with { AffinityMask = 16 }, f.Request() with { Target = f.Access.Target with { Name = "explorer" } } })
        await Throws(() => f.Service.ApplyAsync(request));
    f.Access.State = f.Access.State with { AffinityUnavailableReason = "Multiple processor groups" };
    await Throws(() => f.Service.ApplyAsync(f.Request())); Equal(0, f.Access.Writes);
    Equal(ChangeStatus.Applied, (await f.Service.ApplyAsync(f.Request() with { AffinityMask = null })).Status);
}
static async Task BeforeBackupRace()
{
    using var f = new Fixture(); var request = f.Request();
    f.Access.BeforeRead = count => { if (count == 2) f.Access.State = f.Access.State with { Priority = ProcessPriority.AboveNormal }; };
    await Throws(() => f.Service.ApplyAsync(request)); Equal(0, f.Access.Writes);
    True(!Directory.EnumerateFiles(f.Root, "*.original").Any());
}
static async Task ProcessEnded()
{
    using var f = new Fixture(); await f.Service.ApplyAsync(f.Request()); var writes = f.Access.Writes;
    f.Access.Target = f.Access.Target with { CreatedFileTime = f.Access.Target.CreatedFileTime + 1 };
    var ended = (await f.NewService().ReadHistoryAsync()).Single();
    Equal(ProcessTuningService.EndedNotice, ended.Error); Equal(ChangeStatus.Restored, ended.Status);
    Equal(0, (await f.Service.RestoreAllAsync()).Count); Equal(writes, f.Access.Writes);
}
static async Task AccessDenied()
{
    using var f = new Fixture(); await f.Service.ApplyAsync(f.Request()); f.Access.Denied = true;
    var entry = (await f.Service.ReadHistoryAsync()).Single(); True(entry.Status != ChangeStatus.Restored);
    True((await f.Service.RestoreAllAsync()).Single().Status != ChangeStatus.Restored);
    f.Access.Denied = false; Equal(ChangeStatus.Restored, (await f.Service.RestoreAllAsync()).Single().Status);
}
static async Task PartialFailure()
{
    using var f = new Fixture(); f.Access.FailAffinity = true;
    var entry = await f.Service.ApplyAsync(f.Request()); Equal(ChangeStatus.Partial, entry.Status);
    f.Access.FailAffinity = false;
    Equal(ChangeStatus.Restored, (await f.NewService().RestoreAllAsync()).Single().Status); Equal(f.Original, f.Access.State);
}
static async Task ExternalConflict()
{
    using var f = new Fixture(); var change = await f.Service.ApplyAsync(f.Request());
    f.Access.State = f.Access.State with { AffinityMask = 7 };
    Equal(ChangeStatus.Conflict, (await f.Service.RestoreAllAsync()).Single().Status); Equal(7UL, f.Access.State.AffinityMask);
    Equal(ChangeStatus.Restored, (await f.Service.RestoreAsync(change.Id, true)).Status); Equal(f.Original, f.Access.State);
    True(Directory.EnumerateFiles(f.Root, "*.external*").Any());
}
static async Task OrderedRestore()
{
    using var f = new Fixture(); var first = await f.Service.ApplyAsync(f.Request());
    var second = await f.Service.ApplyAsync(f.Request() with { Priority = ProcessPriority.Normal, AffinityMask = 1 });
    await Throws(() => f.Service.RestoreAsync(first.Id));
    var restored = await f.NewService().RestoreAllAsync(); Equal(second.Id, restored[0].Id);
    True(restored.All(r => r.Status == ChangeStatus.Restored)); Equal(f.Original, f.Access.State);
}
static async Task NativeRoundtrip()
{
    var directory = Path.Combine(Path.GetTempPath(), "radiant-cpu-native-" + Guid.NewGuid().ToString("N"));
    var access = NativeGameProcesses.ForCurrentTestProcess(); var target = access.ListTargets().Single();
    await Throws(() => Task.FromResult(new NativeGameProcesses().Read(target)));
    var original = access.Read(target); var service = new ProcessTuningService(new FileChangeJournal(directory), access);
    try
    {
        ulong? chosenMask = original.CanSetAffinity ? original.AffinityMask & (~original.AffinityMask + 1UL) : null;
        var change = await service.ApplyAsync(new(target, original,
            original.Priority == ProcessPriority.AboveNormal ? ProcessPriority.Normal : ProcessPriority.AboveNormal, chosenMask));
        Equal(ChangeStatus.Applied, change.Status);
        True(Directory.EnumerateFiles(directory, "*.original").Any());
        service = new ProcessTuningService(new FileChangeJournal(directory), access);
        Equal(ChangeStatus.Restored, (await service.RestoreAllAsync()).Single().Status); Equal(original, access.Read(target));
    }
    finally
    {
        foreach (var entry in await service.ReadHistoryAsync())
            if (entry.Status != ChangeStatus.Restored) await service.RestoreAsync(entry.Id, true);
        Equal(original, access.Read(target));
        Console.WriteLine("Native backup retained: " + directory);
    }
}
static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
static async Task Throws(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Expected rejection."); }

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "radiant-cpu-tests-" + Guid.NewGuid().ToString("N"));
    public FakeAccess Access { get; } = new();
    public ProcessTuningState Original { get; }
    public ProcessTuningService Service { get; }
    public Fixture() { Directory.CreateDirectory(Root); Original = Access.State; Service = NewService(); }
    public ProcessTuningService NewService() => new(new FileChangeJournal(Root), Access);
    public ProcessTuningRequest Request() => new(Access.Target, Access.State, ProcessPriority.AboveNormal, 3);
    public void Dispose() { /* Keep bounded fixture evidence and originals until reviewed. */ }
}
sealed class FakeAccess : IGameProcessAccess
{
    public GameProcessTarget Target = new("cs2", 4242, 133000000000000000);
    public ProcessTuningState State = new(ProcessPriority.Normal, 15, 15, null);
    public int Writes; public int Reads; public bool Denied; public bool FailAffinity; public Action? BeforeWrite; public Action<int>? BeforeRead;
    public ProcessTuningState Read(GameProcessTarget target)
    {
        BeforeRead?.Invoke(++Reads);
        if (Denied) throw new UnauthorizedAccessException("Fixture access denied.");
        if (target != Target) throw new GameProcessEndedException();
        return State;
    }
    public bool HasExited(GameProcessTarget target)
    {
        if (Denied) throw new UnauthorizedAccessException("Fixture access denied.");
        return target != Target;
    }
    public void Write(GameProcessTarget target, string field, SettingValue value)
    {
        Read(target); BeforeWrite?.Invoke();
        if (field == "affinity" && FailAffinity) throw new IOException("Fixture affinity rejected.");
        Writes++;
        State = field == "priority" ? State with { Priority = uint.Parse(value.Data) }
            : State with { AffinityMask = Convert.ToUInt64(value.Data, 16) };
    }
}

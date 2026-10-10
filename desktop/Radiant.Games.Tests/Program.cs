using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Radiant.Games;

var checks = new (string Name, Func<Task> Run)[]
{
    ("CS2 both presets alter only validated VSync/MSAA and restore bytes", () => Roundtrip(GameId.CounterStrike2)),
    ("Fortnite both presets alter frame cap and supported existing quality", () => Roundtrip(GameId.Fortnite)),
    ("VALORANT both presets preserve sensitivity, binds and account settings", () => Roundtrip(GameId.Valorant)),
    ("UTF-8 BOM / UTF-16 LE/BE and mixed CRLF/LF survive exact restore", Encodings),
    ("unknown version rejected for all three adapters", Versions),
    ("required key missing prevents preview", MissingKey),
    ("duplicate and case-variant target keys fail closed", DuplicateKeys),
    ("duplicate sections and modifier commands fail closed", AmbiguousIni),
    ("unknown fields and repeated unrelated arrays remain byte-identical", UnknownSettings),
    ("malformed INI/KeyValues and directives rejected", Malformed),
    ("invalid encoding and oversized file rejected", InvalidBytes),
    ("no new config synthesized when original is missing", MissingFile),
    ("manual paths reject unrelated files, streams and network paths", Paths),
    ("read-only flag blocks inspect and mutation without changing flag", ReadOnly),
    ("hard links are rejected", HardLinks),
    ("directory reparse points are rejected", Reparse),
    ("bounded discovery finds all three game configs, never scans unrelated files", Discovery),
    ("caller-forged supported metadata cannot bypass parser", ForgedFile),
    ("unknown preset and foreign/reused preview rejected", PreviewOwnership),
    ("external edit after preview prevents any write", ApplyConflict),
    ("external edit during final check survives without overwrite", ApplyLateConflict),
    ("running game blocks apply and restore", RunningGame),
    ("game starting after backup blocks final replacement", GameStarts),
    ("backup failure performs no game write", BackupFailure),
    ("pending-journal failure performs no game write", JournalFailure),
    ("durable byte backup and pending record exist before replacement", DurableBeforeWrite),
    ("restore remains available after a new engine instance", RestartRestore),
    ("external restore conflict requires explicit force and archives changed bytes", RestoreConflict),
    ("a late force-restore race is not overwritten", RestoreLateConflict),
    ("corrupt backup and journal are rejected", Corruption),
    ("interrupted apply is classified from actual bytes without game writes", InterruptedApply),
    ("interrupted restore is classified from actual bytes without game writes", InterruptedRestore),
    ("failed restore can be retried", RestoreRetry),
    ("cancellation preserves restorable journal and original bytes", Cancellation),
    ("second engine cannot mutate while operation lock is held", OperationLock),
    ("no-op application does not replace original file", Noop),
    ("readback mismatch cannot report success and preserves original backup", ReadbackMismatch),
    ("supported legacy revision values work while optional qualities stay absent", SupportedRevisions),
    ("unreadable or missing process-state information blocks mutation", ProcessReadError),
    ("last-instant external replacement is captured and compensated", AtomicRace),
    ("a second external change during compensation is retained", AtomicSecondRace)
};
var failures = 0;
foreach (var (name, run) in checks)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed. Every configuration write was confined to generated temporary fixture directories.");
return failures == 0 ? 0 : 1;

static async Task Roundtrip(GameId game)
{
    foreach (var preset in new[] { "competitive", "balanced" })
    {
        using var fixture = new Fixture(game);
        var before = File.ReadAllBytes(fixture.Path);
        var preview = await fixture.Preview(preset);
        True(preview.Changes.Count > 0);
        var record = await fixture.Engine.ApplyAsync(preview);
        Equal(GameChangeStatus.Applied, record.Status);
        Equal(preview.TargetSha256, SafeFiles.Hash(File.ReadAllBytes(fixture.Path)));
        Equal(preview.OriginalSha256, SafeFiles.Hash(File.ReadAllBytes(fixture.Backup(record.Id))));
        if (game != GameId.CounterStrike2) Contains("MouseSensitivity=0.400000006", File.ReadAllText(fixture.Path));
        var restored = await fixture.Engine.RestoreAsync(record.Id);
        Equal(GameChangeStatus.Restored, restored.Status);
        Bytes(before, File.ReadAllBytes(fixture.Path));
    }
}
static async Task Encodings()
{
    foreach (var encoding in new Encoding[] { new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) })
    {
        using var f = new Fixture(GameId.Fortnite);
        var source = Fixture.Source(GameId.Fortnite).Replace("\n", "\r\n", StringComparison.Ordinal).Replace("; fixture\r\n", "; fixture\n", StringComparison.Ordinal).TrimEnd('\r', '\n');
        var before = encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray();
        File.WriteAllBytes(f.Path, before);
        var preview = await f.Preview();
        var record = await f.Engine.ApplyAsync(preview);
        var after = File.ReadAllBytes(f.Path);
        True(after.AsSpan().StartsWith(encoding.GetPreamble()));
        var expected = source;
        foreach (var change in preview.Changes) expected = expected.Replace(change.Key + "=" + change.Before, change.Key + "=" + change.After, StringComparison.Ordinal);
        Bytes(encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray(), after);
        await f.Engine.RestoreAsync(record.Id); Bytes(before, File.ReadAllBytes(f.Path));
    }
}
static async Task Versions()
{
    foreach (var game in Enum.GetValues<GameId>())
    {
        using var f = new Fixture(game);
        var source = File.ReadAllText(f.Path).Replace("\"Version\" \"16\"", "\"Version\" \"99\"", StringComparison.Ordinal)
            .Replace("FortniteReleaseVersion=14", "FortniteReleaseVersion=99", StringComparison.Ordinal).Replace("LocalSettingsVersion=7", "LocalSettingsVersion=99", StringComparison.Ordinal);
        File.WriteAllText(f.Path, source); var before = File.ReadAllBytes(f.Path);
        True(!(await f.Engine.InspectAsync(game, f.Path)).IsSupported);
        await Throws<GameProfileException>(() => f.Preview()); Bytes(before, File.ReadAllBytes(f.Path));
    }
}
static async Task MissingKey()
{
    using var f = new Fixture(GameId.Fortnite);
    File.WriteAllText(f.Path, File.ReadAllText(f.Path).Replace("FrameRateLimit=60.000000\n", "", StringComparison.Ordinal));
    await Throws<GameProfileException>(() => f.Preview());
}
static async Task DuplicateKeys()
{
    using var f = new Fixture(GameId.Fortnite);
    File.AppendAllText(f.Path, "\n");
    File.WriteAllText(f.Path, File.ReadAllText(f.Path).Replace("bUseVSync=True", "bUseVSync=True\nbusevsync=False", StringComparison.Ordinal));
    await Throws<GameProfileException>(() => f.Preview());
    using var cs = new Fixture(GameId.CounterStrike2);
    File.WriteAllText(cs.Path, File.ReadAllText(cs.Path).Replace("\"Version\" \"16\"", "\"Version\" \"16\"\n\"version\" \"15\"", StringComparison.Ordinal));
    await Throws<GameProfileException>(() => cs.Preview());
}
static async Task AmbiguousIni()
{
    using var f = new Fixture(GameId.Fortnite);
    File.AppendAllText(f.Path, "\n[ScalabilityGroups]\nsg.ShadowQuality=1");
    await Throws<GameProfileException>(() => f.Preview());
    File.WriteAllText(f.Path, Fixture.Source(GameId.Fortnite).Replace("bUseVSync=True", "bUseVSync=True\n+bUseVSync=False", StringComparison.Ordinal));
    await Throws<GameProfileException>(() => f.Preview());
}
static async Task UnknownSettings()
{
    using var f = new Fixture(GameId.Fortnite);
    var extra = "\n[Unknown]\n+SavedBinding=(Key=SpaceBar,Command=Jump)\n+SavedBinding=(Key=E,Command=Use)\nQuoted=\"雪;亮\"\nAccountOpaque=never-change\n";
    File.AppendAllText(f.Path, extra);
    var record = await f.Engine.ApplyAsync(await f.Preview());
    Equal(GameChangeStatus.Applied, record.Status);
    True(File.ReadAllText(f.Path).EndsWith(extra, StringComparison.Ordinal));
    Contains("ResolutionSizeX=2560", File.ReadAllText(f.Path));
}
static async Task Malformed()
{
    foreach (var source in new[] { "[oops\na=3", "a=2", "[ok]\nunassigned" })
    {
        using var f = new Fixture(GameId.Fortnite); File.WriteAllText(f.Path, source); await Throws<GameProfileException>(() => f.Preview());
    }
    foreach (var source in new[] { "#include \"other\"", "\"video.cfg\" { \"Version\" \"16\"", "\"video.cfg\" { \"nested\" { } }", Fixture.Source(GameId.CounterStrike2) + " trailing" })
    {
        using var f = new Fixture(GameId.CounterStrike2); File.WriteAllText(f.Path, source); await Throws<GameProfileException>(() => f.Preview());
    }
}
static async Task InvalidBytes()
{
    using var f = new Fixture(GameId.Fortnite);
    File.WriteAllBytes(f.Path, [0xff, 0xff, 0x80]); await Throws<GameProfileException>(() => f.Preview());
    File.WriteAllBytes(f.Path, new byte[SafeFiles.MaximumBytes + 1]); await Throws<GameProfileException>(() => f.Preview());
}
static async Task MissingFile()
{
    using var f = new Fixture(GameId.Fortnite); File.Delete(f.Path);
    await Throws<IOException>(() => f.Preview()); True(!File.Exists(f.Path)); Equal(0, (await f.Engine.ReadHistoryAsync()).Count);
}
static async Task Paths()
{
    using var f = new Fixture(GameId.Fortnite);
    foreach (var path in new[] { System.IO.Path.Combine(f.Root, "GameUserSettings.ini"), f.Path + ":other", "\\\\server\\share\\GameUserSettings.ini", "relative\\GameUserSettings.ini" })
        True(!(await f.Engine.InspectAsync(GameId.Fortnite, path)).IsSupported);
    True(!(await f.Engine.InspectAsync(GameId.Valorant, f.Path)).IsSupported);
}
static async Task ReadOnly()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    File.SetAttributes(f.Path, File.GetAttributes(f.Path) | FileAttributes.ReadOnly);
    try
    {
        True(!(await f.Engine.InspectAsync(f.Game, f.Path)).IsSupported);
        await Throws<GameProfileException>(() => f.Engine.ApplyAsync(preview));
        True((File.GetAttributes(f.Path) & FileAttributes.ReadOnly) != 0);
    }
    finally { File.SetAttributes(f.Path, FileAttributes.Normal); }
}
static async Task HardLinks()
{
    if (!OperatingSystem.IsWindows()) return;
    using var f = new Fixture(GameId.Fortnite);
    var other = System.IO.Path.Combine(f.Root, "other.ini");
    if (!Native.CreateHardLink(other, f.Path, IntPtr.Zero)) throw new IOException("Test hard link setup failed: " + Marshal.GetLastWin32Error());
    True(!(await f.Engine.InspectAsync(f.Game, f.Path)).IsSupported);
    await Throws<GameProfileException>(() => f.Preview());
}
static async Task Reparse()
{
    using var f = new Fixture(GameId.Fortnite);
    var folder = System.IO.Path.GetDirectoryName(f.Path)!;
    var moved = System.IO.Path.Combine(f.Root, "elsewhere");
    Directory.Move(folder, moved);
    try
    {
        Directory.CreateSymbolicLink(folder, moved);
        True(!(await f.Engine.InspectAsync(f.Game, f.Path)).IsSupported);
        await Throws<GameProfileException>(() => f.Preview());
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder); }
}
static async Task Discovery()
{
    using var f = new Fixture(GameId.Fortnite);
    foreach (var game in Enum.GetValues<GameId>()) f.Add(game);
    var unrelated = System.IO.Path.Combine(f.Root, "private", "GameUserSettings.ini");
    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(unrelated)!); File.WriteAllText(unrelated, "not a game");
    var files = await f.Engine.DiscoverAsync(); Equal(3, files.Count); True(files.All(v => v.IsSupported));
    True(files.All(v => v.Path != unrelated));
}
static async Task ForgedFile()
{
    using var f = new Fixture(GameId.Fortnite); File.WriteAllText(f.Path, "invalid");
    var fake = new GameConfigFile(f.Game, "wrong", f.Path, "trusted", true, null);
    await Throws<GameProfileException>(() => f.Engine.PreviewAsync(fake, "competitive"));
}
static async Task PreviewOwnership()
{
    using var f = new Fixture(GameId.Fortnite);
    await Throws<GameProfileException>(() => f.Preview("unlimited"));
    var preview = await f.Preview(); var second = f.NewEngine();
    await Throws<GameProfileException>(() => second.ApplyAsync(preview));
    await f.Engine.ApplyAsync(preview);
    await Throws<GameProfileException>(() => f.Engine.ApplyAsync(preview));
}
static async Task ApplyConflict()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    File.AppendAllText(f.Path, "; external\n"); var changed = File.ReadAllBytes(f.Path);
    await Throws<GameProfileException>(() => f.Engine.ApplyAsync(preview)); Bytes(changed, File.ReadAllBytes(f.Path));
    Equal(0, (await f.Engine.ReadHistoryAsync()).Count);
}
static async Task ApplyLateConflict()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    f.Engine.BeforeReplace = () => File.AppendAllText(f.Path, "; race\n");
    var record = await f.Engine.ApplyAsync(preview); Equal(GameChangeStatus.Failed, record.Status);
    Contains("; race", File.ReadAllText(f.Path)); Bytes(preview.Original, File.ReadAllBytes(f.Backup(record.Id)));
}
static async Task RunningGame()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    f.Guard.Blocked = true; await Throws<GameProfileException>(() => f.Engine.ApplyAsync(preview));
    f.Guard.Blocked = false; var record = await f.Engine.ApplyAsync(preview);
    f.Guard.Blocked = true; await Throws<GameProfileException>(() => f.Engine.RestoreAsync(record.Id));
}
static async Task GameStarts()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    f.Engine.BeforeReplace = () => f.Guard.Blocked = true;
    Equal(GameChangeStatus.Failed, (await f.Engine.ApplyAsync(preview)).Status); Bytes(preview.Original, File.ReadAllBytes(f.Path));
}
static async Task BackupFailure()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    f.Engine.BeforeBackup = () => throw new IOException("backup volume full");
    await Throws<IOException>(() => f.Engine.ApplyAsync(preview)); Bytes(preview.Original, File.ReadAllBytes(f.Path));
}
static async Task JournalFailure()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    f.Engine.BeforeJournalSave = _ => throw new IOException("journal volume full");
    await Throws<IOException>(() => f.Engine.ApplyAsync(preview)); Bytes(preview.Original, File.ReadAllBytes(f.Path));
}
static async Task DurableBeforeWrite()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    f.Engine.BeforeReplace = () =>
    {
        var folder = Directory.GetDirectories(f.Data).Single();
        Bytes(preview.Original, File.ReadAllBytes(System.IO.Path.Combine(folder, "original.bin")));
        Contains("\"Status\": \"Pending\"", File.ReadAllText(System.IO.Path.Combine(folder, "record.json")));
    };
    Equal(GameChangeStatus.Applied, (await f.Engine.ApplyAsync(preview)).Status);
}
static async Task RestartRestore()
{
    using var f = new Fixture(GameId.Valorant); var preview = await f.Preview();
    var record = await f.Engine.ApplyAsync(preview); var second = f.NewEngine();
    Equal(record.Id, (await second.ReadHistoryAsync()).Single().Id);
    Equal(GameChangeStatus.Restored, (await second.RestoreAsync(record.Id)).Status); Bytes(preview.Original, File.ReadAllBytes(f.Path));
}
static async Task RestoreConflict()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview(); var record = await f.Engine.ApplyAsync(preview);
    File.AppendAllText(f.Path, "; important external setting\n"); var external = File.ReadAllBytes(f.Path);
    Equal(GameChangeStatus.Conflict, (await f.Engine.RestoreAsync(record.Id)).Status); Bytes(external, File.ReadAllBytes(f.Path));
    Equal(GameChangeStatus.Restored, (await f.Engine.RestoreAsync(record.Id, true)).Status); Bytes(preview.Original, File.ReadAllBytes(f.Path));
    var saved = Directory.GetFiles(System.IO.Path.Combine(f.Data, record.Id), "before-restore-*.bin").Single(); Bytes(external, File.ReadAllBytes(saved));
}
static async Task RestoreLateConflict()
{
    using var f = new Fixture(GameId.Fortnite); var record = await f.Engine.ApplyAsync(await f.Preview());
    f.Engine.BeforeReplace = () => File.AppendAllText(f.Path, "; later external edit\n");
    Equal(GameChangeStatus.Failed, (await f.Engine.RestoreAsync(record.Id, true)).Status); Contains("; later external edit", File.ReadAllText(f.Path));
}
static async Task Corruption()
{
    using var f = new Fixture(GameId.Fortnite); var record = await f.Engine.ApplyAsync(await f.Preview());
    File.WriteAllText(f.Backup(record.Id), "corrupt"); await Throws<GameProfileException>(() => f.Engine.RestoreAsync(record.Id));
    File.WriteAllText(f.Journal(record.Id), "corrupt"); await Throws<GameProfileException>(() => f.Engine.RestoreAsync(record.Id));
}
static async Task InterruptedApply()
{
    foreach (var expected in new[] { GameChangeStatus.Applied, GameChangeStatus.Unchanged, GameChangeStatus.Conflict })
    {
        using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview(); var record = await f.Engine.ApplyAsync(preview);
        if (expected == GameChangeStatus.Unchanged) File.WriteAllBytes(f.Path, preview.Original);
        if (expected == GameChangeStatus.Conflict) File.AppendAllText(f.Path, "; external\n");
        var before = File.ReadAllBytes(f.Path); f.SetStatus(record.Id, "Pending");
        Equal(expected, (await f.NewEngine().ReadHistoryAsync()).Single().Status); Bytes(before, File.ReadAllBytes(f.Path));
    }
}
static async Task InterruptedRestore()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview(); var record = await f.Engine.ApplyAsync(preview);
    f.SetStatus(record.Id, "Restoring"); Equal(GameChangeStatus.Applied, (await f.NewEngine().ReadHistoryAsync()).Single().Status);
    await f.Engine.RestoreAsync(record.Id); f.SetStatus(record.Id, "Restoring");
    Equal(GameChangeStatus.Restored, (await f.NewEngine().ReadHistoryAsync()).Single().Status); Bytes(preview.Original, File.ReadAllBytes(f.Path));
}
static async Task RestoreRetry()
{
    using var f = new Fixture(GameId.Fortnite); var record = await f.Engine.ApplyAsync(await f.Preview());
    f.Engine.BeforeReplace = () => throw new IOException("sharing violation");
    Equal(GameChangeStatus.Failed, (await f.Engine.RestoreAsync(record.Id)).Status);
    f.Engine.BeforeReplace = null; Equal(GameChangeStatus.Restored, (await f.Engine.RestoreAsync(record.Id)).Status);
}
static async Task Cancellation()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview(); using var source = new CancellationTokenSource();
    f.Engine.BeforeReplace = source.Cancel;
    Equal(GameChangeStatus.Failed, (await f.Engine.ApplyAsync(preview, source.Token)).Status);
    Bytes(preview.Original, File.ReadAllBytes(f.Path)); Equal(1, (await f.Engine.ReadHistoryAsync()).Count);
}
static async Task OperationLock()
{
    using var f = new Fixture(GameId.Fortnite); var second = f.NewEngine();
    f.Engine.BeforeReplace = () => Throws<IOException>(() => second.ReadHistoryAsync()).GetAwaiter().GetResult();
    Equal(GameChangeStatus.Applied, (await f.Engine.ApplyAsync(await f.Preview())).Status);
}
static async Task Noop()
{
    using var f = new Fixture(GameId.Fortnite); await f.Engine.ApplyAsync(await f.Preview());
    var preview = await f.Preview(); Equal(0, preview.Changes.Count);
    var before = File.GetLastWriteTimeUtc(f.Path); f.Engine.BeforeReplace = () => throw new Exception("must not replace");
    Equal(GameChangeStatus.Unchanged, (await f.Engine.ApplyAsync(preview)).Status); Equal(before, File.GetLastWriteTimeUtc(f.Path));
}
static async Task ReadbackMismatch()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    f.Engine.AfterFileReplace = () => File.AppendAllText(f.Path, "; external overwrite after replace\n");
    var record = await f.Engine.ApplyAsync(preview); Equal(GameChangeStatus.Failed, record.Status);
    Contains("readback", record.Error!); Bytes(preview.Original, File.ReadAllBytes(f.Backup(record.Id)));
    Contains("; external overwrite", File.ReadAllText(f.Path));
}
static async Task SupportedRevisions()
{
    using var cs = new Fixture(GameId.CounterStrike2);
    File.WriteAllText(cs.Path, File.ReadAllText(cs.Path).Replace("\"Version\" \"16\"", "\"Version\" \"15\"", StringComparison.Ordinal));
    True((await cs.Preview()).Format.EndsWith("15", StringComparison.Ordinal));
    using var valorant = new Fixture(GameId.Valorant);
    File.WriteAllText(valorant.Path, File.ReadAllText(valorant.Path).Replace("LocalSettingsVersion=7", "LocalSettingsVersion=5", StringComparison.Ordinal)
        .Replace("EAresIntSettingName::MaterialQuality=2\n", "", StringComparison.Ordinal).Replace("EAresIntSettingName::DetailQuality=2\n", "", StringComparison.Ordinal));
    var preview = await valorant.Preview(); Equal(2, preview.Changes.Count);
    True(preview.Warnings.Any(w => w.Contains("absent", StringComparison.Ordinal)));
    Equal(GameChangeStatus.Applied, (await valorant.Engine.ApplyAsync(preview)).Status);
    True(!File.ReadAllText(valorant.Path).Contains("MaterialQuality", StringComparison.Ordinal));
}
static async Task ProcessReadError()
{
    using var f = new Fixture(GameId.Fortnite);
    var engine = new GameProfileEngine(f.Data, new GameDiscoveryRoots(f.Root, []), new UnknownProcessState());
    var preview = await engine.PreviewAsync(new(f.Game, "Fortnite", f.Path, "", true, null), "competitive");
    await Throws<GameProfileException>(() => engine.ApplyAsync(preview));
    Bytes(preview.Original, File.ReadAllBytes(f.Path));
}
static async Task AtomicRace()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    var external = Encoding.UTF8.GetBytes(Fixture.Source(f.Game) + "; first external\n");
    var displaced = System.IO.Path.Combine(f.Root, "displaced.bak");
    await Throws<GameProfileException>(() => Task.Run(() => SafeFiles.AtomicReplace(f.Path, preview.Target, preview.OriginalSha256, displaced,
        () => File.WriteAllBytes(f.Path, external))));
    Bytes(external, File.ReadAllBytes(f.Path)); Bytes(external, File.ReadAllBytes(displaced));
}
static async Task AtomicSecondRace()
{
    using var f = new Fixture(GameId.Fortnite); var preview = await f.Preview();
    var first = Encoding.UTF8.GetBytes(Fixture.Source(f.Game) + "; first external\n");
    var second = Encoding.UTF8.GetBytes(Fixture.Source(f.Game) + "; later external\n");
    var displaced = System.IO.Path.Combine(f.Root, "displaced.bak");
    await Throws<GameProfileException>(() => Task.Run(() => SafeFiles.AtomicReplace(f.Path, preview.Target, preview.OriginalSha256, displaced,
        () => File.WriteAllBytes(f.Path, first), () => File.WriteAllBytes(f.Path, second))));
    Bytes(second, File.ReadAllBytes(f.Path)); Bytes(first, File.ReadAllBytes(displaced));
}
static void True(bool condition) { if (!condition) throw new Exception("Expected true."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
static void Bytes(byte[] expected, byte[] actual) { if (!expected.AsSpan().SequenceEqual(actual)) throw new Exception("Byte sequences differ."); }
static void Contains(string needle, string value) { if (!value.Contains(needle, StringComparison.Ordinal)) throw new Exception("Missing " + needle); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
}

sealed class Guard : IGameProcessGuard
{
    public bool Blocked;
    public string? GetBlockingReason(GameId _) => Blocked ? "Test game is running." : null;
}
sealed class UnknownProcessState : IGameProcessGuard
{
    public string? GetBlockingReason(GameId _) => "Cannot verify whether the game is running.";
}
sealed class Fixture : IDisposable
{
    public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "radiant-games-tests-" + Guid.NewGuid().ToString("N"));
    public string Data => System.IO.Path.Combine(Root, "recovery");
    public string Path { get; }
    public GameId Game { get; }
    public Guard Guard { get; } = new();
    public GameProfileEngine Engine { get; }
    public Fixture(GameId game) { Game = game; Directory.CreateDirectory(Root); Path = Add(game); Engine = NewEngine(); }
    public GameProfileEngine NewEngine() => new(Data, new GameDiscoveryRoots(Root, [System.IO.Path.Combine(Root, "Steam")]), Guard);
    public string Backup(string id) => System.IO.Path.Combine(Data, id, "original.bin");
    public string Journal(string id) => System.IO.Path.Combine(Data, id, "record.json");
    public Task<GameProfilePreview> Preview(string preset = "competitive") => Engine.PreviewAsync(new(Game, GameProfiles.GetName(Game), Path, "", false, null), preset);
    public void SetStatus(string id, string status)
    {
        var node = JsonNode.Parse(File.ReadAllText(Journal(id)))!; node["Status"] = status; File.WriteAllText(Journal(id), node.ToJsonString());
    }
    public string Add(GameId game)
    {
        var path = game switch
        {
            GameId.CounterStrike2 => System.IO.Path.Combine(Root, "Steam", "userdata", "12345", "730", "local", "cfg", "cs2_video.txt"),
            GameId.Fortnite => System.IO.Path.Combine(Root, "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini"),
            GameId.Valorant => System.IO.Path.Combine(Root, "VALORANT", "Saved", "Config", "00000000-0000-0000-0000-000000000001-na", "Windows", "RiotUserSettings.ini"),
            _ => throw new ArgumentOutOfRangeException(nameof(game))
        };
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, Source(game), new UTF8Encoding(false)); return path;
    }
    public static string Source(GameId game) => game switch
    {
        GameId.CounterStrike2 => "\"video.cfg\"\n{\n\"Version\" \"16\"\n\"VendorID\" \"4318\"\n\"DeviceID\" \"1000\"\n\"setting.defaultres\" \"2560\"\n\"setting.defaultresheight\" \"1440\"\n\"setting.mat_vsync\" \"1\"\n\"setting.msaa_samples\" \"8\"\n\"setting.videocfg_dynamic_shadows\" \"1\"\n// fixture preserved\n}\n",
        GameId.Fortnite => "; fixture\n[/Script/FortniteGame.FortGameUserSettings]\nFortniteReleaseVersion=14\nbUseVSync=True\nFrameRateLimit=60.000000\nResolutionSizeX=2560\nResolutionSizeY=1440\nMouseSensitivity=0.400000006\n[ScalabilityGroups]\nsg.ResolutionQuality=100\nsg.ShadowQuality=3\nsg.TextureQuality=3\nsg.EffectsQuality=3\n",
        GameId.Valorant => "[Settings]\nLocalSettingsVersion=7\nEAresBoolSettingName::LimitFramerateAlways=False\nEAresFloatSettingName::MaxFramerateAlways=200.000000\nEAresIntSettingName::MaterialQuality=2\nEAresIntSettingName::DetailQuality=2\nEAresFloatSettingName::MouseSensitivity=0.400000006\nEAresBoolSettingName::HasEverAppliedRoamingSettings=True\nEAresStringSettingName::TestBinding=Space\n",
        _ => throw new ArgumentOutOfRangeException(nameof(game))
    };
    public void Dispose()
    {
        var full = System.IO.Path.GetFullPath(Root);
        var temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(full).StartsWith("radiant-games-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
static class Native
{
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateHardLink(string newFile, string existingFile, IntPtr securityAttributes);
}

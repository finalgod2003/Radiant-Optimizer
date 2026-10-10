using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radiant.Games;

/// <summary>Conservative file adapters. The caller owns licensing and explicit user consent.</summary>
public sealed partial class GameProfileEngine
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string directory;
    private readonly GameDiscoveryRoots roots;
    private readonly IGameProcessGuard processGuard;
    private readonly Guid owner = Guid.NewGuid();
    private readonly SemaphoreSlim gate = new(1, 1);
    internal Action? BeforeBackup { get; set; }
    internal Action? BeforeReplace { get; set; }
    internal Action<GameChangeRecord>? BeforeJournalSave { get; set; }
    internal Action? AfterFileReplace { get; set; }

    public GameProfileEngine(string dataDirectory) : this(dataDirectory, GameDiscoveryRoots.CurrentUser(), new WindowsGameProcessGuard()) { }

    public GameProfileEngine(string dataDirectory, GameDiscoveryRoots discoveryRoots, IGameProcessGuard processGuard)
    {
        directory = SafeFiles.LocalPath(dataDirectory);
        SafeFiles.AssertNoReparsePoints(directory);
        Directory.CreateDirectory(directory);
        SafeFiles.AssertNoReparsePoints(directory);
        roots = discoveryRoots ?? throw new ArgumentNullException(nameof(discoveryRoots));
        this.processGuard = processGuard ?? throw new ArgumentNullException(nameof(processGuard));
    }

    public Task<IReadOnlyList<GameConfigFile>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<GameConfigFile>>(() =>
    {
        var result = new List<GameConfigFile>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in GameDiscovery.Candidates(roots))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (found.Add(candidate.Path)) result.Add(Inspect(candidate.Game, candidate.Path));
        }
        return result.AsReadOnly();
    }, cancellationToken);

    public Task<GameConfigFile> InspectAsync(GameId game, string path, CancellationToken cancellationToken = default)
        => Task.Run(() => Inspect(game, path), cancellationToken);

    private static GameConfigFile Inspect(GameId game, string path)
    {
        var name = GameProfiles.GetName(game);
        try
        {
            var normalized = SafeFiles.ConfigPath(game, path);
            var bytes = SafeFiles.Read(normalized, true);
            var profile = GameProfiles.Transform(game, bytes, "competitive");
            return new(game, name, normalized, profile.Format, true, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return new(game, name, path, "Unsupported", false, error.Message); }
    }

    public Task<GameProfilePreview> PreviewAsync(GameConfigFile file, string presetId, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            ArgumentNullException.ThrowIfNull(file);
            var path = SafeFiles.ConfigPath(file.Game, file.Path);
            var original = SafeFiles.Read(path, true);
            var profile = GameProfiles.Transform(file.Game, original, presetId);
            var fresh = new GameConfigFile(file.Game, GameProfiles.GetName(file.Game), path, profile.Format, true, null);
            var warnings = profile.Warnings.ToList();
            var blocking = processGuard.GetBlockingReason(file.Game);
            if (blocking != null) warnings.Add(blocking);
            return new GameProfilePreview(owner, fresh, presetId, original, profile.Target, profile.Changes, warnings);
        }, cancellationToken);

    public Task<GameChangeRecord> ApplyAsync(GameProfilePreview preview, CancellationToken cancellationToken = default)
        => Exclusive(() => Apply(preview, cancellationToken), cancellationToken);

    private GameChangeRecord Apply(GameProfilePreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.Owner != owner || preview.Consumed || DateTimeOffset.UtcNow - preview.CreatedAt > TimeSpan.FromMinutes(15))
            throw new GameProfileException("Preview is foreign, already consumed or expired. Generate a new preview.");
        var path = SafeFiles.ConfigPath(preview.Game, preview.Path);
        EnsureGameClosed(preview.Game);
        var current = SafeFiles.Read(path, true);
        if (SafeFiles.Hash(current) != preview.OriginalSha256) throw new GameProfileException("Configuration changed after preview. Generate a new preview before applying.");
        var fresh = GameProfiles.Transform(preview.Game, current, preview.PresetId);
        if (SafeFiles.Hash(fresh.Target) != preview.TargetSha256 || fresh.Format != preview.Format)
            throw new GameProfileException("Preview no longer matches the supported profile.");
        cancellationToken.ThrowIfCancellationRequested();
        if (HistoryDirectories().Count >= 500) throw new GameProfileException("Recovery history reached 500 entries. Preserve and archive older recovery data before further changes.");
        var id = Guid.NewGuid().ToString("N");
        var folder = Folder(id);
        var now = DateTimeOffset.UtcNow;
        var record = new GameChangeRecord
        {
            Id = id, Game = preview.Game, GameName = preview.GameName, Path = path, PresetId = preview.PresetId,
            Format = preview.Format, CreatedAt = now, UpdatedAt = now, OriginalSha256 = preview.OriginalSha256,
            TargetSha256 = preview.TargetSha256, Status = GameChangeStatus.Pending, Changes = preview.Changes
        };
        BeforeBackup?.Invoke();
        Directory.CreateDirectory(folder); SafeFiles.AssertNoReparsePoints(folder);
        SafeFiles.WriteNewDurable(Path.Combine(folder, "original.bin"), current);
        SafeFiles.WriteNewDurable(Path.Combine(folder, "target.bin"), fresh.Target);
        if (SafeFiles.Hash(SafeFiles.Read(Path.Combine(folder, "original.bin"))) != record.OriginalSha256)
            throw new GameProfileException("Original backup verification failed; configuration was not changed.");
        Save(record); // Both exact byte copies and Pending journal are durable BEFORE any game-file replacement.
        preview.Consumed = true;
        if (preview.OriginalSha256 == preview.TargetSha256) return Save(record with { Status = GameChangeStatus.Unchanged });
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Replace(record.Game, path, fresh.Target, record.OriginalSha256, folder, cancellationToken);
            return Save(record with { Status = GameChangeStatus.Applied });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        { return Save(record with { Status = GameChangeStatus.Failed, Error = error.Message }); }
    }

    public Task<IReadOnlyList<GameChangeRecord>> ReadHistoryAsync(CancellationToken cancellationToken = default)
        => Exclusive<IReadOnlyList<GameChangeRecord>>(() =>
        {
            var result = new List<GameChangeRecord>();
            foreach (var folder in HistoryDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(Path.Combine(folder, "record.json"))) continue; // A prewrite backup failure never mutated a game file.
                var record = Load(Path.GetFileName(folder));
                if (record.Status is GameChangeStatus.Pending or GameChangeStatus.Restoring) record = Reconcile(record);
                result.Add(record);
            }
            return result.OrderByDescending(r => r.CreatedAt).ToArray();
        }, cancellationToken);

    public Task<GameChangeRecord> RestoreAsync(string changeId, bool force = false, CancellationToken cancellationToken = default)
        => Exclusive(() => RestoreOrdered(changeId, force, cancellationToken), cancellationToken);

    private GameChangeRecord Restore(string id, bool force, CancellationToken cancellationToken)
    {
        var record = Load(id);
        if (record.Status is GameChangeStatus.Restored or GameChangeStatus.Unchanged) return record;
        var path = SafeFiles.ConfigPath(record.Game, record.Path);
        EnsureGameClosed(record.Game);
        var folder = Folder(record.Id);
        var original = SafeFiles.Read(Path.Combine(folder, "original.bin"));
        if (SafeFiles.Hash(original) != record.OriginalSha256) throw new GameProfileException("Original backup is corrupt. Restore is blocked.");
        // Validate the archived original against the exact adapter, including its path/schema before restoring.
        if (GameProfiles.Transform(record.Game, original, record.PresetId).Format != record.Format)
            throw new GameProfileException("The recovery record does not match its original configuration format.");
        var current = SafeFiles.Read(path, true);
        var currentHash = SafeFiles.Hash(current);
        if (currentHash == record.OriginalSha256) return Save(record with { Status = GameChangeStatus.Restored, Error = null });
        if (currentHash != record.TargetSha256 && !force)
            return Save(record with { Status = GameChangeStatus.Conflict, Error = "Configuration has external changes. Nothing was overwritten. Review them before explicitly forcing recovery." });
        cancellationToken.ThrowIfCancellationRequested();
        // Forced restore also retains the exact external version, even when the caller re-confirms a conflict.
        var beforeRestore = Path.Combine(folder, "before-restore-" + currentHash + ".bin");
        if (!File.Exists(beforeRestore))
        {
            if (Directory.EnumerateFiles(folder, "before-restore-*.bin").Take(64).Count() >= 64)
                throw new GameProfileException("This record already preserves 64 distinct external versions. Archive its recovery data before forcing another different version.");
            SafeFiles.WriteNewDurable(beforeRestore, current);
        }
        else if (SafeFiles.Hash(SafeFiles.Read(beforeRestore)) != currentHash)
            throw new GameProfileException("The existing pre-restore backup is corrupt.");
        record = Save(record with { Status = GameChangeStatus.Restoring, RestoreExpectedSha256 = currentHash, Error = null });
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Replace(record.Game, path, original, currentHash, folder, cancellationToken);
            return Save(record with { Status = GameChangeStatus.Restored, Error = null });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        { return Save(record with { Status = GameChangeStatus.Failed, Error = error.Message }); }
    }

    private void Replace(GameId game, string path, byte[] target, string expectedHash, string folder, CancellationToken token)
    {
        var captured = Path.Combine(Path.GetDirectoryName(path)!, ".optimizer-" + Guid.NewGuid().ToString("N") + ".bak");
        try
        {
            SafeFiles.AtomicReplace(path, target, expectedHash, captured, () =>
            {
                BeforeReplace?.Invoke(); token.ThrowIfCancellationRequested(); EnsureGameClosed(game);
                // Recheck the path and bytes after process probing/hook, immediately before replace.
                SafeFiles.ConfigPath(game, path);
                if (SafeFiles.Hash(SafeFiles.Read(path, true)) != expectedHash)
                    throw new GameProfileException("Configuration changed immediately before replacement.");
            }, AfterFileReplace);
        }
        finally
        {
            if (File.Exists(captured))
            {
                // Capture lives on the game volume so atomic Replace works with Steam on another drive.
                // Retain its bytes durably in our journal before deleting our own generated sidecar.
                var bytes = SafeFiles.Read(captured);
                var hash = SafeFiles.Hash(bytes);
                var destination = Path.Combine(folder, "displaced-" + hash + ".bin");
                if (!File.Exists(destination)) SafeFiles.WriteNewDurable(destination, bytes);
                else if (SafeFiles.Hash(SafeFiles.Read(destination)) != hash)
                    throw new GameProfileException("The existing displaced-file backup is corrupt. The sidecar was retained beside the game configuration.");
                SafeFiles.AssertNoReparsePoints(captured); File.Delete(captured);
            }
        }
    }

    private GameChangeRecord Reconcile(GameChangeRecord record)
    {
        try
        {
            var hash = SafeFiles.Hash(SafeFiles.Read(SafeFiles.ConfigPath(record.Game, record.Path)));
            if (hash == record.OriginalSha256)
                return Save(record with { Status = record.Status == GameChangeStatus.Restoring ? GameChangeStatus.Restored : GameChangeStatus.Unchanged, Error = "Recovered an interrupted operation by reading the current file." });
            if (hash == record.TargetSha256)
                return Save(record with { Status = GameChangeStatus.Applied, Error = "Recovered an interrupted operation by reading the current file. Original backup is available." });
            return Save(record with { Status = GameChangeStatus.Conflict, Error = "Interrupted operation found external file contents. No automatic file writes were performed." });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Save(record with { Status = GameChangeStatus.Failed, Error = "Interrupted operation requires attention: " + error.Message }); }
    }

    private void EnsureGameClosed(GameId game)
    {
        var reason = processGuard.GetBlockingReason(game);
        if (reason != null) throw new GameProfileException(reason);
    }
    private async Task<T> Exclusive<T>(Func<T> operation, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                SafeFiles.AssertNoReparsePoints(directory);
                var lockPath = Path.Combine(directory, "engine.lock");
                SafeFiles.AssertNoReparsePoints(lockPath);
                using var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                token.ThrowIfCancellationRequested(); return operation();
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private string Folder(string id)
    {
        if (id.Length != 32 || !id.All(char.IsAsciiHexDigit)) throw new GameProfileException("Invalid recovery identifier.");
        var folder = Path.Combine(directory, id); SafeFiles.AssertNoReparsePoints(folder); return folder;
    }
    private List<string> HistoryDirectories()
        => Directory.EnumerateDirectories(directory).Where(path => Path.GetFileName(path).Length == 32 && Path.GetFileName(path).All(char.IsAsciiHexDigit)).Take(501).ToList();

    private GameChangeRecord Load(string id)
    {
        var data = SafeFiles.Read(Path.Combine(Folder(id), "record.json"));
        GameChangeRecord? record;
        try { record = JsonSerializer.Deserialize<GameChangeRecord>(data, Json); }
        catch (JsonException) { throw new GameProfileException("Recovery journal is corrupt. No game files will be changed."); }
        if (record == null || record.SchemaVersion != 1 || record.Id != id || !Enum.IsDefined(record.Game) || !Enum.IsDefined(record.Status)
            || !IsHash(record.OriginalSha256) || !IsHash(record.TargetSha256) || record.Changes == null || record.Changes.Count > 16)
            throw new GameProfileException("Invalid recovery journal. No game files will be changed.");
        SafeFiles.ConfigPath(record.Game, record.Path);
        return record;
    }
    private static bool IsHash(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private GameChangeRecord Save(GameChangeRecord record)
    {
        BeforeJournalSave?.Invoke(record);
        record = record with { UpdatedAt = DateTimeOffset.UtcNow };
        var file = Path.Combine(Folder(record.Id), "record.json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            SafeFiles.WriteNewDurable(temporary, JsonSerializer.SerializeToUtf8Bytes(record, Json));
            SafeFiles.AssertNoReparsePoints(file);
            if (File.Exists(file)) File.Replace(temporary, file, null, false); else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return record;
    }
}

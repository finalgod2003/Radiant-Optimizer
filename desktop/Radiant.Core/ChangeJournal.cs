using System.Text.Json;

namespace Radiant.Core;

public sealed record SettingChange(
    string SettingId,
    SettingValue Before,
    SettingValue After,
    string State = "pending",
    SettingValue? RestoreExpected = null);

public sealed record JournalEntry(
    int Version,
    string Id,
    string TweakId,
    DateTimeOffset CreatedAt,
    bool RestartRequired,
    string Status,
    string? Error,
    IReadOnlyList<SettingChange> Changes)
{
    public ChangeRecord ToRecord() => new(
        Id, TweakId,
        string.Join("; ", Changes.Select(change => Describe(change.SettingId, change.Before))),
        string.Join("; ", Changes.Select(change => Describe(change.SettingId, change.After))),
        Status, CreatedAt, Error, RestartRequired);

    private static string Describe(string id, SettingValue value) => id switch
    {
        "interface.client-animations" => "Interface animations: " + (value.Data == "1" ? "On" : "Off"),
        "interface.window-animations" => "Window transitions: " + (value.Data == "1" ? "On" : "Off"),
        "power.active-plan" => "Power plan: " + (value.Data switch
        {
            "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => "High performance",
            "381b4222-f694-41f0-9685-ff5bb260df2e" => "Balanced",
            "a1841308-3541-4fab-bc81-f71556f20b4a" => "Power saver",
            _ => value.Data,
        }),
        _ => $"{id}: {value.ToDisplayString()}",
    };

    public void Validate()
    {
        if (Version != 1 || !Guid.TryParseExact(Id, "N", out _) || string.IsNullOrWhiteSpace(TweakId)
            || Status is not (ChangeStatus.Pending or ChangeStatus.Applied or ChangeStatus.Unchanged
                or ChangeStatus.Failed or ChangeStatus.Partial or ChangeStatus.Conflict or ChangeStatus.Interrupted
                or ChangeStatus.NeedsAttention or ChangeStatus.Restoring or ChangeStatus.Restored or ChangeStatus.RestoreFailed)
            || Changes is null || Changes.Count == 0 || Changes.Count > 32
            || Changes.Any(change => change is null || string.IsNullOrWhiteSpace(change.SettingId))
            || Changes.Select(change => change.SettingId).Distinct(StringComparer.Ordinal).Count() != Changes.Count)
            throw new InvalidDataException("The saved operation has an invalid format.");
        foreach (var change in Changes)
        {
            if (change.Before is null || change.After is null)
                throw new InvalidDataException("The saved operation is missing its original values.");
            change.Before.Validate();
            change.After.Validate();
            change.RestoreExpected?.Validate();
        }
    }
}

public interface IChangeJournal
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<JournalEntry>> ReadAllAsync(CancellationToken cancellationToken);
    Task SaveAsync(JournalEntry entry, CancellationToken cancellationToken);
}

/// <summary>
/// A per-user write-ahead journal. A cross-process lock serializes operations;
/// fsync followed by same-directory replacement keeps earlier backups intact.
/// </summary>
public sealed partial class FileChangeJournal : IChangeJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string directory;

    public FileChangeJournal(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
    }

    public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        try
        {
            var stream = new FileStream(Path.Combine(directory, "journal.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
            return Task.FromResult<IAsyncDisposable>(stream);
        }
        catch (IOException error)
        {
            throw new IOException("Another optimizer instance is using the recovery journal. Close it and try again.", error);
        }
    }

    public async Task<IReadOnlyList<JournalEntry>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return [];
        var entries = new List<JournalEntry>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                if (info.Length > 1024 * 1024) throw new InvalidDataException("Recovery journal entry is too large.");
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                var entry = await JsonSerializer.DeserializeAsync<JournalEntry>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("Recovery journal entry is empty.");
                entry.Validate();
                if (!string.Equals(Path.GetFileNameWithoutExtension(file), entry.Id, StringComparison.Ordinal))
                    throw new InvalidDataException("Recovery journal identifier does not match the file name.");
                var originalPath = Path.Combine(directory, entry.Id + ".original");
                if (File.Exists(originalPath)) EnsureSameOriginal(await ReadOriginalAsync(originalPath, cancellationToken), entry);
                entries.Add(entry);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
            {
                throw new InvalidDataException($"Cannot read recovery entry {Path.GetFileName(file)}. No new settings will be changed. Keep the journal for recovery.", error);
            }
        }
        // A lost status file does not erase the write-once original. Treat it as
        // interrupted; the engine will inspect Windows without applying anything.
        foreach (var originalPath in Directory.EnumerateFiles(directory, "*.original"))
        {
            var original = await ReadOriginalAsync(originalPath, cancellationToken);
            if (entries.All(e => e.Id != original.Id)) entries.Add(original with { Status = ChangeStatus.Pending, Error = "Status file missing; original backup retained." });
        }
        return entries.OrderByDescending(entry => entry.CreatedAt).ToArray();
    }

    public async Task SaveAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        entry.Validate();
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"{entry.Id}.json");
        await EnsureOriginalAsync(entry, destination, cancellationToken);
        await PreserveExternalAsync(entry, cancellationToken);
        var temporary = Path.Combine(directory, $"{entry.Id}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

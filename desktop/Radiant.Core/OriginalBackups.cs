using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Radiant.Core;

public sealed partial class FileChangeJournal
{
    private sealed record OriginalEnvelope(string Sha256, string Data);

    private async Task PreserveExternalAsync(JournalEntry entry, CancellationToken token)
    {
        if (entry.Status != ChangeStatus.Restoring || !entry.Changes.Any(c => c.RestoreExpected is not null && c.RestoreExpected != c.Before && c.RestoreExpected != c.After)) return;
        var data = JsonSerializer.Serialize(entry, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
        var destination = Path.Combine(directory, entry.Id + "." + hash + ".external");
        if (File.Exists(destination)) return;
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new OriginalEnvelope(hash, data), JsonOptions, token);
                await stream.FlushAsync(token); stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task EnsureOriginalAsync(JournalEntry entry, string statePath, CancellationToken token)
    {
        var originalPath = Path.Combine(directory, entry.Id + ".original");
        if (!File.Exists(originalPath))
        {
            // Adopt existing 0.2 journals without altering their original values.
            var original = File.Exists(statePath)
                ? JsonSerializer.Deserialize<JournalEntry>(await File.ReadAllTextAsync(statePath, token), JsonOptions)
                    ?? throw new InvalidDataException("The earlier recovery record is invalid.")
                : entry;
            original.Validate();
            EnsureSameOriginal(original, entry);
            var data = JsonSerializer.Serialize(original, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new OriginalEnvelope(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data))), data), JsonOptions));
            var temp = originalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
                { await stream.WriteAsync(bytes, token); await stream.FlushAsync(token); stream.Flush(true); }
                token.ThrowIfCancellationRequested();
                File.Move(temp, originalPath, overwrite: false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        EnsureSameOriginal(await ReadOriginalAsync(originalPath, token), entry);
    }

    private static async Task<JournalEntry> ReadOriginalAsync(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Original backup exceeds the supported size.");
        try
        {
            var envelope = JsonSerializer.Deserialize<OriginalEnvelope>(await File.ReadAllTextAsync(path, token), JsonOptions)
                ?? throw new InvalidDataException("Original backup is empty.");
            if (envelope.Data is null || envelope.Sha256 != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Data))))
                throw new InvalidDataException("Original backup checksum does not match. Keep the file; no settings were changed.");
            var original = JsonSerializer.Deserialize<JournalEntry>(envelope.Data, JsonOptions) ?? throw new InvalidDataException("Original backup is invalid.");
            original.Validate();
            if (Path.GetFileNameWithoutExtension(path) != original.Id) throw new InvalidDataException("Original backup identity does not match its filename.");
            return original;
        }
        catch (JsonException error) { throw new InvalidDataException("Original backup is damaged. No settings were changed.", error); }
    }
    private static void EnsureSameOriginal(JournalEntry original, JournalEntry current)
    {
        if (original.Id != current.Id || original.Version != current.Version || original.TweakId != current.TweakId
            || original.CreatedAt != current.CreatedAt || original.RestartRequired != current.RestartRequired || original.Changes.Count != current.Changes.Count
            || original.Changes.Where((c, i) => c.SettingId != current.Changes[i].SettingId || c.Before != current.Changes[i].Before || c.After != current.Changes[i].After).Any())
            throw new InvalidDataException("Recovery status differs from its immutable original backup. No settings were changed.");
    }
}

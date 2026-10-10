namespace Radiant.Games;

public sealed partial class GameProfileEngine
{
    private static bool Outstanding(GameChangeRecord record)
        => record.Status is not (GameChangeStatus.Restored or GameChangeStatus.Unchanged);

    private GameChangeRecord[] RecoveryHistory(CancellationToken token)
    {
        var result = new List<GameChangeRecord>();
        foreach (var folder in HistoryDirectories())
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(Path.Combine(folder, "record.json"))) continue;
            var record = Load(Path.GetFileName(folder));
            if (record.Status is GameChangeStatus.Pending or GameChangeStatus.Restoring) record = Reconcile(record);
            result.Add(record);
        }
        return result.OrderByDescending(r => r.CreatedAt).ToArray();
    }

    private GameChangeRecord RestoreOrdered(string id, bool force, CancellationToken token)
    {
        var entries = RecoveryHistory(token);
        var record = entries.SingleOrDefault(r => r.Id == id) ?? throw new GameProfileException("Recovery record was not found.");
        if (!Outstanding(record)) return record;
        if (entries.Any(r => Outstanding(r) && r.CreatedAt > record.CreatedAt
            && string.Equals(r.Path, record.Path, StringComparison.OrdinalIgnoreCase)))
            throw new GameProfileException("A newer change affects this game file. Restore it first, or use Restore all changes.");
        return Restore(id, force, token);
    }

    /// <summary>One journal lease, newest first. A conflict blocks older records
    /// on that file without abandoning recovery of unrelated game files.</summary>
    public Task<IReadOnlyList<GameChangeRecord>> RestoreAllAsync(CancellationToken cancellationToken = default)
        => Exclusive<IReadOnlyList<GameChangeRecord>>(() =>
        {
            var entries = RecoveryHistory(cancellationToken); // Validate every record before any game-file write.
            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<GameChangeRecord>();
            foreach (var entry in entries.Where(Outstanding))
            {
                cancellationToken.ThrowIfCancellationRequested();
                GameChangeRecord result;
                if (blocked.Contains(entry.Path))
                    result = Save(entry with { Status = GameChangeStatus.Conflict,
                        Error = "A newer change to this file still needs recovery. Its conflict must be resolved first; this original remains backed up." });
                else
                {
                    try { result = Restore(entry.Id, false, cancellationToken); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        result = Save(entry with { Status = GameChangeStatus.Failed,
                            Error = error.Message + " Original backup retained; other game files will still be checked." });
                    }
                }
                if (result.Status != GameChangeStatus.Restored) blocked.Add(entry.Path);
                results.Add(result);
            }
            return results;
        }, cancellationToken);
}

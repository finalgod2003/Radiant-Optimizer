namespace Radiant.Core;

/// <summary>
/// Coordinates reversible changes without knowing registry paths or Windows APIs.
/// Restore intentionally has no dependency on an account or a paid entitlement.
/// </summary>
public sealed class OptimizationEngine(ISettingsAdapter adapter, IChangeJournal journal)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<ChangeRecord> ApplyAsync(string id, string? option = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await journal.AcquireAsync(cancellationToken);
            await RecoverInterruptedAsync(cancellationToken);
            var plan = await adapter.PrepareAsync(id, option, cancellationToken);
            if (plan.TweakId != id || plan.Targets.Count is 0 or > 32
                || plan.Targets.Select(target => target.SettingId).Distinct(StringComparer.Ordinal).Count() != plan.Targets.Count)
                throw new InvalidOperationException("The adapter returned an invalid change plan.");

            var changes = new List<SettingChange>();
            foreach (var target in plan.Targets)
            {
                target.Value.Validate();
                var before = await adapter.ReadAsync(target.SettingId, cancellationToken);
                before.Validate();
                changes.Add(new SettingChange(target.SettingId, before, target.Value));
            }

            var entry = new JournalEntry(1, Guid.NewGuid().ToString("N"), id, DateTimeOffset.UtcNow,
                plan.RestartRequired, ChangeStatus.Pending, null, changes);

            // This durable write is deliberately outside the mutation try/catch.
            // If the backup cannot be saved, no setting write is attempted.
            await journal.SaveAsync(entry, cancellationToken);
            if (entry.Changes.All(change => change.Before == change.After))
            {
                entry = entry with { Status = ChangeStatus.Unchanged };
                await journal.SaveAsync(entry, cancellationToken);
                return entry.ToRecord();
            }

            try
            {
                for (var index = 0; index < entry.Changes.Count; index++)
                {
                    var change = entry.Changes[index];
                    if (change.Before == change.After) continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    entry = WithChange(entry, index, change with { State = "writing" });
                    await journal.SaveAsync(entry, cancellationToken);
                    // Recheck after the durable write, immediately before the OS write.
                    var current = await adapter.ReadAsync(change.SettingId, cancellationToken);
                    if (current != change.Before)
                        throw new SettingConflictException(change.SettingId, current);
                    await adapter.WriteAsync(change.SettingId, change.After, cancellationToken);
                    var verified = await adapter.ReadAsync(change.SettingId, cancellationToken);
                    if (verified != change.After)
                        throw new IOException($"Verification failed for {change.SettingId}; the current value is {verified.ToDisplayString()}.");
                    entry = WithChange(entry, index, change with { State = "applied" });
                    await journal.SaveAsync(entry, cancellationToken);
                }

                // Detect changes to earlier values while a multi-value action was running.
                var final = await ObserveAsync(entry, cancellationToken);
                if (!final.AllAfter)
                    throw new IOException("The final verification did not match every saved target. Review the recovery record.");
                entry = entry with { Status = ChangeStatus.Applied };
                await journal.SaveAsync(entry, cancellationToken);
            }
            catch (Exception error)
            {
                var observed = await ObserveAsync(entry, CancellationToken.None);
                entry = entry with
                {
                    Status = observed.ReadError is not null ? ChangeStatus.NeedsAttention
                        : error is SettingConflictException || observed.HasExternalValue ? ChangeStatus.Conflict
                        : observed.AllBefore ? ChangeStatus.Failed : ChangeStatus.Partial,
                    Error = DescribeError(error) + (observed.ReadError is null ? "" : $" Verification also failed: {observed.ReadError}"),
                };
                // Never abandon the recovery record just because the UI cancelled.
                await journal.SaveAsync(entry, CancellationToken.None);
            }
            return entry.ToRecord();
        }
        finally { gate.Release(); }
    }

    public async Task<ChangeRecord> RestoreAsync(string changeId, bool force = false, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(changeId, "N", out _)) throw new ArgumentException("Invalid recovery identifier.", nameof(changeId));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await journal.AcquireAsync(cancellationToken);
            var entries = await RecoverInterruptedAsync(cancellationToken);
            var entry = entries.SingleOrDefault(candidate => candidate.Id == changeId)
                ?? throw new KeyNotFoundException("This change was not found in the recovery journal.");
            if (entry.Status == ChangeStatus.Restored) return entry.ToRecord();

            var changes = new List<SettingChange>();
            var conflicts = new List<string>();
            foreach (var change in entry.Changes)
            {
                var current = await adapter.ReadAsync(change.SettingId, cancellationToken);
                current.Validate();
                if (current != change.Before && current != change.After)
                    conflicts.Add($"{change.SettingId}: {current.ToDisplayString()}");
                changes.Add(change with { RestoreExpected = current, State = "restore_pending" });
            }

            if (conflicts.Count > 0 && !force)
            {
                entry = entry with
                {
                    Status = ChangeStatus.Conflict,
                    Error = $"Another program changed these values: {string.Join("; ", conflicts)}. Nothing was restored. Review the differences before choosing an explicit forced restore.",
                };
                await journal.SaveAsync(entry, cancellationToken);
                return entry.ToRecord();
            }

            entry = entry with { Changes = changes, Status = ChangeStatus.Restoring, Error = null };
            await journal.SaveAsync(entry, cancellationToken);
            try
            {
                // Reverse the order of a multi-value operation.
                for (var index = entry.Changes.Count - 1; index >= 0; index--)
                {
                    var change = entry.Changes[index];
                    if (change.RestoreExpected == change.Before) continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    entry = WithChange(entry, index, change with { State = "restoring" });
                    await journal.SaveAsync(entry, cancellationToken);
                    var current = await adapter.ReadAsync(change.SettingId, cancellationToken);
                    // Force authorizes the observed baseline, not a later racing change.
                    if (current != change.RestoreExpected)
                        throw new SettingConflictException(change.SettingId, current);
                    await adapter.WriteAsync(change.SettingId, change.Before, cancellationToken);
                    var verified = await adapter.ReadAsync(change.SettingId, cancellationToken);
                    if (verified != change.Before)
                        throw new IOException($"Restore verification failed for {change.SettingId}; the current value is {verified.ToDisplayString()}.");
                    entry = WithChange(entry, index, change with { State = "restored" });
                    await journal.SaveAsync(entry, cancellationToken);
                }

                var final = await ObserveAsync(entry, cancellationToken);
                if (!final.AllBefore)
                    throw new IOException("The final restore verification did not match every original value.");
                entry = entry with { Status = ChangeStatus.Restored };
                await journal.SaveAsync(entry, cancellationToken);
            }
            catch (Exception error)
            {
                var observed = await ObserveAsync(entry, CancellationToken.None);
                entry = entry with
                {
                    Status = error is SettingConflictException ? ChangeStatus.Conflict : ChangeStatus.RestoreFailed,
                    Error = DescribeError(error) + (observed.ReadError is null ? "" : $" Verification also failed: {observed.ReadError}")
                        + " Original values remain saved; review the record and retry restoration.",
                };
                await journal.SaveAsync(entry, CancellationToken.None);
            }
            return entry.ToRecord();
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<ChangeRecord>> ReadHistoryAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await journal.AcquireAsync(cancellationToken);
            var entries = await RecoverInterruptedAsync(cancellationToken);
            return entries.OrderByDescending(entry => entry.CreatedAt).Select(entry => entry.ToRecord()).ToArray();
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<JournalEntry>> RecoverInterruptedAsync(CancellationToken cancellationToken)
    {
        var entries = (await journal.ReadAllAsync(cancellationToken)).ToArray();
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            entry.Validate();
            if (entry.Status is not (ChangeStatus.Pending or ChangeStatus.Restoring)) continue;
            var observed = await ObserveAsync(entry, cancellationToken);
            var restoring = entry.Status == ChangeStatus.Restoring;
            entry = entry with
            {
                Status = observed.ReadError is not null ? ChangeStatus.NeedsAttention
                    : restoring && observed.AllBefore ? ChangeStatus.Restored
                    : observed.HasExternalValue ? ChangeStatus.Conflict
                    : restoring ? ChangeStatus.RestoreFailed
                    : observed.AllBefore ? ChangeStatus.Interrupted
                    : observed.AllAfter ? ChangeStatus.Applied : ChangeStatus.Partial,
                Error = observed.ReadError is not null
                    ? $"An interrupted operation could not be verified: {observed.ReadError}. Its original values remain saved."
                    : restoring
                        ? observed.AllBefore ? "Recovered an interrupted restoration; all original values were verified."
                            : "Restoration was interrupted. Review the saved original values and retry; recovery did not modify any setting."
                        : observed.AllBefore ? "The operation was interrupted; all values still match their original state."
                            : observed.AllAfter ? "Recovered an interrupted operation; all saved target values were verified."
                                : "The operation was interrupted with partial or external changes. Review this record; recovery did not modify any setting.",
            };
            await journal.SaveAsync(entry, cancellationToken);
            entries[index] = entry;
        }
        return entries;
    }

    private async Task<Observation> ObserveAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        var allBefore = true;
        var allAfter = true;
        var external = false;
        var errors = new List<string>();
        foreach (var change in entry.Changes)
        {
            try
            {
                var current = await adapter.ReadAsync(change.SettingId, cancellationToken);
                current.Validate();
                allBefore &= current == change.Before;
                allAfter &= current == change.After;
                external |= current != change.Before && current != change.After
                    && (entry.Status != ChangeStatus.Restoring || current != change.RestoreExpected);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                allBefore = false;
                allAfter = false;
                errors.Add($"{change.SettingId}: {error.Message}");
            }
        }
        return new Observation(allBefore, allAfter, external, errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private static JournalEntry WithChange(JournalEntry entry, int index, SettingChange change)
    {
        var changes = entry.Changes.ToArray();
        changes[index] = change;
        return entry with { Changes = changes };
    }

    private static string DescribeError(Exception error) => error switch
    {
        OperationCanceledException => "The operation was cancelled. Any earlier changes remain in this recovery record.",
        UnauthorizedAccessException => $"Permission denied: {error.Message}. No elevation was requested; use a permitted account or review administrator requirements.",
        _ => error.Message,
    };

    private sealed record Observation(bool AllBefore, bool AllAfter, bool HasExternalValue, string? ReadError);
    private sealed class SettingConflictException(string settingId, SettingValue current)
        : IOException($"{settingId} changed after the preview: {current.ToDisplayString()}. That value was not overwritten.");
}

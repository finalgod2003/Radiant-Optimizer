using System.Collections.ObjectModel;

namespace Radiant.Games;

public enum GameId { CounterStrike2, Fortnite, Valorant }
public enum GameChangeStatus { Pending, Applied, Restoring, Restored, Unchanged, Conflict, Failed }
public sealed record GamePreset(string Id, string Name, string Description);
public sealed record GameConfigFile(GameId Game, string GameName, string Path, string Format, bool IsSupported, string? Reason);
public sealed record GameSettingChange(string Section, string Key, string Before, string After);

/// <summary>An engine-created, immutable preview. Callers cannot provide replacement bytes.</summary>
public sealed class GameProfilePreview
{
    internal GameProfilePreview(Guid owner, GameConfigFile file, string presetId, byte[] original, byte[] target,
        IReadOnlyList<GameSettingChange> changes, IReadOnlyList<string> warnings)
    {
        Owner = owner; Original = original; Target = target; Game = file.Game; GameName = file.GameName;
        Path = file.Path; Format = file.Format; PresetId = presetId;
        OriginalSha256 = SafeFiles.Hash(original); TargetSha256 = SafeFiles.Hash(target);
        Changes = new ReadOnlyCollection<GameSettingChange>(changes.ToArray());
        Warnings = new ReadOnlyCollection<string>(warnings.ToArray()); CreatedAt = DateTimeOffset.UtcNow;
    }
    internal Guid Owner { get; }
    internal byte[] Original { get; }
    internal byte[] Target { get; }
    internal bool Consumed { get; set; }
    public GameId Game { get; }
    public string GameName { get; }
    public string Path { get; }
    public string Format { get; }
    public string PresetId { get; }
    public DateTimeOffset CreatedAt { get; }
    public string OriginalSha256 { get; }
    public string TargetSha256 { get; }
    public IReadOnlyList<GameSettingChange> Changes { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public sealed record GameChangeRecord
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required GameId Game { get; init; }
    public required string GameName { get; init; }
    public required string Path { get; init; }
    public required string Format { get; init; }
    public required string PresetId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public required string OriginalSha256 { get; init; }
    public required string TargetSha256 { get; init; }
    public string? RestoreExpectedSha256 { get; init; }
    public required GameChangeStatus Status { get; init; }
    public string? Error { get; init; }
    public required IReadOnlyList<GameSettingChange> Changes { get; init; }
}

public interface IGameProcessGuard
{
    /// <returns>Null when it is safe to edit; a blocking reason otherwise.</returns>
    string? GetBlockingReason(GameId game);
}

public sealed class GameProfileException(string message) : IOException(message);

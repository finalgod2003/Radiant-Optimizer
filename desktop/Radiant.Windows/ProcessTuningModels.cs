using System.Globalization;
using Radiant.Core;

namespace Radiant.Windows;

public sealed record GameProcessTarget(string Name, int Pid, long CreatedFileTime)
{
    public string GameName => Name switch { "cs2" => "Counter-Strike 2", "fortniteclient-win64-shipping" => "Fortnite",
        "valorant-win64-shipping" => "VALORANT", _ => Name };
    public override string ToString() => $"{GameName} · PID {Pid}";
    internal void Validate()
    {
        if (Name is not ("cs2" or "fortniteclient-win64-shipping" or "valorant-win64-shipping")
            || Pid <= 0 || CreatedFileTime <= 0)
            throw new InvalidDataException("Unsupported game process identity.");
    }
    internal string SettingId(string field) => $"cpu-v1:{Name}:{Pid.ToString(CultureInfo.InvariantCulture)}:{CreatedFileTime.ToString(CultureInfo.InvariantCulture)}:{field}";
    internal static (GameProcessTarget Target, string Field) Parse(string id)
    {
        var parts = id.Split(':');
        if (parts.Length != 5 || parts[0] != "cpu-v1" || parts[4] is not ("priority" or "affinity")
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var created))
            throw new InvalidDataException("Invalid CPU recovery identity.");
        var target = new GameProcessTarget(parts[1], pid, created); target.Validate();
        if (target.SettingId(parts[4]) != id) throw new InvalidDataException("Noncanonical CPU identity.");
        return (target, parts[4]);
    }
}

public static class ProcessPriority
{
    public const uint Normal = 0x20, AboveNormal = 0x8000;
    public static string Label(uint value) => value switch { 0x40 => "Idle", 0x4000 => "Below normal", Normal => "Normal",
        AboveNormal => "Above normal", 0x80 => "High", 0x100 => "Realtime", _ => "Unknown" };
    internal static bool Known(uint value) => value is 0x40 or 0x4000 or Normal or AboveNormal or 0x80 or 0x100;
}
public sealed record ProcessTuningState(uint Priority, ulong AffinityMask, ulong SystemMask, string? AffinityUnavailableReason)
{
    public bool CanSetAffinity => AffinityUnavailableReason is null && AffinityMask != 0 && SystemMask != 0;
    public static string CpuList(ulong mask) => string.Join(", ", Enumerable.Range(0, 64).Where(i => (mask & (1UL << i)) != 0));
}
public sealed record ProcessTuningRequest(GameProcessTarget Target, ProcessTuningState Expected, uint? Priority, ulong? AffinityMask);
public sealed class GameProcessEndedException() : IOException("This game process has ended. No replacement process will be changed.");

public interface IGameProcessAccess
{
    ProcessTuningState Read(GameProcessTarget target);
    bool HasExited(GameProcessTarget target);
    void Write(GameProcessTarget target, string field, SettingValue value);
}

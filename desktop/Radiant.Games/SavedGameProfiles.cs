using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radiant.Games;

public sealed record SavedGameProfile(int Version, string Id, string Name, string PresetId, DateTimeOffset CreatedAt);

/// <summary>Immutable named copies; saving never touches the installed game.</summary>
public sealed class SavedGameProfiles
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly string directory;
    public SavedGameProfiles(string directory)
    {
        this.directory = SafeFiles.LocalPath(directory);
        SafeFiles.AssertNoReparsePoints(this.directory);
        Directory.CreateDirectory(this.directory);
    }

    public SavedGameProfile Save(string name, CustomGameProfile profile)
    {
        name = name.Trim(); ValidateName(name);
        var id = Guid.NewGuid().ToString("N");
        var saved = new SavedGameProfile(1, id, name, CustomGameProfiles.Encode(profile), DateTimeOffset.UtcNow);
        using var lease = Acquire();
        if (Files().Length >= 200) throw new GameProfileException("200 saved profiles reached. Archive older profile files before saving more.");
        SafeFiles.WriteNewDurable(Path.Combine(directory, id + ".json"), JsonSerializer.SerializeToUtf8Bytes(saved, Json));
        return saved;
    }

    public IReadOnlyList<SavedGameProfile> List(GameId game)
    {
        using var lease = Acquire();
        var result = new List<SavedGameProfile>();
        foreach (var path in Files())
        {
            try
            {
                var bytes = SafeFiles.Read(path);
                if (bytes.Length > 4096) throw new GameProfileException("Saved profile is too large.");
                var saved = JsonSerializer.Deserialize<SavedGameProfile>(bytes, Json);
                if (saved is null || saved.Version != 1 || !Guid.TryParseExact(saved.Id, "N", out _)
                    || saved.Id != Path.GetFileNameWithoutExtension(path)) throw new GameProfileException("Invalid saved profile.");
                ValidateName(saved.Name);
                if (CustomGameProfiles.Decode(saved.PresetId).Game == game) result.Add(saved);
            }
            catch (Exception e) when (e is JsonException or ArgumentException)
            { throw new GameProfileException("A saved profile is corrupt. No game settings were changed."); }
        }
        return result.OrderByDescending(p => p.CreatedAt).ToArray();
    }

    private string[] Files()
    {
        var files = Directory.EnumerateFiles(directory, "*.json").Take(201).ToArray();
        if (files.Length > 200) throw new GameProfileException("Too many saved profiles. Archive older files first.");
        return files;
    }
    private FileStream Acquire()
    {
        SafeFiles.AssertNoReparsePoints(directory);
        var path = Path.Combine(directory, "profiles.lock"); SafeFiles.AssertNoReparsePoints(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60 || name.Any(char.IsControl))
            throw new GameProfileException("Use a profile name between 1 and 60 characters without control characters.");
    }
}

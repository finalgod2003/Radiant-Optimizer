using System.Globalization;

namespace Radiant.Games;

public sealed record CustomGameProfile(GameId Game, bool? VSync = null, int? Msaa = null, int? FrameCap = null,
    int? ShadowQuality = null, int? TextureQuality = null, int? EffectsQuality = null,
    int? PostProcessQuality = null, int? FoliageQuality = null, int? MaterialQuality = null, int? DetailQuality = null);

/// <summary>Versioned, finite option grammar retained with each recovery record.
/// Changing or deleting a saved profile never changes an older transformation.</summary>
public static class CustomGameProfiles
{
    private const string FortniteSection = "/Script/FortniteGame.FortGameUserSettings";
    public static string Encode(CustomGameProfile profile)
    {
        Validate(profile);
        static string N(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";
        return string.Join(':', "custom-v1", ((int)profile.Game).ToString(CultureInfo.InvariantCulture),
            profile.VSync is bool sync ? sync ? "1" : "0" : "-", N(profile.Msaa), N(profile.FrameCap),
            N(profile.ShadowQuality), N(profile.TextureQuality), N(profile.EffectsQuality), N(profile.PostProcessQuality),
            N(profile.FoliageQuality), N(profile.MaterialQuality), N(profile.DetailQuality));
    }

    public static CustomGameProfile Decode(string id)
    {
        if (id is null || id.Length > 160) throw new GameProfileException("Invalid custom profile identifier.");
        var fields = id.Split(':');
        if (fields.Length != 12 || fields[0] != "custom-v1" || fields[1] is not ("0" or "1" or "2")
            || fields[2] is not ("-" or "0" or "1")) throw new GameProfileException("Unknown custom profile version or options.");
        int? N(int index) => fields[index] == "-" ? null
            : int.TryParse(fields[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value
            : throw new GameProfileException("Invalid numeric profile option.");
        var profile = new CustomGameProfile((GameId)N(1)!.Value,
            fields[2] == "-" ? null : fields[2] == "1", N(3), N(4), N(5), N(6), N(7), N(8), N(9), N(10), N(11));
        if (Encode(profile) != id) throw new GameProfileException("Noncanonical custom profile options.");
        return profile;
    }

    private static void Validate(CustomGameProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var fortnite = new[] { p.ShadowQuality, p.TextureQuality, p.EffectsQuality, p.PostProcessQuality, p.FoliageQuality };
        if (!Enum.IsDefined(p.Game) || p.FrameCap is < 30 or > 1000 || p.Msaa is not (null or 0 or 2 or 4 or 8)
            || fortnite.Any(v => v is < 0 or > 4) || p.MaterialQuality is < 0 or > 2 || p.DetailQuality is < 0 or > 2)
            throw new GameProfileException("Custom settings are outside their supported range.");
        if ((p.Game != GameId.CounterStrike2 && p.Msaa is not null)
            || (p.Game == GameId.CounterStrike2 && p.FrameCap is not null)
            || (p.Game == GameId.Valorant && p.VSync is not null)
            || (p.Game != GameId.Fortnite && fortnite.Any(v => v is not null))
            || (p.Game != GameId.Valorant && (p.MaterialQuality is not null || p.DetailQuality is not null)))
            throw new GameProfileException("This option is not supported for the selected game.");
        if (p.VSync is null && p.Msaa is null && p.FrameCap is null && fortnite.All(v => v is null)
            && p.MaterialQuality is null && p.DetailQuality is null)
            throw new GameProfileException("Choose at least one custom setting; the others stay unchanged.");
    }

    internal static (string Format, byte[] Target, IReadOnlyList<GameSettingChange> Changes, IReadOnlyList<string> Warnings)
        Transform(GameId game, byte[] bytes, string id)
    {
        var p = Decode(id);
        if (p.Game != game) throw new GameProfileException("This custom profile belongs to another game.");
        // Keep the same strict schema/value checks as our tested built-in adapters.
        var validated = GameProfiles.Transform(game, bytes, "performance");
        var document = game == GameId.CounterStrike2 ? ConfigDocument.ValveVideo(bytes) : ConfigDocument.Ini(bytes);
        var changes = new List<GameSettingChange>();
        var warnings = new List<string> {
            "Only selected settings change. Close the game before applying or restoring. Cloud sync may replace local settings.",
            "Higher quality can increase GPU work; lower quality can reduce detail. An FPS cap is a ceiling, not a promised frame rate. Compare results on your own PC." };
        void Optional(string section, string key, int? value)
        {
            if (value is null) return;
            if (!document.Has(section, key)) { warnings.Add(key + " is absent and will not be inserted."); return; }
            document.Set(section, key, value.Value.ToString(CultureInfo.InvariantCulture), changes);
        }
        switch (game)
        {
            case GameId.CounterStrike2:
                if (p.VSync is bool csSync) document.Set("video.cfg", "setting.mat_vsync", csSync ? "1" : "0", changes);
                Optional("video.cfg", "setting.msaa_samples", p.Msaa);
                break;
            case GameId.Fortnite:
                if (p.VSync is bool fnSync) document.Set(FortniteSection, "bUseVSync", fnSync ? "True" : "False", changes);
                if (p.FrameCap is int fnCap) document.Set(FortniteSection, "FrameRateLimit", fnCap.ToString("F6", CultureInfo.InvariantCulture), changes);
                Optional("ScalabilityGroups", "sg.ShadowQuality", p.ShadowQuality);
                Optional("ScalabilityGroups", "sg.TextureQuality", p.TextureQuality);
                Optional("ScalabilityGroups", "sg.EffectsQuality", p.EffectsQuality);
                Optional("ScalabilityGroups", "sg.PostProcessQuality", p.PostProcessQuality);
                Optional("ScalabilityGroups", "sg.FoliageQuality", p.FoliageQuality);
                break;
            case GameId.Valorant:
                if (p.FrameCap is int valCap)
                {
                    document.Set("Settings", "EAresBoolSettingName::LimitFramerateAlways", "True", changes);
                    document.Set("Settings", "EAresFloatSettingName::MaxFramerateAlways", valCap.ToString("F6", CultureInfo.InvariantCulture), changes);
                }
                Optional("Settings", "EAresIntSettingName::MaterialQuality", p.MaterialQuality);
                Optional("Settings", "EAresIntSettingName::DetailQuality", p.DetailQuality);
                break;
        }
        return (validated.Format, document.Render(), changes.AsReadOnly(), warnings.AsReadOnly());
    }
}

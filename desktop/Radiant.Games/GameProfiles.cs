using System.Globalization;

namespace Radiant.Games;

public static class GameProfiles
{
    private const string FortniteSection = "/Script/FortniteGame.FortGameUserSettings";
    public static string GetName(GameId game) => game switch
    {
        GameId.CounterStrike2 => "Counter-Strike 2", GameId.Fortnite => "Fortnite", GameId.Valorant => "VALORANT",
        _ => throw new ArgumentOutOfRangeException(nameof(game))
    };
    public static IReadOnlyList<GamePreset> GetPresets(GameId game)
        => PerformancePresets(game).Concat(LegacyPresets(game)).ToArray();

    public static bool IsFreePreset(GameId game, string id) => PerformancePresets(game).Any(p => p.Id == id);

    private static IReadOnlyList<GamePreset> PerformancePresets(GameId game) => game switch
    {
        GameId.CounterStrike2 => [new("performance", "Performance · Free", "VSync off; MSAA off to reduce GPU work. Edges can look rougher. Resolution, shadows and input remain unchanged.")],
        GameId.Fortnite or GameId.Valorant => new[] { new GamePreset("performance", "Performance · keep current cap · Free",
            game == GameId.Fortnite ? "VSync off; reduce existing shadow, effects, postprocess and foliage quality. Keep the current FPS cap, resolution and input settings."
                : "Lower existing material and detail quality. Keep the current FPS limit, resolution, input and account settings.") }.Concat(new[] { 60, 90, 120, 144, 165, 240, 360 }
            .Select(cap => new GamePreset($"performance-{cap}", $"Performance · {cap} FPS · Free",
                game == GameId.Fortnite
                    ? $"{cap} FPS cap; VSync off; lower existing shadow, effects, postprocess and foliage settings. Textures set to Low. Resolution and input are preserved."
                    : $"{cap} FPS always cap; lower existing material and detail settings. Input, resolution and account settings are preserved."))).ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(game))
    };

    private static IReadOnlyList<GamePreset> LegacyPresets(GameId game) => game switch
    {
        GameId.CounterStrike2 => Array.AsReadOnly(new[]
        {
            new GamePreset("competitive", "Competitive", "VSync off; 2x MSAA. Frame cap and all other graphics settings stay unchanged."),
            new GamePreset("balanced", "Balanced", "VSync on; 4x MSAA. Frame cap and all other graphics settings stay unchanged.")
        }),
        GameId.Fortnite => Array.AsReadOnly(new[]
        {
            new GamePreset("competitive", "Competitive · 144 FPS", "144 FPS cap; VSync off; existing shadow, texture and effects quality set to 1."),
            new GamePreset("balanced", "Balanced · 120 FPS", "120 FPS cap; VSync on; existing shadow, texture and effects quality set to 2.")
        }),
        GameId.Valorant => Array.AsReadOnly(new[]
        {
            new GamePreset("competitive", "Competitive · 144 FPS", "Enable 144 FPS always cap; existing material and detail quality set to Low (0)."),
            new GamePreset("balanced", "Balanced · 120 FPS", "Enable 120 FPS always cap; existing material and detail quality set to Medium (1).")
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(game))
    };

    internal static (string Format, byte[] Target, IReadOnlyList<GameSettingChange> Changes, IReadOnlyList<string> Warnings)
        Transform(GameId game, byte[] bytes, string presetId)
    {
        if (presetId.StartsWith("custom-", StringComparison.Ordinal)) return CustomGameProfiles.Transform(game, bytes, presetId);
        if (!GetPresets(game).Any(p => p.Id == presetId)) throw new GameProfileException("Unknown preset.");
        var competitive = presetId == "competitive";
        var performance = IsFreePreset(game, presetId);
        var keepCap = performance && presetId == "performance";
        var cap = performance && !keepCap ? int.Parse(presetId["performance-".Length..], CultureInfo.InvariantCulture) : competitive ? 144 : 120;
        var changes = new List<GameSettingChange>();
        var warnings = new List<string> { "Close the game before applying or restoring. Cloud sync or the game may later replace local settings. Check the in-game menu after launch; no FPS improvement is guaranteed." };
        var document = game == GameId.CounterStrike2 ? ConfigDocument.ValveVideo(bytes) : ConfigDocument.Ini(bytes);
        string format;
        switch (game)
        {
            case GameId.CounterStrike2:
            {
                var version = OneOf(document.Get("video.cfg", "Version"), "15", "16");
                Integer(document.Get("video.cfg", "VendorID"), 1, 65535);
                Integer(document.Get("video.cfg", "DeviceID"), 1, 65535);
                Integer(document.Get("video.cfg", "setting.defaultres"), 320, 16384);
                Integer(document.Get("video.cfg", "setting.defaultresheight"), 200, 16384);
                OneOf(document.Get("video.cfg", "setting.mat_vsync"), "0", "1");
                OneOf(document.Get("video.cfg", "setting.msaa_samples"), "0", "2", "4", "8");
                document.Set("video.cfg", "setting.mat_vsync", competitive || performance ? "0" : "1", changes);
                document.Set("video.cfg", "setting.msaa_samples", performance ? "0" : competitive ? "2" : "4", changes);
                format = $"CS2 video.cfg / Version {version}";
                warnings.Add("CS2 presets change only VSync and MSAA; fps_max, shadows, resolution and input configuration are preserved.");
                break;
            }
            case GameId.Fortnite:
            {
                OneOf(document.Get(FortniteSection, "FortniteReleaseVersion"), "14");
                if (document.Has(FortniteSection, "Version")) OneOf(document.Get(FortniteSection, "Version"), "5");
                Boolean(document.Get(FortniteSection, "bUseVSync"));
                Number(document.Get(FortniteSection, "FrameRateLimit"), 0, 1000);
                document.Set(FortniteSection, "bUseVSync", competitive || performance ? "False" : "True", changes);
                if (!keepCap) document.Set(FortniteSection, "FrameRateLimit", cap.ToString("F6", CultureInfo.InvariantCulture), changes);
                foreach (var key in new[] { "sg.ShadowQuality", "sg.TextureQuality", "sg.EffectsQuality" })
                    OptionalQuality(document, "ScalabilityGroups", key, 4,
                        performance ? key == "sg.TextureQuality" ? "1" : "0" : competitive ? "1" : "2", changes, warnings);
                if (performance)
                    foreach (var key in new[] { "sg.PostProcessQuality", "sg.FoliageQuality" })
                        OptionalQuality(document, "ScalabilityGroups", key, 4, "0", changes, warnings);
                format = "Fortnite GameUserSettings / FortniteReleaseVersion 14";
                break;
            }
            case GameId.Valorant:
            {
                var version = OneOf(document.Get("Settings", "LocalSettingsVersion"), "5", "7");
                Boolean(document.Get("Settings", "EAresBoolSettingName::LimitFramerateAlways"));
                Number(document.Get("Settings", "EAresFloatSettingName::MaxFramerateAlways"), 1, 1000);
                if (!keepCap)
                {
                    document.Set("Settings", "EAresBoolSettingName::LimitFramerateAlways", "True", changes);
                    document.Set("Settings", "EAresFloatSettingName::MaxFramerateAlways", cap.ToString("F6", CultureInfo.InvariantCulture), changes);
                }
                foreach (var key in new[] { "EAresIntSettingName::MaterialQuality", "EAresIntSettingName::DetailQuality" })
                    OptionalQuality(document, "Settings", key, 2, competitive || performance ? "0" : "1", changes, warnings);
                format = $"VALORANT RiotUserSettings / LocalSettingsVersion {version}";
                warnings.Add("VALORANT uses RiotUserSettings.ini for the always-on cap. VSync, GameUserSettings.ini, roaming/account data, crosshair and sensitivity are not changed.");
                break;
            }
            default: throw new GameProfileException("Unknown game.");
        }
        if (performance && !keepCap && game != GameId.CounterStrike2)
            warnings.Add($"The {cap} FPS cap is a ceiling, not a promised frame rate. Choose a sustainable cap for your display and hardware; lower caps can reduce GPU load and heat.");
        return (format, document.Render(), changes.AsReadOnly(), warnings.AsReadOnly());
    }

    private static void OptionalQuality(ConfigDocument document, string section, string key, int maximum, string after,
        List<GameSettingChange> changes, List<string> warnings)
    {
        if (!document.Has(section, key)) { warnings.Add($"{key} is absent and will not be inserted."); return; }
        Integer(document.Get(section, key), 0, maximum);
        document.Set(section, key, after, changes);
    }
    private static string OneOf(string value, params string[] supported)
    {
        if (!supported.Contains(value, StringComparer.Ordinal)) throw new GameProfileException($"Unsupported configuration version or value: {value}.");
        return value;
    }
    private static void Boolean(string value)
    {
        if (!bool.TryParse(value, out _)) throw new GameProfileException("Expected a true/false setting.");
    }
    private static void Integer(string value, int minimum, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
            throw new GameProfileException("Unsupported integer setting.");
    }
    private static void Number(string value, double minimum, double maximum)
    {
        if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed < minimum || parsed > maximum)
            throw new GameProfileException("Unsupported frame-rate value.");
    }
}

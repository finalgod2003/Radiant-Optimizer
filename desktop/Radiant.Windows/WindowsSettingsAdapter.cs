using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using Radiant.Core;

namespace Radiant.Windows;

/// <summary>
/// Only these action IDs and HKCU values are writable. Neither the UI nor a saved
/// journal can supply a registry path, command, machine hive, or policy setting.
/// </summary>
internal sealed class WindowsSettingsAdapter : ISettingsAdapter
{
    private const string ActivePowerPlan = "power.active-plan";
    private static readonly IReadOnlyDictionary<string, RegistrySetting> RegistrySettings =
        new Dictionary<string, RegistrySetting>(StringComparer.Ordinal)
        {
            ["gaming.auto-game-mode"] = new(@"Software\Microsoft\GameBar", "AutoGameModeEnabled"),
            ["gaming.allow-auto-game-mode"] = new(@"Software\Microsoft\GameBar", "AllowAutoGameMode"),
            ["gaming.game-dvr"] = new(@"System\GameConfigStore", "GameDVR_Enabled"),
            ["gaming.app-capture"] = new(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"),
            ["gaming.historical-capture"] = new(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled"),
        };

    private static readonly TweakDefinition[] Definitions =
    [
        new("client-animations", "Reduce interface animations", "Disable Windows client-area animations in apps that respect this preference. Supported interfaces become less animated; this does not promise more game FPS. Original preference is backed up and can be restored without restarting Windows.", "Interface", false,
            [new(VisualPreferences.ClientAnimations, SettingValue.DWord(0))]),
        new("window-animations", "Instant window transitions", "Disable minimize/restore animations so windows appear and disappear immediately. This changes desktop transitions, not application processing speed. The original preference is saved before applying.", "Interface", false,
            [new(VisualPreferences.WindowAnimations, SettingValue.DWord(0))]),
        new("game-mode", "Game Mode", "Enable the current user's Windows Game Mode preference. Restart running games after applying; no frame-rate gain is guaranteed.", "Gaming", false,
            [new("gaming.auto-game-mode", SettingValue.DWord(1)), new("gaming.allow-auto-game-mode", SettingValue.DWord(1))]),
        new("game-recording", "Game recording", "Disable the current user's built-in game capture preferences. Windows Game Bar recording may stop working until you restore these values. This does not uninstall or close recording applications.", "Gaming", false,
            [new("gaming.game-dvr", SettingValue.DWord(0)), new("gaming.app-capture", SettingValue.DWord(0))]),
        new("background-capture", "Background capture", "Disable the current user's background recording preference. Recent gameplay may no longer be available to save; restart running games after applying.", "Gaming", false,
            [new("gaming.historical-capture", SettingValue.DWord(0))]),
    ];

    public Task<OptimizationPlan> PrepareAsync(string tweakId, string? option, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        if (tweakId == "power-plan")
        {
            if (!Guid.TryParseExact(option, "D", out var identifier)
                || !PowerPlans.Read().Any(plan => Guid.Parse(plan.Id) == identifier))
                throw new ArgumentException("Choose a power plan from the current list of installed plans.", nameof(option));
            return Task.FromResult(new OptimizationPlan(tweakId, [new(ActivePowerPlan, SettingValue.PowerPlan(identifier))], false));
        }

        if (option is not null) throw new ArgumentException("This setting does not accept an option.", nameof(option));
        var definition = Definitions.SingleOrDefault(candidate => candidate.Id == tweakId)
            ?? throw new ArgumentException("Unknown optimization action. Only listed settings can be applied.", nameof(tweakId));
        if (definition.Category == "Gaming") EnsureGamingAvailable(definition);
        return Task.FromResult(new OptimizationPlan(tweakId, definition.Targets, definition.RestartRequired));
    }

    public Task<SettingValue> ReadAsync(string settingId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        if (VisualPreferences.Handles(settingId)) return Task.FromResult(VisualPreferences.Read(settingId));
        return Task.FromResult(settingId == ActivePowerPlan
            ? SettingValue.PowerPlan(PowerPlans.ReadActive()) : ReadRegistry(FindRegistrySetting(settingId)));
    }

    public Task WriteAsync(string settingId, SettingValue value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        value.Validate();
        if (VisualPreferences.Handles(settingId))
        {
            VisualPreferences.Write(settingId, value);
            return Task.CompletedTask;
        }
        if (settingId == ActivePowerPlan)
        {
            if (value.Kind != SettingValueKind.PowerPlan) throw new InvalidDataException("A power plan requires a saved GUID.");
            PowerPlans.Activate(Guid.ParseExact(value.Data, "D"));
            return Task.CompletedTask;
        }

        var setting = FindRegistrySetting(settingId);
        if (!value.Exists)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(setting.Path, writable: true);
            existing?.DeleteValue(setting.Name, throwOnMissingValue: false);
            existing?.Flush();
            return Task.CompletedTask;
        }

        var (data, kind) = ToRegistryData(value);
        using var key = Registry.CurrentUser.CreateSubKey(setting.Path, writable: true)
            ?? throw new UnauthorizedAccessException("The current user's settings key could not be opened for writing.");
        key.SetValue(setting.Name, data, kind);
        key.Flush();
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<TweakStatus>> ReadTweaksAsync(CancellationToken cancellationToken)
    {
        var result = new List<TweakStatus>();
        foreach (var definition in Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = new List<string>();
            string? reason = null;
            try
            {
                foreach (var target in definition.Targets)
                    current.Add(VisualPreferences.Handles(target.SettingId)
                        ? (await ReadAsync(target.SettingId, cancellationToken)).Data == "1" ? "On" : "Off"
                        : $"{RegistrySettings[target.SettingId].Name}: {(await ReadAsync(target.SettingId, cancellationToken)).ToDisplayString()}");
                if (definition.Category == "Gaming") EnsureGamingAvailable(definition);
            }
            catch (Exception error) when (error is not OperationCanceledException) { reason = error.Message; }
            result.Add(new TweakStatus(definition.Id, definition.Title, definition.Description, definition.Category,
                current.Count == 0 ? "Unavailable" : string.Join("; ", current),
                definition.Id == "game-mode" ? "Enabled" : "Disabled", reason is null, false,
                definition.RestartRequired, reason));
        }

        try
        {
            var plans = await Task.Run(PowerPlans.Read, cancellationToken);
            result.Add(new TweakStatus("power-plan", "Power plan", "Switch to an existing Windows power plan. Performance-oriented plans may increase power use, temperature and fan noise. System policy may require administrator permission.",
                "Power", plans.SingleOrDefault(plan => plan.Active)?.Name ?? "Unavailable", "Choose an existing plan",
                plans.Count > 0 && plans.Any(plan => plan.Active), false, false, plans.Count == 0 ? "No installed power plans were found." : null));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            result.Add(new TweakStatus("power-plan", "Power plan", "Switch to an existing Windows power plan.", "Power", "Unavailable", "Choose an existing plan", false, error is UnauthorizedAccessException, false, error.Message));
        }
        return result;
    }

    private static void EnsureGamingAvailable(TweakDefinition definition)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("These game preferences require Windows 10 build 19041 or later; earlier versions are unsupported.");
        using var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", writable: false);
        if (definition.Id != "game-mode" && policy?.GetValue("AllowGameDVR") is not null)
            throw new InvalidOperationException("Game recording is controlled by a machine policy. This application does not override policy settings.");
        foreach (var target in definition.Targets)
        {
            var current = ReadRegistry(RegistrySettings[target.SettingId]);
            if (current.Exists && current.Kind != SettingValueKind.DWord)
                throw new InvalidOperationException($"{RegistrySettings[target.SettingId].Name} has an unexpected registry type; new changes are disabled to preserve it. Existing saved values can still be restored.");
        }
    }

    private static RegistrySetting FindRegistrySetting(string settingId)
        => RegistrySettings.TryGetValue(settingId, out var setting) ? setting
            : throw new InvalidDataException("This setting identifier is not in the optimizer allowlist.");

    private static SettingValue ReadRegistry(RegistrySetting setting)
    {
        using var key = Registry.CurrentUser.OpenSubKey(setting.Path, writable: false);
        if (key is null || !key.GetValueNames().Contains(setting.Name, StringComparer.OrdinalIgnoreCase))
            return SettingValue.Missing;
        var kind = key.GetValueKind(setting.Name);
        var value = key.GetValue(setting.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return (kind, value) switch
        {
            (RegistryValueKind.DWord, int data) => SettingValue.DWord(data),
            (RegistryValueKind.QWord, long data) => SettingValue.QWord(data),
            (RegistryValueKind.String, string data) => SettingValue.String(data),
            (RegistryValueKind.ExpandString, string data) => SettingValue.String(data, expandable: true),
            (RegistryValueKind.MultiString, string[] data) => SettingValue.MultiString(data),
            (RegistryValueKind.Binary, byte[] data) => SettingValue.Binary(data),
            (RegistryValueKind.None, byte[] data) => SettingValue.Binary(data, untyped: true),
            _ => throw new IOException($"Cannot safely read the type and original value of {setting.Name}. No change was made."),
        };
    }

    private static (object Data, RegistryValueKind Kind) ToRegistryData(SettingValue value) => value.Kind switch
    {
        SettingValueKind.DWord => (int.Parse(value.Data, CultureInfo.InvariantCulture), RegistryValueKind.DWord),
        SettingValueKind.QWord => (long.Parse(value.Data, CultureInfo.InvariantCulture), RegistryValueKind.QWord),
        SettingValueKind.String => (value.Data, RegistryValueKind.String),
        SettingValueKind.ExpandString => (value.Data, RegistryValueKind.ExpandString),
        SettingValueKind.MultiString => (JsonSerializer.Deserialize<string[]>(value.Data)!, RegistryValueKind.MultiString),
        SettingValueKind.Binary => (Convert.FromBase64String(value.Data), RegistryValueKind.Binary),
        SettingValueKind.None => (Convert.FromBase64String(value.Data), RegistryValueKind.None),
        _ => throw new InvalidDataException("This value type cannot be written to a game preference."),
    };

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows settings are unavailable on this operating system.");
    }

    private sealed record RegistrySetting(string Path, string Name);
    private sealed record TweakDefinition(string Id, string Title, string Description, string Category, bool RestartRequired, IReadOnlyList<SettingTarget> Targets);
}

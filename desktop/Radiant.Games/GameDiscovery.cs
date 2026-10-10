using System.Diagnostics;
using Microsoft.Win32;

namespace Radiant.Games;

public sealed record GameDiscoveryRoots(string LocalAppData, IReadOnlyList<string> SteamDirectories)
{
    public static GameDiscoveryRoots CurrentUser()
    {
        var steam = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string path && !string.IsNullOrWhiteSpace(path)) steam.Add(Path.GetFullPath(path));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFiles)) steam.Add(Path.Combine(programFiles, "Steam"));
        }
        return new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), steam.ToArray());
    }
}

internal static class GameDiscovery
{
    internal static IEnumerable<(GameId Game, string Path)> Candidates(GameDiscoveryRoots roots)
    {
        // No recursive/full-volume scan. At most 64 accounts per known root and eight Steam roots.
        foreach (var steam in roots.SteamDirectories.Take(8))
        {
            foreach (var account in Subdirectories(Path.Combine(steam, "userdata")))
            {
                var path = Path.Combine(account, "730", "local", "cfg", "cs2_video.txt");
                if (File.Exists(path)) yield return (GameId.CounterStrike2, path);
            }
        }
        var fortnite = Path.Combine(roots.LocalAppData, "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini");
        if (File.Exists(fortnite)) yield return (GameId.Fortnite, fortnite);
        foreach (var account in Subdirectories(Path.Combine(roots.LocalAppData, "VALORANT", "Saved", "Config")))
            foreach (var platform in new[] { "Windows", "WindowsClient" })
            {
                var path = Path.Combine(account, platform, "RiotUserSettings.ini");
                if (File.Exists(path)) yield return (GameId.Valorant, path);
            }
    }
    private static IReadOnlyList<string> Subdirectories(string root)
    {
        try
        {
            SafeFiles.AssertNoReparsePoints(root);
            if (!Directory.Exists(root)) return [];
            return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0).Take(64).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return []; }
    }
}

public sealed class WindowsGameProcessGuard : IGameProcessGuard
{
    public string? GetBlockingReason(GameId game)
    {
        if (!OperatingSystem.IsWindows()) return "Game process detection requires Windows.";
        var names = game switch
        {
            GameId.CounterStrike2 => new[] { "cs2", "cs2_win64" },
            GameId.Fortnite => new[] { "FortniteClient-Win64-Shipping", "FortniteClient-Win64-Shipping_EAC_EOS", "FortniteLauncher" },
            GameId.Valorant => new[] { "VALORANT-Win64-Shipping", "VALORANT", "RiotClientServices" },
            _ => throw new ArgumentOutOfRangeException(nameof(game))
        };
        try
        {
            foreach (var name in names)
            {
                var processes = Process.GetProcessesByName(name);
                try { if (processes.Length != 0) return $"Close {name} before changing or restoring its configuration."; }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { return "Cannot verify whether the game is running. No configuration will be changed."; }
    }
}

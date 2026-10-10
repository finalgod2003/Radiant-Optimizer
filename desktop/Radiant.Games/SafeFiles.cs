using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Radiant.Games;

internal static class SafeFiles
{
    internal const int MaximumBytes = 2 * 1024 * 1024;
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string ConfigPath(GameId game, string path)
    {
        var full = LocalPath(path);
        var parts = full.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        static bool Eq(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        static bool Account(string value) => value.Length is >= 1 and <= 100 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        var valid = false;
        // Steam/userdata/<account>/730/local/cfg/cs2_video.txt (six components after Steam).
        if (game == GameId.CounterStrike2)
            valid = parts.Length >= 6 && Eq(parts[^6], "userdata") && parts[^5].Length is >= 1 and <= 20 && parts[^5].All(char.IsAsciiDigit)
                && Eq(parts[^4], "730") && Eq(parts[^3], "local") && Eq(parts[^2], "cfg") && Eq(parts[^1], "cs2_video.txt");
        if (game == GameId.Fortnite)
            valid = parts.Length >= 5 && Eq(parts[^5], "FortniteGame") && Eq(parts[^4], "Saved") && Eq(parts[^3], "Config")
                && Eq(parts[^2], "WindowsClient") && Eq(parts[^1], "GameUserSettings.ini");
        if (game == GameId.Valorant)
            valid = parts.Length >= 6 && Eq(parts[^6], "VALORANT") && Eq(parts[^5], "Saved") && Eq(parts[^4], "Config")
                && Account(parts[^3]) && (Eq(parts[^2], "Windows") || Eq(parts[^2], "WindowsClient")) && Eq(parts[^1], "RiotUserSettings.ini");
        if (!valid) throw new GameProfileException("The selected file is outside the supported game's canonical configuration path.");
        AssertNoReparsePoints(full);
        return full;
    }

    internal static string LocalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new GameProfileException("Only an absolute local file path is supported.");
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows() && (full.Length < 3 || full[1] != ':' || full[2] != '\\' || full[2..].Contains(':')))
            throw new GameProfileException("Device, network and alternate-stream paths are unsupported.");
        return full;
    }

    internal static void AssertNoReparsePoints(string path)
    {
        var full = LocalPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new GameProfileException("Symbolic links and reparse points are not supported.");
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }
    }

    internal static byte[] Read(string path, bool requireWritable = false)
    {
        AssertNoReparsePoints(path);
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0) throw new GameProfileException("Expected a configuration file.");
        if (requireWritable && (attributes & FileAttributes.ReadOnly) != 0) throw new GameProfileException("Configuration is read-only; change the file's own permissions deliberately before retrying.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        CheckLinks(stream.SafeFileHandle);
        if (stream.Length > MaximumBytes) throw new GameProfileException("Configuration exceeds 2 MiB.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new GameProfileException("Configuration changed while reading.");
        return bytes;
    }

    internal static void WriteNewDurable(string path, byte[] bytes)
    {
        AssertNoReparsePoints(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(bytes); stream.Flush(true);
    }

    internal static void AtomicReplace(string path, byte[] target, string expectedHash, string displacedBackup, Action beforeReplace, Action? afterReplace = null)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".optimizer-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            WriteNewDurable(temporary, target);
            if (Hash(Read(path, true)) != expectedHash) throw new GameProfileException("Configuration changed after preview; no replacement was performed.");
            beforeReplace();
            AssertNoReparsePoints(path); AssertNoReparsePoints(displacedBackup);
            // File.Replace preserves the exact displaced file as an additional race/recovery backup.
            File.Replace(temporary, path, displacedBackup, false);
            afterReplace?.Invoke();
            var displaced = Read(displacedBackup);
            if (Hash(displaced) != expectedHash)
            {
                // An external rename raced the final check. Restore that external version only while our bytes remain current.
                if (Hash(Read(path, true)) == Hash(target))
                {
                    WriteNewDurable(temporary, displaced);
                    // Keep a second displaced capture even for this compensation. Another external
                    // rename can race the compensation too; never discard that version's bytes.
                    var compensationCapture = displacedBackup + ".compensation";
                    File.Replace(temporary, path, compensationCapture, false);
                    if (Hash(Read(compensationCapture)) == Hash(target)) File.Delete(compensationCapture);
                }
                throw new GameProfileException("An external file replacement raced application. Its exact bytes were retained in the displaced backup; inspect the recovery record.");
            }
            if (Hash(Read(path)) != Hash(target)) throw new GameProfileException("Configuration readback did not match. Original bytes remain available for recovery.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void CheckLinks(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!GetFileInformationByHandle(handle, out var info)) throw new GameProfileException("Cannot verify file identity.");
        if (info.NumberOfLinks != 1) throw new GameProfileException("Hard-linked configuration files are unsupported.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}

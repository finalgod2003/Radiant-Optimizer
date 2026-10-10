using System.ComponentModel;
using System.Runtime.InteropServices;
using Radiant.Core;

namespace Radiant.Windows;

// Documented Windows preferences. These IDs never accept registry paths or commands.
internal static class VisualPreferences
{
    internal const string ClientAnimations = "interface.client-animations";
    internal const string WindowAnimations = "interface.window-animations";
    internal static bool Handles(string id) => id is ClientAnimations or WindowAnimations;
    internal static SettingValue Read(string id)
    {
        if (id == ClientAnimations)
        {
            Check(GetBoolean(0x1042, 0, out var enabled, 0));
            return SettingValue.DWord(enabled != 0 ? 1 : 0);
        }
        if (id != WindowAnimations) throw new ArgumentException("Unknown animation setting.");
        var value = new AnimationInfo { Size = 8 };
        Check(Animation(0x48, value.Size, ref value, 0));
        return SettingValue.DWord(value.Enabled != 0 ? 1 : 0);
    }
    internal static void Write(string id, SettingValue value)
    {
        if (!Handles(id) || value.Kind != SettingValueKind.DWord || value.Data is not ("0" or "1"))
            throw new InvalidDataException("An animation preference must contain a saved boolean value.");
        uint action;
        if (id == ClientAnimations)
        {
            action = 0x1043;
            // SET uses BOOL by value in pvParam; GET uses BOOL*. Persist the profile.
            Check(SetBoolean(action, 0, new IntPtr(value.Data == "1" ? 1 : 0), 1));
        }
        else
        {
            action = 0x49;
            var animation = new AnimationInfo { Size = 8, Enabled = value.Data == "1" ? 1 : 0 };
            Check(Animation(action, animation.Size, ref animation, 1));
        }
        // Bound notification time for hung windows rather than using SPIF_SENDCHANGE.
        _ = Notify(new IntPtr(0xffff), 0x1a, new UIntPtr(action), IntPtr.Zero, 2, 100, out _);
    }
    private static void Check(bool result) { if (!result) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [StructLayout(LayoutKind.Sequential)] private struct AnimationInfo { public uint Size; public int Enabled; }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetBoolean(uint action, uint parameter, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetBoolean(uint action, uint parameter, IntPtr value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Animation(uint action, uint parameter, ref AnimationInfo value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr Notify(IntPtr window, uint message, UIntPtr wparam, IntPtr lparam, uint flags, uint timeout, out UIntPtr result);
}

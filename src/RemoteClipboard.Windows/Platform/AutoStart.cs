using System.Runtime.Versioning;
using Microsoft.Win32;

namespace RemoteClipboard.Windows.Platform;

/// <summary>
/// Per-user start at logon (HKCU\...\Run). Per-user on purpose: on a Windows Server each user decides
/// whether their own agent runs, and no administrator rights are needed.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RemoteClipboard";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Enable(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, $"\"{executablePath}\" --background", RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

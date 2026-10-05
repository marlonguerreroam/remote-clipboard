using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace RemoteClipboard.Windows.Platform;

/// <summary>Read-only facts about this Windows installation, from documented registry values.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsSystemInfo
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    /// <summary>True on Windows Server editions (InstallationType "Server" or "Server Core").</summary>
    public static bool IsServer
    {
        get
        {
            using var key = OpenLocalMachine(CurrentVersionKey);
            var type = key?.GetValue("InstallationType") as string;
            return type is not null && type.StartsWith("Server", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Friendly OS name such as "Windows 11", "Windows 10" or "Windows Server 2022".
    /// ProductName still says "Windows 10" on Windows 11, so client versions use the build number (22000+).
    /// </summary>
    public static string OsDescription
    {
        get
        {
            using var key = OpenLocalMachine(CurrentVersionKey);
            var productName = key?.GetValue("ProductName") as string ?? "Windows";
            _ = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var build);

            if (IsServer)
            {
                // e.g. "Windows Server 2022 Datacenter" -> "Windows Server 2022"
                var parts = productName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 3 ? string.Join(' ', parts.Take(3)) : productName;
            }

            return build >= 22000 ? "Windows 11" : "Windows 10";
        }
    }

    /// <summary>Per-installation GUID created by Windows setup (changes when a disk image is generalized).</summary>
    public static string MachineGuid
    {
        get
        {
            using var key = OpenLocalMachine(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string ?? string.Empty;
        }
    }

    public static string CurrentUserSid => WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;

    private static RegistryKey? OpenLocalMachine(string path)
    {
        // Always read the 64-bit view so values are correct regardless of process bitness.
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return root.OpenSubKey(path, writable: false);
    }
}

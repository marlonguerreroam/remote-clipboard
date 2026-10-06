// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;

namespace RemoteClipboard.Windows.Platform;

/// <summary>
/// Whether the app runs from an MSIX package (Microsoft Store) or as a classic install/portable copy.
/// Packaged, Windows manages start-with-Windows (manifest startup task) and the firewall rules, and
/// redirects writes under %LOCALAPPDATA% to the package's private folder.
/// </summary>
public static partial class PackageInfo
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    /// <summary>Package family name, or null when not packaged.</summary>
    public static string? FamilyName { get; } = QueryFamilyName();

    public static bool IsPackaged => FamilyName is not null;

    private static unsafe string? QueryFamilyName()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
        {
            return null;
        }

        uint length = 0;
        var result = GetCurrentPackageFamilyName(ref length, null);
        if (result == AppModelErrorNoPackage || result != ErrorInsufficientBuffer || length == 0)
        {
            return null;
        }

        var buffer = new char[length];
        fixed (char* pointer = buffer)
        {
            return GetCurrentPackageFamilyName(ref length, pointer) == 0 && length > 0
                ? new string(pointer, 0, (int)length - 1) // length includes the terminating null
                : null;
        }
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, char* packageFamilyName);
}

// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Windows.Platform;

namespace RemoteClipboard.Windows.Storage;

/// <summary>
/// Per-user data locations. Everything lives under %LOCALAPPDATA% (non-roaming): identity and keys
/// belong to this user on this machine and must never roam to another computer.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "RemoteClipboard");

    public static string SecretsDirectory => Path.Combine(DataDirectory, "secrets");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>
    /// Files received from other devices (deleted after a day). It must be a real path that Explorer can
    /// read when pasting: inside an MSIX package that is the package's private LocalCache folder.
    /// </summary>
    public static string ReceivedFilesDirectory => PackageInfo.FamilyName is { } family
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", family, "LocalCache", "Local", "RemoteClipboard", "received")
        : Path.Combine(DataDirectory, "received");

    /// <summary>
    /// Logs folder as other programs (Explorer) see it. Inside an MSIX package, writes to %LOCALAPPDATA%
    /// are redirected to the package's private LocalCache; only the app sees the original path.
    /// </summary>
    public static string LogsDirectoryForShell
    {
        get
        {
            if (PackageInfo.FamilyName is { } family)
            {
                var redirected = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", family, "LocalCache", "Local", "RemoteClipboard", "logs");
                if (Directory.Exists(redirected))
                {
                    return redirected;
                }
            }

            return LogsDirectory;
        }
    }
}

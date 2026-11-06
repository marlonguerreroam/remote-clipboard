// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.Versioning;

namespace RemoteClipboard.Windows.Clipboard;

/// <summary>
/// Tags received files as coming from another computer (Zone.Identifier, the same mark browsers add to
/// downloads), so Windows SmartScreen and Office Protected View treat them with care: a program
/// received from another device asks before running. Best effort: not every file system supports it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MarkOfTheWeb
{
    private const string ZoneInternet = "[ZoneTransfer]\r\nZoneId=3\r\n";

    public static void Apply(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var files = Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                : [path];
            foreach (var file in files)
            {
                try
                {
                    File.WriteAllText(file + ":Zone.Identifier", ZoneInternet);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    // FAT32/exFAT or network drives without alternate data streams.
                }
            }
        }
    }
}

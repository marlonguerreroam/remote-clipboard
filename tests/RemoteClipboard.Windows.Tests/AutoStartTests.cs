// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.Versioning;
using Microsoft.Win32;
using RemoteClipboard.Windows.Platform;

namespace RemoteClipboard.Windows.Tests;

/// <summary>Uses the real HKCU Run key; the original value is restored afterwards.</summary>
[SupportedOSPlatform("windows")]
[Collection("Registry")]
public sealed class AutoStartTests : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly object? _original;

    public AutoStartTests()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        _original = key?.GetValue("RemoteClipboard");
    }

    public void Dispose()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (_original is string value)
        {
            key.SetValue("RemoteClipboard", value);
        }
        else
        {
            key.DeleteValue("RemoteClipboard", throwOnMissingValue: false);
        }
    }

    [Fact]
    public void Repair_points_an_enabled_entry_to_the_new_location()
    {
        AutoStart.Enable(@"C:\RemoteClipboard\RemoteClipboard.exe"); // portable copy

        Assert.True(AutoStart.RepairPath(@"C:\Program Files\Remote Clipboard\RemoteClipboard.exe"));
        Assert.False(AutoStart.RepairPath(@"C:\Program Files\Remote Clipboard\RemoteClipboard.exe"));
        Assert.True(AutoStart.IsEnabled);
    }

    [Fact]
    public void Repair_never_enables_a_disabled_entry()
    {
        AutoStart.Disable();

        Assert.False(AutoStart.RepairPath(@"C:\Program Files\Remote Clipboard\RemoteClipboard.exe"));
        Assert.False(AutoStart.IsEnabled);
    }
}

// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Configuration;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Configuration;

public class AppSettingsTests
{
    [Fact]
    public void Settings_round_trip()
    {
        using var dir = new TempDirectory();
        var settings = new AppSettings { DisplayName = "PC-MARLON", DiscoveryEnabled = false, Theme = AppTheme.Dark, SyncEnabled = false, PreferredPort = 47805 };

        settings.Save(dir.File("settings.json"));

        Assert.Equal(settings, AppSettings.Load(dir.File("settings.json")));
        Assert.Contains("\"Dark\"", File.ReadAllText(dir.File("settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_or_corrupt_file_gives_defaults()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("bad.json"), "{ nope");

        Assert.Equal(new AppSettings(), AppSettings.Load(dir.File("missing.json")));
        Assert.Equal(new AppSettings(), AppSettings.Load(dir.File("bad.json")));
        Assert.True(new AppSettings().DiscoveryEnabled);
    }

    [Theory]
    [InlineData("  PC Marlon  ", "PC Marlon")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("A\u0007B", "AB")]
    public void Display_name_is_normalized(string? input, string? expected) =>
        Assert.Equal(expected, AppSettings.NormalizeDisplayName(input));

    [Fact]
    public void Display_name_is_limited() =>
        Assert.Equal(AppSettings.MaxDisplayNameLength, AppSettings.NormalizeDisplayName(new string('x', 500))!.Length);
}

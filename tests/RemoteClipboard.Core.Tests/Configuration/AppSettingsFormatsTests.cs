// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Configuration;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Configuration;

public sealed class AppSettingsFormatsTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Settings_from_the_previous_version_enable_images_and_files()
    {
        var path = _directory.File("settings.json");
        File.WriteAllText(path, """{ "DisplayName": "PC-OFICINA", "DiscoveryEnabled": true, "Theme": "Dark", "SyncEnabled": true }""");

        var settings = AppSettings.Load(path);

        Assert.True(settings.SyncImages);
        Assert.True(settings.SyncFiles);
        Assert.Equal([ClipboardFormat.Text, ClipboardFormat.Image, ClipboardFormat.FileList], settings.AcceptedFormats());
    }

    [Fact]
    public void Options_missing_from_an_old_file_keep_their_defaults()
    {
        var path = _directory.File("settings.json");
        File.WriteAllText(path, """{ "DisplayName": "PC-VIEJO" }""");

        var settings = AppSettings.Load(path);

        Assert.True(settings.DiscoveryEnabled);
        Assert.True(settings.SyncEnabled);
        Assert.Equal(47800, settings.PreferredPort);
    }

    [Fact]
    public void Turning_a_kind_off_removes_it_from_what_the_device_announces()
    {
        var settings = new AppSettings { SyncImages = false };
        Assert.Equal([ClipboardFormat.Text, ClipboardFormat.FileList], settings.AcceptedFormats());

        var path = _directory.File("settings.json");
        (settings with { SyncFiles = false }).Save(path);
        Assert.Equal([ClipboardFormat.Text], AppSettings.Load(path).AcceptedFormats());
    }
}

// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Storage;

namespace RemoteClipboard.Core.Configuration;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>User preferences. Contains no secrets.</summary>
/// <remarks>
/// Properties with a default value use <c>set</c>, not <c>init</c>: the source-generated serializer
/// assigns init-only properties in the object initializer, so a property missing from an older
/// settings.json would become false/0 instead of keeping its default.
/// </remarks>
public sealed record AppSettings
{
    public const int MaxDisplayNameLength = 64;

    /// <summary>Name shown to other devices. Null = the computer name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Announce this device on the LAN and list others (UDP 47810).</summary>
    public bool DiscoveryEnabled { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool SyncEnabled { get; set; } = true;

    /// <summary>Send and receive images (screenshots, copied pictures).</summary>
    public bool SyncImages { get; set; } = true;

    /// <summary>Send and receive files and folders copied in Explorer.</summary>
    public bool SyncFiles { get; set; } = true;

    /// <summary>Clipboard formats this device sends and accepts, as configured.</summary>
    public IReadOnlyList<Clipboard.ClipboardFormat> AcceptedFormats() =>
    [
        Clipboard.ClipboardFormat.Text,
        .. SyncImages ? [Clipboard.ClipboardFormat.Image] : Array.Empty<Clipboard.ClipboardFormat>(),
        .. SyncFiles ? [Clipboard.ClipboardFormat.FileList] : Array.Empty<Clipboard.ClipboardFormat>(),
    ];

    /// <summary>First TCP port tried; the next free one up to <see cref="ProtocolLimits.LastTcpPort"/> is used if busy.</summary>
    public int PreferredPort { get; set; } = ProtocolLimits.DefaultTcpPort;

    /// <summary>Set once the first-run start-with-Windows default has been applied.</summary>
    public bool AutoStartConfigured { get; init; }

    /// <summary>Trims, removes control characters and limits the length; empty means "use the computer name".</summary>
    public static string? NormalizeDisplayName(string? name)
    {
        var text = new string([.. (name ?? string.Empty).Trim().Where(c => !char.IsControl(c))]);
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > MaxDisplayNameLength ? text[..MaxDisplayNameLength] : text;
    }

    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllBytes(path), SettingsJsonContext.Default.AppSettings) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path) =>
        AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(this, SettingsJsonContext.Default.AppSettings));
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

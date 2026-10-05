using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Storage;

namespace RemoteClipboard.Core.Configuration;

/// <summary>User preferences. Contains no secrets.</summary>
public sealed record AppSettings
{
    public bool SyncEnabled { get; init; } = true;

    /// <summary>First TCP port tried; the next free one up to <see cref="ProtocolLimits.LastTcpPort"/> is used if busy.</summary>
    public int PreferredPort { get; init; } = ProtocolLimits.DefaultTcpPort;

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

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

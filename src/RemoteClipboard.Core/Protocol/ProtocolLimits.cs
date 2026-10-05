namespace RemoteClipboard.Core.Protocol;

public static class ProtocolLimits
{
    /// <summary>Current wire protocol version, exchanged in <see cref="HelloMessage"/>.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Preferred TCP port for the encrypted sync channel.</summary>
    public const int DefaultTcpPort = 47800;

    /// <summary>Last port tried when the preferred one is taken (multi-user Windows Server).</summary>
    public const int LastTcpPort = 47809;

    /// <summary>
    /// Maximum clipboard payload (32 MiB of UTF-8, roughly 30 million characters of text).
    /// Large content is split into <see cref="ChunkBytes"/> chunks, so frames stay small.
    /// </summary>
    public const int MaxClipboardBytes = 32 * 1024 * 1024;

    /// <summary>Raw payload bytes per <see cref="ClipboardUpdateMessage"/>.</summary>
    public const int ChunkBytes = 256 * 1024;

    /// <summary>Maximum frame size: one chunk as base64 inside JSON plus envelope overhead.</summary>
    public const int MaxFrameBytes = 1024 * 1024;
}

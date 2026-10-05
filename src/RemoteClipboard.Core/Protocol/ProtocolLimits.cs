namespace RemoteClipboard.Core.Protocol;

public static class ProtocolLimits
{
    /// <summary>Current wire protocol version, exchanged in <see cref="HelloMessage"/>.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Default TCP port for the encrypted sync channel.</summary>
    public const int DefaultTcpPort = 47800;

    /// <summary>Maximum clipboard payload accepted or sent (4 MiB of UTF-8 text in the MVP).</summary>
    public const int MaxClipboardBytes = 4 * 1024 * 1024;

    /// <summary>Maximum frame size: payload is base64 inside JSON (~4/3) plus envelope overhead.</summary>
    public const int MaxFrameBytes = 6 * 1024 * 1024;
}

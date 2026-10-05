using System.Net;
using RemoteClipboard.Core.Protocol;

namespace RemoteClipboard.Core.Networking;

/// <summary>Network tuning. Defaults are production values; tests shorten the timings.</summary>
public sealed record ConnectionOptions
{
    /// <summary>Listen address. IPv6Any in dual mode accepts IPv4 and IPv6.</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.IPv6Any;

    /// <summary>First port tried. 0 = let the OS choose (tests).</summary>
    public int PreferredPort { get; init; } = ProtocolLimits.DefaultTcpPort;

    public int LastPort { get; init; } = ProtocolLimits.LastTcpPort;

    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>No traffic for this long means the peer is gone (cable unplugged, sleep, power off).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(45);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan PairingTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a duplicate connection keeps delivering in-flight messages before it is closed.</summary>
    public TimeSpan RetireGracePeriod { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ReconnectMinDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan ReconnectMaxDelay { get; init; } = TimeSpan.FromSeconds(30);
}

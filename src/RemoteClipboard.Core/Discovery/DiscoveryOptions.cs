using System.Net;

namespace RemoteClipboard.Core.Discovery;

public sealed record DiscoveryOptions
{
    public const int DefaultPort = 47810;

    public bool Enabled { get; init; } = true;

    public int Port { get; init; } = DefaultPort;

    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>A device not heard from for this long disappears from the list.</summary>
    public TimeSpan Expiry { get; init; } = TimeSpan.FromSeconds(50);

    /// <summary>Where announcements are sent. Default: every IPv4 subnet broadcast address plus 255.255.255.255.</summary>
    public Func<IEnumerable<IPEndPoint>>? Targets { get; init; }
}

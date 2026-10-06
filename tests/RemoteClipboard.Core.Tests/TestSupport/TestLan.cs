using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using RemoteClipboard.Core.Discovery;

namespace RemoteClipboard.Core.Tests.TestSupport;

/// <summary>Simulated LAN for discovery: every member gets its own loopback UDP port and "broadcasts" to the others.</summary>
internal sealed class TestLan
{
    private readonly ConcurrentDictionary<int, bool> _ports = new();

    public DiscoveryOptions Join()
    {
        var port = FreeUdpPort();
        _ports[port] = true;
        return new DiscoveryOptions
        {
            Port = port,
            BindAddress = IPAddress.Loopback,
            AnnounceInterval = TimeSpan.FromMilliseconds(300),
            Expiry = TimeSpan.FromSeconds(3),
            Targets = () => _ports.Keys.Where(p => p != port).Select(p => new IPEndPoint(IPAddress.Loopback, p)).ToList(),
        };
    }

    /// <summary>Every current member, as an outsider (e.g. an attacker) would reach them with a broadcast.</summary>
    public IReadOnlyList<IPEndPoint> Members => [.. _ports.Keys.Select(p => new IPEndPoint(IPAddress.Loopback, p))];

    public void Leave(DiscoveryOptions options) => _ports.TryRemove(options.Port, out _);

    public static int FreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}

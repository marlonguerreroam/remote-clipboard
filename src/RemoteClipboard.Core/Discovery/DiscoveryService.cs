using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Logging;

namespace RemoteClipboard.Core.Discovery;

/// <summary>What this device announces. Evaluated at send time (port and pairing state can change).</summary>
public sealed record LocalAnnouncement(string DisplayName, string OsDescription, int Port, bool AcceptingPairing);

/// <summary>
/// LAN discovery over UDP broadcast: periodic announcements plus on-demand queries. Several agents on the
/// same machine (multi-user Windows Server) share the port. Datagrams are size-limited and validated;
/// nothing received here is trusted for authentication.
/// </summary>
public sealed class DiscoveryService : IAsyncDisposable
{
    private const int MaxDatagramBytes = 1024;
    private const int MaxDevices = 256;

    private readonly DeviceId _localId;
    private readonly Func<LocalAnnouncement> _announcement;
    private readonly DiscoveryOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<DeviceId, DiscoveredDevice> _devices = new();
    private readonly CancellationTokenSource _cts = new();
    private Socket? _socket;
    private Task? _receiveLoop;
    private Task? _announceLoop;
    private long _lastQueryAnswerTicks;
    private int _disposed;

    public DiscoveryService(DeviceId localId, Func<LocalAnnouncement> announcement, DiscoveryOptions options, ILogger logger, TimeProvider? time = null)
    {
        _localId = localId;
        _announcement = announcement ?? throw new ArgumentNullException(nameof(announcement));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised for every valid announcement from another device (may repeat).</summary>
    public event EventHandler<DiscoveredDevice>? DeviceSeen;

    /// <summary>Raised when the visible list changes (device added, removed or details changed).</summary>
    public event EventHandler? DevicesChanged;

    public IReadOnlyList<DiscoveredDevice> Devices
    {
        get
        {
            var cutoff = _time.GetUtcNow() - _options.Expiry;
            return [.. _devices.Values.Where(d => d.LastSeenUtc >= cutoff).OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        }
    }

    public void Start()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // Shared port: each user's agent on a terminal server receives the broadcasts.
            socket.ExclusiveAddressUse = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            socket.Bind(new IPEndPoint(_options.BindAddress, _options.Port));
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            Log.DiscoveryUnavailable(_logger, _options.Port, ex.SocketErrorCode.ToString());
            return;
        }

        _socket = socket;
        _receiveLoop = ReceiveLoopAsync(socket, _cts.Token);
        _announceLoop = AnnounceLoopAsync(_cts.Token);
        Query();
    }

    /// <summary>Send our announcement now (pairing window opened, network changed).</summary>
    public void AnnounceNow()
    {
        var a = _announcement();
        Send(new DiscoveryDatagram(DiscoveryDatagram.CurrentVersion, DiscoveryDatagram.Announce, _localId.Value, a.DisplayName, a.OsDescription, a.Port, a.AcceptingPairing));
    }

    /// <summary>Ask every device to announce itself now (e.g. the pairing window was opened).</summary>
    public void Query() =>
        Send(new DiscoveryDatagram(DiscoveryDatagram.CurrentVersion, DiscoveryDatagram.Query, _localId.Value));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        _socket?.Dispose();
        foreach (var loop in new[] { _receiveLoop, _announceLoop })
        {
            if (loop is null)
            {
                continue;
            }

            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
        }

        _cts.Dispose();
    }

    private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            AnnounceNow();
            PruneExpired();
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
            await Task.Delay(_options.AnnounceInterval + jitter, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(Socket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxDatagramBytes + 1];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // e.g. ICMP port unreachable surfacing on Windows; keep listening.
                continue;
            }

            if (result.ReceivedBytes is 0 or > MaxDatagramBytes || result.RemoteEndPoint is not IPEndPoint remote)
            {
                continue;
            }

            Handle(buffer.AsSpan(0, result.ReceivedBytes), remote);
        }
    }

    private void Handle(ReadOnlySpan<byte> payload, IPEndPoint remote)
    {
        DiscoveryDatagram? datagram;
        try
        {
            datagram = JsonSerializer.Deserialize(payload, DiscoveryJsonContext.Default.DiscoveryDatagram);
        }
        catch (JsonException)
        {
            return;
        }

        if (datagram is null || datagram.Version != DiscoveryDatagram.CurrentVersion || datagram.DeviceId == Guid.Empty || datagram.DeviceId == _localId.Value)
        {
            return;
        }

        if (datagram.Type == DiscoveryDatagram.Query)
        {
            // Answer queries, but at most once per second (a broadcast storm must not amplify).
            var now = _time.GetTimestamp();
            var last = Interlocked.Read(ref _lastQueryAnswerTicks);
            if (_time.GetElapsedTime(last, now) >= TimeSpan.FromSeconds(1) && Interlocked.CompareExchange(ref _lastQueryAnswerTicks, now, last) == last)
            {
                AnnounceNow();
            }

            return;
        }

        if (datagram.Type != DiscoveryDatagram.Announce || datagram.Port is < 1 or > 65535)
        {
            return;
        }

        var id = new DeviceId(datagram.DeviceId);
        if (!_devices.ContainsKey(id) && _devices.Count >= MaxDevices)
        {
            return;
        }

        var address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
        var device = new DiscoveredDevice(
            id, Sanitize(datagram.Name, "Dispositivo"), Sanitize(datagram.Os, "Windows"), address.ToString(),
            datagram.Port, datagram.AcceptingPairing, _time.GetUtcNow());

        var changed = !_devices.TryGetValue(id, out var previous) || previous with { LastSeenUtc = device.LastSeenUtc } != device;
        _devices[id] = device;
        DeviceSeen?.Invoke(this, device);
        if (changed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PruneExpired()
    {
        var cutoff = _time.GetUtcNow() - _options.Expiry;
        var removed = false;
        foreach (var (id, device) in _devices)
        {
            if (device.LastSeenUtc < cutoff)
            {
                removed |= _devices.TryRemove(id, out _);
            }
        }

        if (removed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Send(DiscoveryDatagram datagram)
    {
        var socket = _socket;
        if (socket is null || _cts.IsCancellationRequested)
        {
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(datagram, DiscoveryJsonContext.Default.DiscoveryDatagram);
        foreach (var target in (_options.Targets ?? BroadcastTargets)())
        {
            try
            {
                socket.SendTo(bytes, target);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // Adapter down or no route: discovery is best-effort.
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Used as Func<IEnumerable<IPEndPoint>>, same type as DiscoveryOptions.Targets.")]
    private IEnumerable<IPEndPoint> BroadcastTargets()
    {
        var targets = new HashSet<IPEndPoint> { new(IPAddress.Broadcast, _options.Port) };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                    {
                        continue;
                    }

                    var ip = unicast.Address.GetAddressBytes();
                    var mask = unicast.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (var i = 0; i < 4; i++)
                    {
                        broadcast[i] = (byte)(ip[i] | ~mask[i]);
                    }

                    targets.Add(new IPEndPoint(new IPAddress(broadcast), _options.Port));
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return targets;
    }

    private static string Sanitize(string? value, string fallback)
    {
        var text = new string([.. (value ?? string.Empty).Trim().Where(c => !char.IsControl(c))]);
        if (text.Length == 0)
        {
            return fallback;
        }

        return text.Length > 64 ? text[..64] : text;
    }
}

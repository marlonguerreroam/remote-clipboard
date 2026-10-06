using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Logging;
using RemoteClipboard.Core.Pairing;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Security;

namespace RemoteClipboard.Core.Networking;

/// <summary>
/// Owns the TCP listener, one reconnecting dial loop per paired device and the set of live connections.
/// Every device both listens and dials; if two connections to the same peer exist, both ends keep the one
/// opened by the device with the smaller id, so the choice is deterministic without coordination.
/// </summary>
public sealed class ConnectionManager : IAsyncDisposable
{
    private readonly DeviceIdentity _identity;
    private readonly LocalDeviceInfo _info;
    private readonly PeerStore _peers;
    private readonly ConnectionOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<DeviceId, PeerConnection> _connections = new();
    private readonly ConcurrentDictionary<DeviceId, CancellationTokenSource> _dialers = new();
    private readonly ConcurrentDictionary<DeviceId, (string Host, int Port)> _addressHints = new();
    private readonly Lock _registrationGate = new();
    private readonly AsyncPulse _pulse = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _pairingGate = new();
    private readonly SemaphoreSlim _pairingInProgress = new(1, 1);
    private PairingWindow? _pairingWindow;
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private int _disposed;

    public ConnectionManager(
        DeviceIdentity identity, LocalDeviceInfo info, PeerStore peers, ConnectionOptions options, ILogger logger, TimeProvider? time = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _info = info ?? throw new ArgumentNullException(nameof(info));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Handles application messages (clipboard). Runs on the connection's read loop.</summary>
    public Func<PeerConnection, ProtocolMessage, Task> MessageHandler { get; set; } = (_, _) => Task.CompletedTask;

    public event EventHandler<PeerConnection>? Connected;

    public event EventHandler<PeerConnection>? Disconnected;

    /// <summary>Raised when this device accepted a pairing initiated by another device.</summary>
    public event EventHandler<PairedDevice>? PairedAsHost;

    public int ListenPort { get; private set; }

    public IReadOnlyCollection<PeerConnection> Connections => [.. _connections.Values.Where(c => !c.IsClosed)];

    public bool IsConnected(DeviceId id) => _connections.TryGetValue(id, out var c) && !c.IsClosed;

    public PeerConnection? GetConnection(DeviceId id) => _connections.TryGetValue(id, out var c) && !c.IsClosed ? c : null;

    public void Start()
    {
        _listener = CreateListener();
        ListenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Log.Listening(_logger, ListenPort);
        _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);
        _peers.Changed += OnPeersChanged;
        SyncDialers();
    }

    /// <summary>Retry every disconnected peer now (network came back, IP changed, user asked).</summary>
    public void Wake() => _pulse.Pulse();

    /// <summary>
    /// Unverified address for a paired device (from LAN discovery). It is only tried as an extra candidate;
    /// it becomes the stored address only after a TLS handshake proves the device's identity.
    /// </summary>
    public void AddAddressHint(DeviceId id, string host, int port)
    {
        if (_peers.Find(id) is not { } peer)
        {
            return;
        }

        var hint = (host, port);
        var isNew = !_addressHints.TryGetValue(id, out var previous) || previous != hint;
        _addressHints[id] = hint;
        if (isNew && !IsConnected(id) && (peer.LastKnownHost != host || peer.LastKnownPort != port))
        {
            Log.AddressHint(_logger, id, $"{host}:{port}");
            _pulse.Pulse();
        }
    }

    public PairingWindow OpenPairingWindow()
    {
        lock (_pairingGate)
        {
            _pairingWindow = new PairingWindow(_time);
            Log.PairingWindowOpened(_logger, _pairingWindow.ExpiresAt);
            return _pairingWindow;
        }
    }

    public void ClosePairingWindow()
    {
        lock (_pairingGate)
        {
            _pairingWindow = null;
        }
    }

    /// <summary>True while a pairing invitation (code) is open on this device.</summary>
    public bool IsAcceptingPairing
    {
        get
        {
            lock (_pairingGate)
            {
                return _pairingWindow?.IsOpen == true;
            }
        }
    }

    /// <summary>Joiner side: pair with the device showing <paramref name="code"/> at host:port.</summary>
    public async Task<PairingOutcome> PairWithAsync(string host, int port, string code, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        timeout.CancelAfter(_options.PairingTimeout);

        TcpClient? client = null;
        try
        {
            client = await ConnectTcpAsync(host, port, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (PeerConnection.IsConnectionFailure(ex))
        {
            client?.Dispose();
            return new PairingOutcome(PairingStatus.Unreachable);
        }

        using (client)
        await using (var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
        {
            CertificatePin hostPin = default;
            try
            {
                // Certificate not trusted yet: accept it, record its pin, and let the PAKE confirmation bind it.
                var options = PinnedTls.CreateClientOptions(_identity.Certificate, cert =>
                {
                    hostPin = CertificatePin.FromCertificate(cert);
                    return true;
                });
                await ssl.AuthenticateAsClientAsync(options, timeout.Token).ConfigureAwait(false);

                var (status, result) = await PairingProtocol.RunJoinerAsync(ssl, code, _identity, _info, ListenPort, hostPin, timeout.Token).ConfigureAwait(false);
                if (status != PairingStatus.Success || result is null)
                {
                    Log.PairingFailed(_logger, status.ToString());
                    return new PairingOutcome(status);
                }

                var device = new PairedDevice(
                    new DeviceId(result.DeviceId), Sanitize(result.DisplayName), hostPin, _time.GetUtcNow(),
                    LastKnownHost: host, LastKnownPort: result.ListenPort > 0 ? result.ListenPort : port, OsDescription: Sanitize(result.OsDescription));
                _peers.AddOrUpdate(device);
                Log.PairingSucceeded(_logger, device.Id, device.DisplayName);
                return new PairingOutcome(PairingStatus.Success, device);
            }
            catch (Exception ex) when (PeerConnection.IsConnectionFailure(ex))
            {
                Log.PairingFailed(_logger, ex.GetType().Name);
                return new PairingOutcome(PairingStatus.Failed);
            }
        }
    }

    public void Disconnect(DeviceId id)
    {
        if (_connections.TryRemove(id, out var connection))
        {
            connection.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _peers.Changed -= OnPeersChanged;
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        foreach (var dialer in _dialers.Values)
        {
            await dialer.CancelAsync().ConfigureAwait(false);
        }

        foreach (var connection in _connections.Values)
        {
            connection.Close();
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (Exception ex) when (PeerConnection.IsConnectionFailure(ex))
            {
            }
        }

        _cts.Dispose();
        _pairingInProgress.Dispose();
    }

    private TcpListener CreateListener()
    {
        var first = _options.PreferredPort;
        var last = first == 0 ? 0 : Math.Max(first, _options.LastPort);
        SocketException? lastError = null;
        for (var port = first; port <= last; port++)
        {
            var listener = new TcpListener(_options.BindAddress, port);
            try
            {
                if (_options.BindAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    listener.Server.DualMode = true;
                }

                // Never share the port with another process/user (no SO_REUSEADDR hijacking on Windows).
                listener.ExclusiveAddressUse = OperatingSystem.IsWindows();
                listener.Start();
                return listener;
            }
            catch (SocketException ex)
            {
                listener.Dispose();
                lastError = ex;
            }
        }

        throw lastError ?? new SocketException((int)SocketError.AddressAlreadyInUse);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (PeerConnection.IsConnectionFailure(ex))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            _ = HandleInboundAsync(client, cancellationToken);
        }
    }

    private async Task HandleInboundAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint as IPEndPoint;
        var remoteText = remote?.ToString() ?? "?";
        client.NoDelay = true;
        var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        var handedOver = false;
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(_options.HandshakeTimeout);

            CertificatePin remotePin = default;
            var options = PinnedTls.CreateServerOptions(_identity.Certificate, cert =>
            {
                remotePin = CertificatePin.FromCertificate(cert);
                // Unknown certificates are only admitted while the user has a pairing window open.
                return _peers.FindByPin(remotePin) is not null || IsAcceptingPairing;
            });

            try
            {
                await ssl.AuthenticateAsServerAsync(options, handshake.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (PeerConnection.IsConnectionFailure(ex))
            {
                Log.UnauthorizedConnection(_logger, remoteText);
                return;
            }

            var first = await FrameCodec.ReadAsync(ssl, handshake.Token).ConfigureAwait(false);
            switch (first)
            {
                case HelloMessage hello:
                    var peer = _peers.FindByPin(remotePin);
                    if (peer is null || peer.Id.Value != hello.DeviceId || hello.ProtocolVersion != ProtocolLimits.ProtocolVersion)
                    {
                        Log.PeerIdentityMismatch(_logger);
                        return;
                    }

                    await FrameCodec.WriteAsync(ssl, LocalHello(), handshake.Token).ConfigureAwait(false);
                    peer = Learn(peer.Id, hello, Normalize(remote?.Address)?.ToString());
                    if (peer is null)
                    {
                        return; // unpaired during the handshake
                    }

                    handedOver = true;
                    Register(new PeerConnection(client, ssl, peer, hello, isInitiator: false, _options, _time, _logger));
                    return;

                case PairingRequestMessage request when IsAcceptingPairing:
                    await HostPairingAsync(ssl, request, remotePin, remote?.Address, cancellationToken).ConfigureAwait(false);
                    return;

                case PairingRequestMessage:
                    await FrameCodec.WriteAsync(ssl, new PairingResultMessage(false, "closed", Guid.Empty, string.Empty, string.Empty, 0), handshake.Token).ConfigureAwait(false);
                    return;

                default:
                    return;
            }
        }
        catch (Exception ex) when (PeerConnection.IsConnectionFailure(ex))
        {
            Log.InboundHandshakeFailed(_logger, remoteText, ex.GetType().Name);
        }
        finally
        {
            if (!handedOver)
            {
                if (ssl.IsAuthenticated)
                {
                    await CloseGracefullyAsync(ssl).ConfigureAwait(false);
                }

                await ssl.DisposeAsync().ConfigureAwait(false);
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// Ends a short-lived connection (pairing answer, rejection) without losing our last message.
    /// Closing a socket that still has unread data makes Windows send RST instead of FIN, and the RST can
    /// discard data already sent to the peer (seen as "Failed" instead of "LockedOut" in pairing).
    /// So: send TLS close_notify, then drain until the peer closes, bounded in time and size.
    /// </summary>
    private static async Task CloseGracefullyAsync(SslStream ssl)
    {
        const int MaxDrainBytes = 64 * 1024;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var buffer = new byte[4096];
        var drained = 0;
        try
        {
            await ssl.ShutdownAsync().ConfigureAwait(false);
            while (drained < MaxDrainBytes)
            {
                var read = await ssl.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                drained += read;
            }
        }
        catch (Exception ex) when (PeerConnection.IsConnectionFailure(ex) || ex is InvalidOperationException)
        {
            // Peer already gone or timeout: nothing more to do.
        }
    }

    private async Task HostPairingAsync(SslStream ssl, PairingRequestMessage request, CertificatePin joinerPin, IPAddress? remoteAddress, CancellationToken cancellationToken)
    {
        if (!await _pairingInProgress.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            await FrameCodec.WriteAsync(ssl, new PairingResultMessage(false, "busy", Guid.Empty, string.Empty, string.Empty, 0), cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            PairingWindow? window;
            lock (_pairingGate)
            {
                window = _pairingWindow;
            }

            if (window is null)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.PairingTimeout);
            var accepted = await PairingProtocol.RunHostAsync(
                ssl, window.Code, _identity, _info, ListenPort, request, joinerPin, window.RegisterAttempt, timeout.Token).ConfigureAwait(false);

            if (!accepted)
            {
                Log.PairingFailed(_logger, "verification failed");
                return;
            }

            ClosePairingWindow();
            var device = new PairedDevice(
                new DeviceId(request.DeviceId), Sanitize(request.DisplayName), joinerPin, _time.GetUtcNow(),
                LastKnownHost: Normalize(remoteAddress)?.ToString(), LastKnownPort: request.ListenPort > 0 ? request.ListenPort : null,
                OsDescription: Sanitize(request.OsDescription));
            _peers.AddOrUpdate(device);
            Log.PairingSucceeded(_logger, device.Id, device.DisplayName);
            PairedAsHost?.Invoke(this, device);
        }
        finally
        {
            _pairingInProgress.Release();
        }
    }

    private async Task DialLoopAsync(DeviceId peerId, CancellationToken cancellationToken)
    {
        var delay = _options.ReconnectMinDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            var wake = _pulse.WaitAsync();
            if (IsConnected(peerId))
            {
                await WaitAsync(wake, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                delay = _options.ReconnectMinDelay;
                continue;
            }

            var peer = _peers.Find(peerId);
            if (peer is null)
            {
                return;
            }

            foreach (var (host, port) in DialCandidates(peer))
            {
                try
                {
                    await DialAsync(peer, host, port, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (PeerConnection.IsConnectionFailure(ex) && !cancellationToken.IsCancellationRequested)
                {
                    Log.ConnectFailed(_logger, peerId, $"{host}:{port}", ex.GetType().Name);
                }
            }

            if (IsConnected(peerId))
            {
                delay = _options.ReconnectMinDelay;
                continue;
            }

            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Math.Max(1, delay.TotalMilliseconds / 4)));
            if (await WaitAsync(wake, delay + jitter, cancellationToken).ConfigureAwait(false))
            {
                delay = _options.ReconnectMinDelay; // woken by network change / disconnect: retry promptly
            }
            else
            {
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.ReconnectMaxDelay.Ticks));
            }
        }
    }

    /// <summary>Verified last address first, then the discovery hint if it differs.</summary>
    private List<(string Host, int Port)> DialCandidates(PairedDevice peer)
    {
        var candidates = new List<(string Host, int Port)>(2);
        if (peer.LastKnownHost is not null && peer.LastKnownPort is int port)
        {
            candidates.Add((peer.LastKnownHost, port));
        }

        if (_addressHints.TryGetValue(peer.Id, out var hint) && !candidates.Contains(hint))
        {
            candidates.Add(hint);
        }

        return candidates;
    }

    private async Task DialAsync(PairedDevice peer, string host, int port, CancellationToken cancellationToken)
    {
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshake.CancelAfter(_options.ConnectTimeout + _options.HandshakeTimeout);

        var client = await ConnectTcpAsync(host, port, handshake.Token).ConfigureAwait(false);
        var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsClientAsync(PinnedTls.CreateClientOptions(_identity.Certificate, peer.CertificatePin), handshake.Token).ConfigureAwait(false);
            await FrameCodec.WriteAsync(ssl, LocalHello(), handshake.Token).ConfigureAwait(false);
            if (await FrameCodec.ReadAsync(ssl, handshake.Token).ConfigureAwait(false) is not HelloMessage hello
                || hello.DeviceId != peer.Id.Value || hello.ProtocolVersion != ProtocolLimits.ProtocolVersion)
            {
                throw new ProtocolException("Unexpected handshake response.");
            }

            // The handshake proved the identity: this address is now verified.
            peer = Learn(peer.Id, hello, host) ?? throw new ProtocolException("Device was unpaired during the handshake.");
            _addressHints.TryRemove(peer.Id, out _);
            Register(new PeerConnection(client, ssl, peer, hello, isInitiator: true, _options, _time, _logger));
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            client.Dispose();
            throw;
        }
    }

    private async Task<TcpClient> ConnectTcpAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connect.CancelAfter(_options.ConnectTimeout);
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private void Register(PeerConnection connection)
    {
        PeerConnection? retired = null;
        var becameCurrent = true;
        bool wasConnected;
        lock (_registrationGate)
        {
            wasConnected = _connections.TryGetValue(connection.PeerId, out var existing) && !existing.IsClosed;
            if (wasConnected && IsPreferred(existing!) && !IsPreferred(connection))
            {
                retired = connection;
                becameCurrent = false;
            }
            else
            {
                retired = wasConnected ? existing : null;
                _connections[connection.PeerId] = connection;
            }
        }

        // Read loops run for every connection, including the one being retired, so messages already in
        // flight on it are still delivered. It is no longer used for sending and closes after a grace period.
        _ = RunConnectionAsync(connection);
        if (retired is not null)
        {
            _ = CloseAfterGraceAsync(retired);
        }

        if (becameCurrent)
        {
            if (!wasConnected)
            {
                Log.DeviceConnected(_logger, connection.PeerId, connection.Peer.DisplayName);
            }

            Connected?.Invoke(this, connection);
        }
    }

    private async Task CloseAfterGraceAsync(PeerConnection connection)
    {
        try
        {
            await Task.Delay(_options.RetireGracePeriod, _time, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        connection.Close();
    }

    private async Task RunConnectionAsync(PeerConnection connection)
    {
        await Task.Yield();
        await connection.RunAsync(MessageHandler).ConfigureAwait(false);
        var removed = false;
        lock (_registrationGate)
        {
            if (_connections.TryGetValue(connection.PeerId, out var current) && ReferenceEquals(current, connection))
            {
                _connections.TryRemove(connection.PeerId, out _);
                removed = true;
            }
        }

        if (removed)
        {
            Log.DeviceDisconnected(_logger, connection.PeerId);
            Disconnected?.Invoke(this, connection);
            _pulse.Pulse();
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>The connection opened by the device with the smaller id wins.</summary>
    private bool IsPreferred(PeerConnection connection)
    {
        var initiator = connection.IsInitiator ? _identity.Id : connection.PeerId;
        var smaller = _identity.Id.Value.CompareTo(connection.PeerId.Value) < 0 ? _identity.Id : connection.PeerId;
        return initiator == smaller;
    }

    private PairedDevice? Learn(DeviceId id, HelloMessage hello, string? verifiedHost)
    {
        // Track IP/port changes so we can dial back after the peer moves (DHCP, restarts, other port).
        // Only network/display fields are touched, on the current stored record: user choices made
        // meanwhile (sync direction, unpairing) are never overwritten.
        return _peers.TryUpdate(id, current => current with
        {
            DisplayName = Sanitize(hello.DisplayName),
            OsDescription = Sanitize(hello.OsDescription),
            LastKnownHost = verifiedHost ?? current.LastKnownHost,
            LastKnownPort = hello.ListenPort > 0 ? hello.ListenPort : current.LastKnownPort,
        });
    }

    private HelloMessage LocalHello() =>
        new(ProtocolLimits.ProtocolVersion, _identity.Id.Value, _info.DisplayName, _info.OsDescription, ListenPort);

    private void OnPeersChanged(object? sender, EventArgs e) => SyncDialers();

    private void SyncDialers()
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        var known = _peers.All.Select(p => p.Id).ToHashSet();
        foreach (var id in known)
        {
            if (!_dialers.ContainsKey(id))
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                if (_dialers.TryAdd(id, cts))
                {
                    _ = DialLoopAsync(id, cts.Token);
                }
                else
                {
                    cts.Dispose();
                }
            }
        }

        foreach (var id in _dialers.Keys.Where(id => !known.Contains(id)).ToList())
        {
            // Unpaired: stop dialing and drop any live connection immediately.
            if (_dialers.TryRemove(id, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            Disconnect(id);
        }
    }

    private static async Task<bool> WaitAsync(Task wake, TimeSpan delay, CancellationToken cancellationToken)
    {
        var timer = Task.Delay(delay, cancellationToken);
        var completed = await Task.WhenAny(wake, timer).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return completed == wake;
    }

    private static IPAddress? Normalize(IPAddress? address) =>
        address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;

    private static string Sanitize(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        text = new string([.. text.Where(c => !char.IsControl(c))]);
        return text.Length > 64 ? text[..64] : text;
    }
}

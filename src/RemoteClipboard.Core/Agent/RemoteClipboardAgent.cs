using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Logging;
using RemoteClipboard.Core.Networking;
using RemoteClipboard.Core.Pairing;
using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.Core.Agent;

/// <summary>
/// Composition of identity, paired devices, networking, pairing and synchronization: everything the
/// tray application needs, independent of Windows (tests run two agents in one process).
/// </summary>
public sealed class RemoteClipboardAgent : IAsyncDisposable
{
    private readonly DeviceIdentity _identity;
    private readonly PeerStore _peers;
    private readonly IClipboardMonitor _monitor;
    private readonly ConnectionManager _connections;
    private readonly SyncEngine _sync;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _syncLoop;
    private int _disposed;

    public RemoteClipboardAgent(
        DeviceIdentity identity,
        LocalDeviceInfo info,
        PeerStore peers,
        IClipboardMonitor monitor,
        IClipboardWriter writer,
        ConnectionOptions options,
        ILoggerFactory loggerFactory,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _logger = loggerFactory.CreateLogger<RemoteClipboardAgent>();

        _connections = new ConnectionManager(identity, info, peers, options, loggerFactory.CreateLogger<ConnectionManager>(), time);
        _sync = new SyncEngine(identity.Id, writer, () => _connections.Connections, loggerFactory.CreateLogger<SyncEngine>(), time);
        _connections.MessageHandler = _sync.OnMessageAsync;
        _connections.Connected += (_, _) => RaiseStateChanged();
        _connections.Disconnected += (_, _) => RaiseStateChanged();
        _connections.PairedAsHost += (_, device) => Paired?.Invoke(this, device);
        _peers.Changed += (_, _) => RaiseStateChanged();
        _sync.ContentSent += (_, _) => ContentSent?.Invoke(this, EventArgs.Empty);
        _sync.ContentReceived += (_, id) => ContentReceived?.Invoke(this, id);
    }

    /// <summary>Paired list or connection state changed.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Another device completed pairing with this one (host side).</summary>
    public event EventHandler<PairedDevice>? Paired;

    public event EventHandler? ContentSent;

    public event EventHandler<DeviceId>? ContentReceived;

    public DeviceId DeviceId => _identity.Id;

    public int ListenPort => _connections.ListenPort;

    public bool SyncEnabled
    {
        get => _sync.Enabled;
        set
        {
            _sync.Enabled = value;
            RaiseStateChanged();
        }
    }

    public IReadOnlyList<DeviceStatus> Devices =>
        [.. _peers.All.Select(p =>
        {
            var connection = _connections.GetConnection(p.Id);
            return new DeviceStatus(p, connection is not null, connection?.RemoteEndPoint?.Address.ToString() ?? p.LastKnownHost);
        })];

    public void Start()
    {
        _connections.Start();
        _syncLoop = _sync.RunAsync(_cts.Token);
        _monitor.ClipboardChanged += _sync.OnLocalClipboardChanged;
        _monitor.Start();
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    /// <summary>Host side: show this code on screen; it expires after two minutes or three wrong attempts.</summary>
    public PairingWindow OpenPairing() => _connections.OpenPairingWindow();

    public void ClosePairing() => _connections.ClosePairingWindow();

    /// <summary>Joiner side: pair with the device showing <paramref name="code"/>.</summary>
    public Task<PairingOutcome> PairWithAsync(string host, int port, string code, CancellationToken cancellationToken) =>
        _connections.PairWithAsync(host, port, code, cancellationToken);

    /// <summary>Removes trust immediately: the connection is closed and future handshakes fail.</summary>
    public void Unpair(DeviceId id)
    {
        if (_peers.Remove(id))
        {
            _connections.Disconnect(id);
            Log.DeviceUnpaired(_logger, id);
        }
    }

    /// <summary>Retry disconnected devices now.</summary>
    public void Reconnect() => _connections.Wake();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _monitor.ClipboardChanged -= _sync.OnLocalClipboardChanged;
        await _cts.CancelAsync().ConfigureAwait(false);
        await _connections.DisposeAsync().ConfigureAwait(false);
        if (_syncLoop is not null)
        {
            try
            {
                await _syncLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        Log.NetworkChanged(_logger);
        _connections.Wake();
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => OnNetworkChanged(sender, e);

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}

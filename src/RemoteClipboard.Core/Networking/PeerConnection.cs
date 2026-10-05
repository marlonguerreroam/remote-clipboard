using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Logging;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.Core.Networking;

/// <summary>
/// An authenticated, encrypted connection to one paired device. One reader loop, serialized writers,
/// and a heartbeat that detects silent failures (cable unplugged, Wi-Fi lost, power off).
/// </summary>
public sealed class PeerConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly SslStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly ConnectionOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private long _lastReceivedTicks;
    private int _closed;

    internal PeerConnection(
        TcpClient client, SslStream stream, PairedDevice peer, HelloMessage remoteHello, bool isInitiator,
        ConnectionOptions options, TimeProvider time, ILogger logger)
    {
        _client = client;
        _stream = stream;
        Peer = peer;
        RemoteHello = remoteHello;
        IsInitiator = isInitiator;
        _options = options;
        _time = time;
        _logger = logger;
        RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
        _lastReceivedTicks = time.GetTimestamp();
    }

    public PairedDevice Peer { get; internal set; }

    public DeviceId PeerId => Peer.Id;

    public HelloMessage RemoteHello { get; }

    /// <summary>True when this device opened the connection.</summary>
    public bool IsInitiator { get; }

    public IPEndPoint? RemoteEndPoint { get; }

    internal ClipboardAssembler Assembler { get; } = new();

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>Sends messages contiguously (chunks of one clipboard item are never interleaved).</summary>
    public async Task SendAsync(IEnumerable<ProtocolMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        await _writeLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            foreach (var message in messages)
            {
                await FrameCodec.WriteAsync(_stream, message, linked.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken) =>
        SendAsync([message], cancellationToken);

    /// <summary>Runs until the connection ends. Never throws for network/protocol failures.</summary>
    internal async Task RunAsync(Func<PeerConnection, ProtocolMessage, Task> onMessage)
    {
        var heartbeat = HeartbeatAsync();
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var message = await FrameCodec.ReadAsync(_stream, _cts.Token).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                Interlocked.Exchange(ref _lastReceivedTicks, _time.GetTimestamp());
                switch (message)
                {
                    case PingMessage ping:
                        await SendAsync(new PongMessage(ping.Nonce), _cts.Token).ConfigureAwait(false);
                        break;
                    case PongMessage:
                        break;
                    default:
                        await onMessage(this, message).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            if (ex is ProtocolException)
            {
                Log.ProtocolViolation(_logger, PeerId, ex.Message);
            }
        }
        finally
        {
            Close();
            await heartbeat.ConfigureAwait(false);
        }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        _stream.Dispose();
        _client.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Close();
        _cts.Dispose();
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }

    internal static bool IsConnectionFailure(Exception ex) =>
        ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException
            or ProtocolException or System.Security.Authentication.AuthenticationException or TimeoutException;

    private async Task HeartbeatAsync()
    {
        long nonce = 0;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(_options.PingInterval, _time, _cts.Token).ConfigureAwait(false);
                var idle = _time.GetElapsedTime(Interlocked.Read(ref _lastReceivedTicks));
                if (idle > _options.IdleTimeout)
                {
                    Log.PeerTimedOut(_logger, PeerId);
                    Close();
                    return;
                }

                await SendAsync(new PingMessage(++nonce), _cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            Close();
        }
    }
}

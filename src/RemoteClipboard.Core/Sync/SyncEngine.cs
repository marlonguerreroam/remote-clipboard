using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Logging;
using RemoteClipboard.Core.Networking;
using RemoteClipboard.Core.Protocol;

namespace RemoteClipboard.Core.Sync;

/// <summary>
/// Bidirectional clipboard synchronization. All clipboard work (local changes and remote writes) is
/// processed sequentially on one queue, so the newest change always wins and the echo guard sees
/// events in order. Devices only send what originated locally; received content is never relayed.
/// </summary>
public sealed class SyncEngine
{
    private readonly DeviceId _localId;
    private readonly IClipboardWriter _writer;
    private readonly Func<IReadOnlyCollection<PeerConnection>> _connections;
    private readonly Func<DeviceId, SyncDirection> _directionOf;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly ClipboardEchoGuard _guard = new();
    private readonly MessageDeduplicator _dedup = new();
    private readonly Channel<Func<CancellationToken, Task>> _queue =
        Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });

    public SyncEngine(
        DeviceId localId,
        IClipboardWriter writer,
        Func<IReadOnlyCollection<PeerConnection>> connections,
        ILogger logger,
        TimeProvider? time = null,
        Func<DeviceId, SyncDirection>? directionOf = null)
    {
        _localId = localId;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
        // Read live so a policy change applies immediately to existing connections.
        _directionOf = directionOf ?? (_ => SyncDirection.Bidirectional);
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Raised after local content was sent to at least one device (no content in the event).</summary>
    public event EventHandler<Guid>? ContentSent;

    /// <summary>Raised after remote content was written to the local clipboard.</summary>
    public event EventHandler<DeviceId>? ContentReceived;

    /// <summary>Called from the clipboard monitor; returns immediately.</summary>
    public void OnLocalClipboardChanged(object? sender, ClipboardChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _queue.Writer.TryWrite(ct => HandleLocalChangeAsync(change, ct));
    }

    /// <summary>Called from a connection's read loop (in order, per connection).</summary>
    public Task OnMessageAsync(PeerConnection connection, ProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (message is not ClipboardUpdateMessage chunk)
        {
            return Task.CompletedTask;
        }

        // No relaying: a device may only send content it originated itself.
        if (chunk.OriginDeviceId != connection.PeerId.Value)
        {
            throw new ProtocolException("Clipboard origin does not match the authenticated device.");
        }

        var content = connection.Assembler.Add(chunk);
        if (content is not null)
        {
            _queue.Writer.TryWrite(ct => ApplyRemoteAsync(connection, chunk.MessageId, content, ct));
        }

        return Task.CompletedTask;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await work(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // One failed item must not stop synchronization.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Log.UnexpectedError(_logger, nameof(SyncEngine), ex);
            }
        }
    }

    private async Task HandleLocalChangeAsync(ClipboardChange change, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            Log.ClipboardChangeSkipped(_logger, OutboundDecision.SkipSyncDisabled);
            return;
        }

        var targets = _connections().Where(c => _directionOf(c.PeerId) != SyncDirection.ReceiveOnly).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var decision = _guard.EvaluateLocalChange(change);
        if (decision != OutboundDecision.Broadcast)
        {
            Log.ClipboardChangeSkipped(_logger, decision);
            return;
        }

        var messageId = Guid.NewGuid();
        var chunks = ClipboardChunker.Split(change.Content!, _localId, messageId, _time.GetUtcNow()).ToList();
        Log.SyncStarted(_logger, messageId, targets.Count);
        var results = await Task.WhenAll(targets.Select(c => TrySendAsync(c, chunks, cancellationToken))).ConfigureAwait(false);
        if (results.Any(sent => sent))
        {
            Log.SyncCompleted(_logger, messageId);
            ContentSent?.Invoke(this, messageId);
        }
    }

    private static async Task<bool> TrySendAsync(PeerConnection connection, List<ClipboardUpdateMessage> chunks, CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendAsync(chunks, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (PeerConnection.IsConnectionFailure(ex))
        {
            connection.Close(); // the connection manager will reconnect
            return false;
        }
    }

    private async Task ApplyRemoteAsync(PeerConnection connection, Guid messageId, ClipboardContent content, CancellationToken cancellationToken)
    {
        if (!_dedup.TryRegister(messageId) || !Enabled || _directionOf(connection.PeerId) == SyncDirection.SendOnly)
        {
            return;
        }

        // Register BEFORE writing so the resulting clipboard event is recognised as an echo.
        _guard.RegisterRemoteContent(content);
        try
        {
            await _writer.WriteRemoteContentAsync(content, cancellationToken).ConfigureAwait(false);
            Log.RemoteClipboardApplied(_logger, messageId, connection.PeerId);
            ContentReceived?.Invoke(this, connection.PeerId);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.ClipboardWriteFailed(_logger, ex);
        }
    }
}

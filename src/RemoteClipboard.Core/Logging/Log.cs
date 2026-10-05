using Microsoft.Extensions.Logging;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.Core.Logging;

/// <summary>
/// All log events of the application. PRIVACY RULE: no event may take clipboard content, its length,
/// pairing codes or key material as a parameter. Only technical metadata is logged.
/// </summary>
public static partial class Log
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Clipboard event detected (format: {Format})")]
    public static partial void ClipboardEventDetected(ILogger logger, ClipboardFormat format);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Clipboard is busy (held by another application); change skipped")]
    public static partial void ClipboardBusy(ILogger logger);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "Could not write remote content to the clipboard")]
    public static partial void ClipboardWriteFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Device identity {DeviceId} ({Result})")]
    public static partial void IdentityLoaded(ILogger logger, DeviceId deviceId, string result);

    [LoggerMessage(EventId = 9000, Level = LogLevel.Error, Message = "Unexpected error in {Component}")]
    public static partial void UnexpectedError(ILogger logger, string component, Exception exception);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Clipboard change not broadcast: {Decision}")]
    public static partial void ClipboardChangeSkipped(ILogger logger, OutboundDecision decision);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Clipboard synchronization started (message {MessageId}, {PeerCount} peer(s))")]
    public static partial void SyncStarted(ILogger logger, Guid messageId, int peerCount);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Clipboard synchronization completed (message {MessageId})")]
    public static partial void SyncCompleted(ILogger logger, Guid messageId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Remote clipboard applied (message {MessageId} from {DeviceId})")]
    public static partial void RemoteClipboardApplied(ILogger logger, Guid messageId, DeviceId deviceId);

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Device connected: {DeviceId} ({DisplayName})")]
    public static partial void DeviceConnected(ILogger logger, DeviceId deviceId, string displayName);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Device disconnected: {DeviceId}")]
    public static partial void DeviceDisconnected(ILogger logger, DeviceId deviceId);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Rejected connection from unauthorized endpoint {RemoteEndpoint}")]
    public static partial void UnauthorizedConnection(ILogger logger, string remoteEndpoint);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Protocol violation by {DeviceId}: {Reason}")]
    public static partial void ProtocolViolation(ILogger logger, DeviceId deviceId, string reason);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information, Message = "Device {DeviceId} stopped responding (heartbeat timeout)")]
    public static partial void PeerTimedOut(ILogger logger, DeviceId deviceId);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information, Message = "Listening on TCP port {Port}")]
    public static partial void Listening(ILogger logger, int port);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Debug, Message = "Connection attempt to {DeviceId} at {Endpoint} failed: {Reason}")]
    public static partial void ConnectFailed(ILogger logger, DeviceId deviceId, string endpoint, string reason);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Debug, Message = "Inbound connection from {RemoteEndpoint} closed during handshake: {Reason}")]
    public static partial void InboundHandshakeFailed(ILogger logger, string remoteEndpoint, string reason);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Information, Message = "Network change detected; reconnecting")]
    public static partial void NetworkChanged(ILogger logger);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Warning, Message = "Peer announced a different device id than the paired one")]
    public static partial void PeerIdentityMismatch(ILogger logger);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Pairing window opened (expires {ExpiresAt:u})")]
    public static partial void PairingWindowOpened(ILogger logger, DateTimeOffset expiresAt);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "Device unpaired: {DeviceId}")]
    public static partial void DeviceUnpaired(ILogger logger, DeviceId deviceId);

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Pairing successful with {DeviceId} ({DisplayName})")]
    public static partial void PairingSucceeded(ILogger logger, DeviceId deviceId, string displayName);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Pairing failed: {Reason}")]
    public static partial void PairingFailed(ILogger logger, string reason);
}

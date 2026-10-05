using System.Text.Json.Serialization;
using RemoteClipboard.Core.Clipboard;

namespace RemoteClipboard.Core.Protocol;

/// <summary>
/// Messages exchanged inside the mutually-authenticated TLS channel. The JSON "type" discriminator
/// is part of the protocol: never rename existing values. New message kinds (images, files, history)
/// are added as new derived types without breaking older peers.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(ClipboardUpdateMessage), "clipboard")]
[JsonDerivedType(typeof(PingMessage), "ping")]
[JsonDerivedType(typeof(PongMessage), "pong")]
public abstract record ProtocolMessage;

/// <summary>First message on every connection, sent by both sides.</summary>
public sealed record HelloMessage(
    int ProtocolVersion,
    Guid DeviceId,
    string DisplayName,
    string OsDescription,
    int ListenPort) : ProtocolMessage;

/// <summary>A clipboard change originated on <paramref name="OriginDeviceId"/>.</summary>
public sealed record ClipboardUpdateMessage(
    Guid MessageId,
    Guid OriginDeviceId,
    DateTimeOffset CreatedUtc,
    ClipboardFormat Format,
    byte[] Data) : ProtocolMessage
{
    // Content-free: records would otherwise print the payload.
    public override string ToString() => $"ClipboardUpdateMessage({MessageId}, {Format})";
}

public sealed record PingMessage(long Nonce) : ProtocolMessage;

public sealed record PongMessage(long Nonce) : ProtocolMessage;

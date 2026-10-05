using System.Text.Json.Serialization;
using RemoteClipboard.Core.Clipboard;

namespace RemoteClipboard.Core.Protocol;

/// <summary>
/// Messages exchanged inside the TLS channel. The JSON "type" discriminator is part of the protocol:
/// never rename existing values. New message kinds are added as new derived types.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(ClipboardUpdateMessage), "clipboard")]
[JsonDerivedType(typeof(PingMessage), "ping")]
[JsonDerivedType(typeof(PongMessage), "pong")]
[JsonDerivedType(typeof(PairingRequestMessage), "pairRequest")]
[JsonDerivedType(typeof(PairingRound1Message), "pairRound1")]
[JsonDerivedType(typeof(PairingRound2Message), "pairRound2")]
[JsonDerivedType(typeof(PairingConfirmMessage), "pairConfirm")]
[JsonDerivedType(typeof(PairingResultMessage), "pairResult")]
public abstract record ProtocolMessage;

/// <summary>First message of a sync connection, sent by both sides.</summary>
public sealed record HelloMessage(
    int ProtocolVersion,
    Guid DeviceId,
    string DisplayName,
    string OsDescription,
    int ListenPort) : ProtocolMessage;

/// <summary>
/// One chunk of a clipboard change originated on <paramref name="OriginDeviceId"/>. Content larger than
/// <see cref="ProtocolLimits.ChunkBytes"/> is sent as consecutive chunks sharing <paramref name="MessageId"/>.
/// </summary>
public sealed record ClipboardUpdateMessage(
    Guid MessageId,
    Guid OriginDeviceId,
    DateTimeOffset CreatedUtc,
    ClipboardFormat Format,
    int TotalLength,
    int ChunkIndex,
    int ChunkCount,
    byte[] Data) : ProtocolMessage
{
    // Content-free: records would otherwise print the payload.
    public override string ToString() => $"ClipboardUpdateMessage({MessageId}, {Format}, chunk {ChunkIndex + 1}/{ChunkCount})";
}

public sealed record PingMessage(long Nonce) : ProtocolMessage;

public sealed record PongMessage(long Nonce) : ProtocolMessage;

/// <summary>Sent by the joining device right after TLS to start pairing.</summary>
public sealed record PairingRequestMessage(
    int ProtocolVersion,
    Guid DeviceId,
    string DisplayName,
    string OsDescription,
    int ListenPort) : ProtocolMessage;

/// <summary>J-PAKE round 1 (RFC 8236). Big integers are unsigned big-endian.</summary>
public sealed record PairingRound1Message(
    string ParticipantId,
    byte[] Gx1,
    byte[] Gx2,
    byte[][] KnowledgeProofForX1,
    byte[][] KnowledgeProofForX2) : ProtocolMessage;

/// <summary>J-PAKE round 2.</summary>
public sealed record PairingRound2Message(
    string ParticipantId,
    byte[] A,
    byte[][] KnowledgeProofForX2s) : ProtocolMessage;

/// <summary>Explicit key confirmation bound to both TLS certificate pins.</summary>
public sealed record PairingConfirmMessage(byte[] Mac) : ProtocolMessage;

/// <summary>Final verdict of the host, plus its public identity when successful.</summary>
public sealed record PairingResultMessage(
    bool Success,
    string? Reason,
    Guid DeviceId,
    string DisplayName,
    string OsDescription,
    int ListenPort) : ProtocolMessage;

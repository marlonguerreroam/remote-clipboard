using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteClipboard.Core.Protocol;

/// <summary>
/// Length-prefixed framing: [uint32 big-endian length][UTF-8 JSON message].
/// Runs on top of an authenticated TLS stream; it provides structure, not security.
/// Callers must serialize concurrent writes on the same stream.
/// </summary>
public static class FrameCodec
{
    private const int HeaderSize = 4;

    public static async ValueTask WriteAsync(
        Stream stream,
        ProtocolMessage message,
        CancellationToken cancellationToken,
        int maxFrameBytes = ProtocolLimits.MaxFrameBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJsonContext.Default.ProtocolMessage);
        var frame = new byte[HeaderSize + payload.Length];
        try
        {
            if (payload.Length > maxFrameBytes)
            {
                throw new ProtocolException($"Outgoing frame exceeds the {maxFrameBytes}-byte limit.");
            }

            BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
            payload.CopyTo(frame.AsSpan(HeaderSize));
            await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Payloads may contain clipboard data: do not leave copies lying around.
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(frame);
        }
    }

    /// <summary>Reads one message. Returns null when the peer closed the stream cleanly between frames.</summary>
    public static async ValueTask<ProtocolMessage?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken,
        int maxFrameBytes = ProtocolLimits.MaxFrameBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[HeaderSize];
        var read = await stream.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < HeaderSize)
        {
            throw new ProtocolException("Connection closed in the middle of a frame header.");
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length == 0 || length > (uint)maxFrameBytes)
        {
            throw new ProtocolException($"Invalid frame length {length}.");
        }

        var payload = new byte[length];
        try
        {
            try
            {
                await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException ex)
            {
                throw new ProtocolException("Connection closed in the middle of a frame.", ex);
            }

            try
            {
                return JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.ProtocolMessage)
                    ?? throw new ProtocolException("Empty message.");
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // Never include the payload in the exception: it may contain clipboard data.
                throw new ProtocolException("Malformed or unsupported message.", ex);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}

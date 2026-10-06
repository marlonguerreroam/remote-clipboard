// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Protocol;

namespace RemoteClipboard.Core.Sync;

/// <summary>
/// Reassembles chunked clipboard messages from ONE connection (TCP keeps them ordered). Every header
/// field is validated before memory is allocated, so a peer cannot force large allocations by lying.
/// A new message id abandons any incomplete transfer (the newest clipboard wins).
/// </summary>
public sealed class ClipboardAssembler
{
    private readonly int _maxContentBytes;
    private readonly int _maxChunkBytes;
    private ClipboardUpdateMessage? _first;
    private byte[]? _buffer;
    private int _received;
    private int _nextIndex;

    public ClipboardAssembler(int maxContentBytes = ProtocolLimits.MaxClipboardBytes, int maxChunkBytes = ProtocolLimits.ChunkBytes)
    {
        _maxContentBytes = maxContentBytes;
        _maxChunkBytes = maxChunkBytes;
    }

    /// <summary>Adds a chunk. Returns the completed content on the last chunk, otherwise null.</summary>
    /// <exception cref="ProtocolException">The chunk sequence is inconsistent or exceeds limits.</exception>
    public ClipboardContent? Add(ClipboardUpdateMessage chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var data = chunk.Data ?? [];

        if (chunk.ChunkIndex == 0)
        {
            Reset();
            Validate(chunk.TotalLength is >= 0 && chunk.TotalLength <= _maxContentBytes, "Clipboard content exceeds the size limit.");
            var expectedCount = Math.Max(1, (chunk.TotalLength + _maxChunkBytes - 1) / _maxChunkBytes);
            Validate(chunk.ChunkCount >= expectedCount && chunk.ChunkCount <= Math.Max(1, chunk.TotalLength), "Invalid chunk count.");
            Validate(chunk.Format != ClipboardFormat.Unknown && Enum.IsDefined(chunk.Format), "Unsupported clipboard format.");
            _first = chunk;
            _buffer = new byte[chunk.TotalLength];
        }
        else
        {
            Validate(_first is not null && chunk.MessageId == _first.MessageId && chunk.ChunkIndex == _nextIndex, "Unexpected clipboard chunk.");
            Validate(chunk.TotalLength == _first!.TotalLength && chunk.ChunkCount == _first.ChunkCount && chunk.Format == _first.Format
                && chunk.OriginDeviceId == _first.OriginDeviceId, "Inconsistent clipboard chunk header.");
        }

        Validate(data.Length <= _maxChunkBytes && _received + data.Length <= _first!.TotalLength, "Clipboard chunk too large.");
        data.CopyTo(_buffer!, _received);
        _received += data.Length;
        _nextIndex = chunk.ChunkIndex + 1;

        if (_nextIndex < _first.ChunkCount)
        {
            return null;
        }

        Validate(_received == _first.TotalLength, "Clipboard content length mismatch.");
        var content = ClipboardContent.FromBytes(_first.Format, _buffer);
        Reset();
        return content;
    }

    /// <summary>Header of the transfer in progress or just completed (message id, origin).</summary>
    public ClipboardUpdateMessage? Current => _first;

    private void Reset()
    {
        if (_buffer is not null)
        {
            CryptographicOperations.ZeroMemory(_buffer);
        }

        _buffer = null;
        _received = 0;
        _nextIndex = 0;
    }

    private void Validate(bool condition, string message)
    {
        if (!condition)
        {
            Reset();
            _first = null;
            throw new ProtocolException(message);
        }
    }
}

// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Protocol;

namespace RemoteClipboard.Core.Sync;

/// <summary>Splits clipboard content into protocol chunks.</summary>
public static class ClipboardChunker
{
    public static IEnumerable<ClipboardUpdateMessage> Split(
        ClipboardContent content,
        DeviceId origin,
        Guid messageId,
        DateTimeOffset createdUtc,
        int chunkBytes = ProtocolLimits.ChunkBytes)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkBytes);

        var total = content.Length;
        var count = Math.Max(1, (total + chunkBytes - 1) / chunkBytes);
        for (var i = 0; i < count; i++)
        {
            var offset = i * chunkBytes;
            var length = Math.Min(chunkBytes, total - offset);
            yield return new ClipboardUpdateMessage(
                messageId, origin.Value, createdUtc, content.Format, total, i, count,
                content.Data.Slice(offset, length).ToArray());
        }
    }
}

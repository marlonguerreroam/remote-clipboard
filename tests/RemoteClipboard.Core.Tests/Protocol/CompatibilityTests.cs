// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.Core.Tests.Protocol;

/// <summary>Devices on the previous version (text only, no "accepts" in the hello) must keep working.</summary>
public class CompatibilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hello_from_a_previous_version_means_text_only()
    {
        // Exactly what v0.4 sends: no "accepts" property.
        var json = $$"""{"type":"hello","protocolVersion":1,"deviceId":"{{Guid.NewGuid()}}","displayName":"PC-VIEJO","osDescription":"Windows 10","listenPort":47800}""";
        var hello = Assert.IsType<HelloMessage>(await ReadFrameAsync(json));

        Assert.Null(hello.Accepts);
        Assert.True(hello.CanReceive(ClipboardFormat.Text));
        Assert.False(hello.CanReceive(ClipboardFormat.Image));
        Assert.False(hello.CanReceive(ClipboardFormat.FileList));
    }

    [Fact]
    public async Task Hello_announces_accepted_formats_and_keeps_the_protocol_version()
    {
        var sent = new HelloMessage(ProtocolLimits.ProtocolVersion, Guid.NewGuid(), "PC", "Windows 11", 47800,
            [ClipboardFormat.Text, ClipboardFormat.Image]);
        using var stream = new MemoryStream();
        await FrameCodec.WriteAsync(stream, sent, Ct);
        stream.Position = 0;

        var received = Assert.IsType<HelloMessage>(await FrameCodec.ReadAsync(stream, Ct));

        Assert.Equal(1, received.ProtocolVersion); // unchanged: old devices still accept the handshake
        Assert.True(received.CanReceive(ClipboardFormat.Image));
        Assert.False(received.CanReceive(ClipboardFormat.FileList));
    }

    [Fact]
    public void Files_get_a_larger_limit_than_text_and_images()
    {
        var assembler = new ClipboardAssembler();
        var big = ProtocolLimits.MaxClipboardBytes + 1;
        var count = (big + ProtocolLimits.ChunkBytes - 1) / ProtocolLimits.ChunkBytes;

        Assert.Throws<ProtocolException>(() => assembler.Add(Chunk(ClipboardFormat.Image, big, count)));
        Assert.Throws<ProtocolException>(() => assembler.Add(Chunk(ClipboardFormat.Text, big, count)));
        Assert.Null(assembler.Add(Chunk(ClipboardFormat.FileList, big, count))); // accepted, waits for more chunks

        var tooBig = ProtocolLimits.MaxFileListBytes + 1;
        Assert.Throws<ProtocolException>(() => assembler.Add(Chunk(ClipboardFormat.FileList, tooBig, (tooBig / ProtocolLimits.ChunkBytes) + 1)));
    }

    private static ClipboardUpdateMessage Chunk(ClipboardFormat format, int total, int count) =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, format, total, 0, count, new byte[ProtocolLimits.ChunkBytes]);

    private static async Task<ProtocolMessage?> ReadFrameAsync(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);
        using var stream = new MemoryStream(frame);
        return await FrameCodec.ReadAsync(stream, Ct);
    }
}

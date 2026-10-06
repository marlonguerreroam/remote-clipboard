// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Protocol;

public class FrameCodecTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Samples => TextSamples.All;

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Clipboard_update_round_trips(string text)
    {
        var content = ClipboardContent.FromText(text);
        var sent = new ClipboardUpdateMessage(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, content.Format, content.Length, 0, 1, content.Data.ToArray());
        using var stream = new MemoryStream();

        await FrameCodec.WriteAsync(stream, sent, Ct);
        stream.Position = 0;
        var received = Assert.IsType<ClipboardUpdateMessage>(await FrameCodec.ReadAsync(stream, Ct));

        Assert.Equal(sent.MessageId, received.MessageId);
        Assert.Equal(sent.OriginDeviceId, received.OriginDeviceId);
        Assert.Equal(text, ClipboardContent.FromBytes(received.Format, received.Data).GetText());
    }

    [Fact]
    public async Task Multiple_messages_are_read_in_order_then_clean_eof_returns_null()
    {
        using var stream = new MemoryStream();
        var hello = new HelloMessage(ProtocolLimits.ProtocolVersion, Guid.NewGuid(), "PC-MARLON", "Windows 11", 47800);
        await FrameCodec.WriteAsync(stream, hello, Ct);
        await FrameCodec.WriteAsync(stream, new PingMessage(42), Ct);
        await FrameCodec.WriteAsync(stream, new PongMessage(42), Ct);
        stream.Position = 0;

        Assert.Equal(hello, await FrameCodec.ReadAsync(stream, Ct));
        Assert.Equal(new PingMessage(42), await FrameCodec.ReadAsync(stream, Ct));
        Assert.Equal(new PongMessage(42), await FrameCodec.ReadAsync(stream, Ct));
        Assert.Null(await FrameCodec.ReadAsync(stream, Ct));
    }

    [Fact]
    public async Task Truncated_header_is_a_protocol_error()
    {
        using var stream = new MemoryStream([0, 0]);

        await Assert.ThrowsAsync<ProtocolException>(async () => await FrameCodec.ReadAsync(stream, Ct));
    }

    [Fact]
    public async Task Truncated_payload_is_a_protocol_error()
    {
        var frame = new byte[4 + 3];
        BinaryPrimitives.WriteUInt32BigEndian(frame, 100);
        using var stream = new MemoryStream(frame);

        await Assert.ThrowsAsync<ProtocolException>(async () => await FrameCodec.ReadAsync(stream, Ct));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    [InlineData((uint)ProtocolLimits.MaxFrameBytes + 1)]
    public async Task Invalid_length_is_rejected_before_allocating(uint length)
    {
        var frame = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(frame, length);
        using var stream = new MemoryStream(frame);

        await Assert.ThrowsAsync<ProtocolException>(async () => await FrameCodec.ReadAsync(stream, Ct));
    }

    [Theory]
    [InlineData("{\"type\":\"steal-clipboard\"}")]
    [InlineData("{not json")]
    [InlineData("{\"nonce\":1}")]
    public async Task Malformed_or_unknown_messages_are_rejected(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);
        using var stream = new MemoryStream(frame);

        await Assert.ThrowsAsync<ProtocolException>(async () => await FrameCodec.ReadAsync(stream, Ct));
    }

    [Fact]
    public async Task Oversized_outgoing_frame_is_rejected()
    {
        var message = new ClipboardUpdateMessage(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, ClipboardFormat.Text, 2048, 0, 1, new byte[2048]);
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ProtocolException>(async () => await FrameCodec.WriteAsync(stream, message, Ct, maxFrameBytes: 1024));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Clipboard_message_ToString_hides_payload()
    {
        var data = Encoding.UTF8.GetBytes("contraseña123");
        var message = new ClipboardUpdateMessage(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, ClipboardFormat.Text, data.Length, 0, 1, data);

        Assert.DoesNotContain("contraseña123", message.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(data), message.ToString(), StringComparison.Ordinal);
    }
}

// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.Core.Tests.Sync;

public class ClipboardChunkingTests
{
    private static readonly DeviceId Origin = DeviceId.New();

    private static ClipboardContent? RoundTrip(ClipboardContent content, int chunkBytes, ClipboardAssembler? assembler = null)
    {
        assembler ??= new ClipboardAssembler(maxChunkBytes: chunkBytes);
        ClipboardContent? result = null;
        foreach (var chunk in ClipboardChunker.Split(content, Origin, Guid.NewGuid(), DateTimeOffset.UtcNow, chunkBytes))
        {
            Assert.Null(result);
            result = assembler.Add(chunk);
        }

        return result;
    }

    [Fact]
    public void Long_text_round_trips_through_many_chunks()
    {
        // ~10 MiB with multi-byte characters and emojis straddling chunk boundaries.
        var builder = new StringBuilder();
        while (builder.Length < 5_000_000)
        {
            builder.Append("Línea con ñ, acentos y emojis 🚀✅ — ").Append(builder.Length).Append("\r\n");
        }

        var text = builder.ToString();
        var result = RoundTrip(ClipboardContent.FromText(text), ProtocolLimits.ChunkBytes);

        Assert.NotNull(result);
        Assert.Equal(text, result.GetText());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    public void Boundary_sizes_round_trip(int size)
    {
        var text = new string('a', size);
        var result = RoundTrip(ClipboardContent.FromText(text), 1024);

        Assert.Equal(text, result!.GetText());
    }

    [Fact]
    public async Task Each_chunk_frame_stays_under_the_frame_limit()
    {
        var content = ClipboardContent.FromText(new string('x', ProtocolLimits.ChunkBytes * 2));
        foreach (var chunk in ClipboardChunker.Split(content, Origin, Guid.NewGuid(), DateTimeOffset.UtcNow))
        {
            using var stream = new MemoryStream();
            await FrameCodec.WriteAsync(stream, chunk, TestContext.Current.CancellationToken);
            Assert.True(stream.Length < ProtocolLimits.MaxFrameBytes);
        }
    }

    [Fact]
    public void Declared_size_above_limit_is_rejected_before_allocation()
    {
        var assembler = new ClipboardAssembler(maxContentBytes: 100, maxChunkBytes: 10);
        var chunk = new ClipboardUpdateMessage(Guid.NewGuid(), Origin.Value, DateTimeOffset.UtcNow, ClipboardFormat.Text, int.MaxValue, 0, int.MaxValue, [1]);

        Assert.Throws<ProtocolException>(() => assembler.Add(chunk));
    }

    [Fact]
    public void Out_of_order_or_foreign_chunks_are_rejected()
    {
        var assembler = new ClipboardAssembler(maxChunkBytes: 4);
        var chunks = ClipboardChunker.Split(ClipboardContent.FromText("123456789"), Origin, Guid.NewGuid(), DateTimeOffset.UtcNow, 4).ToList();

        assembler.Add(chunks[0]);
        Assert.Throws<ProtocolException>(() => assembler.Add(chunks[2]));
        Assert.Throws<ProtocolException>(() => assembler.Add(chunks[1] with { MessageId = Guid.NewGuid() }));
    }

    [Fact]
    public void Chunk_larger_than_declared_total_is_rejected()
    {
        var assembler = new ClipboardAssembler(maxChunkBytes: 8);
        var chunk = new ClipboardUpdateMessage(Guid.NewGuid(), Origin.Value, DateTimeOffset.UtcNow, ClipboardFormat.Text, 2, 0, 1, [1, 2, 3]);

        Assert.Throws<ProtocolException>(() => assembler.Add(chunk));
    }

    [Fact]
    public void New_transfer_abandons_incomplete_one()
    {
        var assembler = new ClipboardAssembler(maxChunkBytes: 4);
        var first = ClipboardChunker.Split(ClipboardContent.FromText("aaaaaaaa"), Origin, Guid.NewGuid(), DateTimeOffset.UtcNow, 4).First();
        assembler.Add(first);

        var result = RoundTrip(ClipboardContent.FromText("nuevo"), 4, assembler);

        Assert.Equal("nuevo", result!.GetText());
    }
}

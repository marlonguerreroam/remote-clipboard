using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Clipboard;

public class ClipboardContentTests
{
    public static TheoryData<string> Samples => TextSamples.All;

    [Theory]
    [MemberData(nameof(Samples))]
    public void Text_round_trips_exactly(string text)
    {
        var content = ClipboardContent.FromText(text);

        Assert.Equal(ClipboardFormat.Text, content.Format);
        Assert.Equal(text, content.GetText());
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Text_round_trips_through_wire_bytes(string text)
    {
        var sent = ClipboardContent.FromText(text);
        var received = ClipboardContent.FromBytes(sent.Format, sent.Data.Span);

        Assert.Equal(text, received.GetText());
    }

    [Fact]
    public void Line_endings_are_preserved()
    {
        var content = ClipboardContent.FromText(TextSamples.WindowsNewLines);

        Assert.Contains("\r\n", content.GetText(), StringComparison.Ordinal);
        Assert.Equal(3, content.GetText().Split("\r\n").Length - 1);
    }

    [Fact]
    public void Empty_text_is_representable_and_flagged()
    {
        var content = ClipboardContent.FromText(string.Empty);

        Assert.True(content.IsEmpty);
        Assert.Equal(string.Empty, content.GetText());
    }

    [Fact]
    public void ToString_never_exposes_content()
    {
        var content = ClipboardContent.FromText("contraseña123");

        Assert.DoesNotContain("contraseña123", content.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_format_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipboardContent.FromBytes(ClipboardFormat.Unknown, [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipboardContent.FromBytes((ClipboardFormat)99, [1]));
    }

    [Fact]
    public void FromBytes_copies_input()
    {
        byte[] data = [0x41, 0x42];
        var content = ClipboardContent.FromBytes(ClipboardFormat.Text, data);
        data[0] = 0x5A;

        Assert.Equal("AB", content.GetText());
    }
}

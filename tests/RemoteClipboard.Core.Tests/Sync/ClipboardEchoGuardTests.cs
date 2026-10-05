using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.Core.Tests.Sync;

public class ClipboardEchoGuardTests
{
    private static ClipboardChange Local(string text) => new(ClipboardContent.FromText(text), false, false);

    [Fact]
    public void Genuine_local_change_is_broadcast()
    {
        var guard = new ClipboardEchoGuard();

        Assert.Equal(OutboundDecision.Broadcast, guard.EvaluateLocalChange(Local("Hola")));
    }

    [Fact]
    public void Consecutive_different_changes_are_all_broadcast()
    {
        var guard = new ClipboardEchoGuard();

        Assert.Equal(OutboundDecision.Broadcast, guard.EvaluateLocalChange(Local("uno")));
        Assert.Equal(OutboundDecision.Broadcast, guard.EvaluateLocalChange(Local("dos")));
        Assert.Equal(OutboundDecision.Broadcast, guard.EvaluateLocalChange(Local("tres")));
    }

    [Fact]
    public void Change_with_remote_origin_marker_is_not_broadcast()
    {
        var guard = new ClipboardEchoGuard();
        var change = new ClipboardChange(ClipboardContent.FromText("Hola"), HasRemoteOriginMarker: true, IsExcludedByOwner: false);

        Assert.Equal(OutboundDecision.SkipRemoteOrigin, guard.EvaluateLocalChange(change));
    }

    [Fact]
    public void Remote_content_echo_is_suppressed_even_if_marker_was_stripped()
    {
        var guard = new ClipboardEchoGuard();
        guard.RegisterRemoteContent(ClipboardContent.FromText("Hola"));

        // e.g. RDP clipboard redirection re-publishes the same text without our marker.
        Assert.Equal(OutboundDecision.SkipDuplicate, guard.EvaluateLocalChange(Local("Hola")));
    }

    [Fact]
    public void Same_text_copied_again_after_receiving_other_text_is_broadcast()
    {
        var guard = new ClipboardEchoGuard();

        Assert.Equal(OutboundDecision.Broadcast, guard.EvaluateLocalChange(Local("x")));
        guard.RegisterRemoteContent(ClipboardContent.FromText("y"));

        // Shared clipboard now holds "y": copying "x" again is a real change.
        Assert.Equal(OutboundDecision.Broadcast, guard.EvaluateLocalChange(Local("x")));
    }

    [Fact]
    public void Re_copying_identical_text_is_not_resent()
    {
        var guard = new ClipboardEchoGuard();

        Assert.Equal(OutboundDecision.Broadcast, guard.EvaluateLocalChange(Local("x")));
        Assert.Equal(OutboundDecision.SkipDuplicate, guard.EvaluateLocalChange(Local("x")));
    }

    [Fact]
    public void Excluded_empty_unsupported_and_oversized_changes_are_skipped()
    {
        var guard = new ClipboardEchoGuard(maxContentBytes: 8);

        Assert.Equal(OutboundDecision.SkipExcludedByOwner,
            guard.EvaluateLocalChange(new ClipboardChange(ClipboardContent.FromText("secreto"), false, IsExcludedByOwner: true)));
        Assert.Equal(OutboundDecision.SkipEmpty, guard.EvaluateLocalChange(Local(string.Empty)));
        Assert.Equal(OutboundDecision.SkipUnsupported, guard.EvaluateLocalChange(new ClipboardChange(null, false, false)));
        Assert.Equal(OutboundDecision.SkipTooLarge, guard.EvaluateLocalChange(Local("123456789")));
        Assert.Equal(OutboundDecision.SkipTooLarge, guard.EvaluateLocalChange(new ClipboardChange(null, false, false, IsTooLarge: true)));
    }

    /// <summary>
    /// Simulates two devices wired back-to-back, where applying remote content raises a local clipboard
    /// event exactly like Windows does. A loop would show up as unbounded message traffic.
    /// </summary>
    [Fact]
    public void Two_devices_do_not_ping_pong()
    {
        var a = new SimulatedDevice("A");
        var b = new SimulatedDevice("B");
        a.Peer = b;
        b.Peer = a;

        a.UserCopies("Hola");
        b.UserCopies("Respuesta");
        a.UserCopies("Hola de nuevo");

        Assert.Equal("Hola de nuevo", a.ClipboardText);
        Assert.Equal("Hola de nuevo", b.ClipboardText);
        Assert.Equal(2, a.Sent);
        Assert.Equal(1, b.Sent);
    }

    [Fact]
    public void Two_devices_do_not_ping_pong_when_marker_is_lost()
    {
        var a = new SimulatedDevice("A", markerSurvives: false);
        var b = new SimulatedDevice("B", markerSurvives: false);
        a.Peer = b;
        b.Peer = a;

        a.UserCopies("Hola");
        b.UserCopies("Hola B");

        Assert.Equal("Hola B", a.ClipboardText);
        Assert.Equal(1, a.Sent);
        Assert.Equal(1, b.Sent);
    }

    private sealed class SimulatedDevice(string name, bool markerSurvives = true)
    {
        private readonly ClipboardEchoGuard _guard = new();
        private int _depth;

        public SimulatedDevice? Peer { get; set; }

        public string? ClipboardText { get; private set; }

        public int Sent { get; private set; }

        public void UserCopies(string text)
        {
            ClipboardText = text;
            OnClipboardChanged(new ClipboardChange(ClipboardContent.FromText(text), false, false));
        }

        public void Receive(ClipboardContent content)
        {
            _guard.RegisterRemoteContent(content);
            ClipboardText = content.GetText();
            // Writing the clipboard triggers WM_CLIPBOARDUPDATE on the same device.
            OnClipboardChanged(new ClipboardChange(content, HasRemoteOriginMarker: markerSurvives, false));
        }

        private void OnClipboardChanged(ClipboardChange change)
        {
            if (++_depth > 10)
            {
                throw new InvalidOperationException($"Synchronization loop detected on {name}.");
            }

            if (_guard.EvaluateLocalChange(change) == OutboundDecision.Broadcast)
            {
                Sent++;
                Peer!.Receive(change.Content!);
            }

            _depth--;
        }
    }
}

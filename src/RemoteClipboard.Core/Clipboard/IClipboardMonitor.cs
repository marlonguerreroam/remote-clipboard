namespace RemoteClipboard.Core.Clipboard;

/// <summary>Event-driven clipboard change notifications (no polling). Disposing stops monitoring.</summary>
public interface IClipboardMonitor : IDisposable
{
    event EventHandler<ClipboardChange>? ClipboardChanged;

    void Start();
}

namespace RemoteClipboard.Core.Clipboard;

/// <summary>Snapshot raised by <see cref="IClipboardMonitor"/> when the OS clipboard changes.</summary>
/// <param name="Content">The supported content, or null when the clipboard holds no supported format.</param>
/// <param name="HasRemoteOriginMarker">True when the clipboard carries Remote Clipboard's private origin marker
/// (i.e. it was written by this application after receiving remote content).</param>
/// <param name="IsExcludedByOwner">True when the source application asked monitors to ignore the content
/// (e.g. password managers via ExcludeClipboardContentFromMonitorProcessing).</param>
public sealed record ClipboardChange(
    ClipboardContent? Content,
    bool HasRemoteOriginMarker,
    bool IsExcludedByOwner);

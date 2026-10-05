namespace RemoteClipboard.Core.Clipboard;

/// <summary>Writes remote content to the local clipboard, tagged with the remote-origin marker.</summary>
public interface IClipboardWriter
{
    Task WriteRemoteContentAsync(ClipboardContent content, CancellationToken cancellationToken);
}

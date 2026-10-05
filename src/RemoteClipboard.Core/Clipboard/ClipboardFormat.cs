namespace RemoteClipboard.Core.Clipboard;

/// <summary>
/// Wire-level clipboard payload formats. Values are part of the protocol: never renumber.
/// Only <see cref="Text"/> is supported in the MVP.
/// </summary>
public enum ClipboardFormat
{
    Unknown = 0,

    /// <summary>Plain Unicode text, UTF-8 encoded on the wire (CF_UNICODETEXT on Windows).</summary>
    Text = 1,

    // Reserved for future phases:
    // RichText = 2, Html = 3, Image = 4, FileList = 5
}

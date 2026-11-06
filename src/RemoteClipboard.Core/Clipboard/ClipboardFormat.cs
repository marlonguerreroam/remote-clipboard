// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Clipboard;

/// <summary>
/// Wire-level clipboard payload formats. Values are part of the protocol: never renumber. Peers announce
/// which formats they accept in <c>HelloMessage.Accepts</c>; text is always accepted.
/// </summary>
public enum ClipboardFormat
{
    Unknown = 0,

    /// <summary>Plain Unicode text, UTF-8 encoded on the wire (CF_UNICODETEXT on Windows).</summary>
    Text = 1,

    // Reserved: RichText = 2, Html = 3.

    /// <summary>A bitmap image, PNG-encoded on the wire (screenshots, images copied from apps).</summary>
    Image = 4,

    /// <summary>Files and folders (copied in Explorer), as a ZIP archive on the wire (see <c>FileArchive</c>).</summary>
    FileList = 5,
}

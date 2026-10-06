// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Clipboard;

/// <summary>Writes remote content to the local clipboard, tagged with the remote-origin marker.</summary>
public interface IClipboardWriter
{
    Task WriteRemoteContentAsync(ClipboardContent content, CancellationToken cancellationToken);
}

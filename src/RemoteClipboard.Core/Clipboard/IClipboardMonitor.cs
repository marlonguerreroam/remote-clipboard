// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Clipboard;

/// <summary>Event-driven clipboard change notifications (no polling). Disposing stops monitoring.</summary>
public interface IClipboardMonitor : IDisposable
{
    event EventHandler<ClipboardChange>? ClipboardChanged;

    void Start();
}

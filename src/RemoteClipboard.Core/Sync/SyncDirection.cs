// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Sync;

/// <summary>
/// Local, per-peer policy: what THIS device does with that peer. Each side decides for itself
/// (e.g. a server set to <see cref="ReceiveOnly"/> never sends its clipboard to anyone).
/// </summary>
public enum SyncDirection
{
    Bidirectional = 0,
    SendOnly = 1,
    ReceiveOnly = 2,
}

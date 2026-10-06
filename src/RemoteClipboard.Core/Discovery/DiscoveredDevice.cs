// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Devices;

namespace RemoteClipboard.Core.Discovery;

/// <summary>A device seen on the LAN. Unauthenticated: use only as an address hint and for display.</summary>
public sealed record DiscoveredDevice(
    DeviceId Id,
    string DisplayName,
    string OsDescription,
    string Address,
    int Port,
    bool AcceptingPairing,
    DateTimeOffset LastSeenUtc);

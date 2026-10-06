// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Networking;

/// <summary>What this device announces to peers (public information only).</summary>
public sealed record LocalDeviceInfo(string DisplayName, string OsDescription);

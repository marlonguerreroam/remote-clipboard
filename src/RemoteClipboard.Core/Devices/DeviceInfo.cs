// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Devices;

/// <summary>Public, non-secret description of a device as shown in the UI and announced to peers.</summary>
public sealed record DeviceInfo(DeviceId Id, string DisplayName, string OsDescription);

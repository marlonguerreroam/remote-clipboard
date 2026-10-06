// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Devices;

/// <summary>
/// What an identity is bound to: this OS installation (Windows MachineGuid) and this user (SID).
/// If either changes (cloned VM/disk, copied profile) the identity is regenerated, so two machines
/// never present the same DeviceId and keys.
/// </summary>
public sealed record MachineBinding(string MachineId, string UserId);

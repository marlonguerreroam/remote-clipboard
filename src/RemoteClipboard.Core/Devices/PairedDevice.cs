// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Security;
using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.Core.Devices;

/// <summary>
/// A peer that completed pairing. Trust is anchored on <see cref="CertificatePin"/>
/// (SHA-256 of the peer's public key), never on its IP address or name.
/// </summary>
public sealed record PairedDevice(
    DeviceId Id,
    string DisplayName,
    CertificatePin CertificatePin,
    DateTimeOffset PairedAtUtc,
    SyncDirection Direction = SyncDirection.Bidirectional,
    string? LastKnownHost = null,
    int? LastKnownPort = null,
    string? OsDescription = null);

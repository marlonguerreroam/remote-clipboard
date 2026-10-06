// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Devices;

namespace RemoteClipboard.Core.Pairing;

public enum PairingStatus
{
    Success,
    WrongCode,
    Expired,
    LockedOut,
    NotAccepting,
    Unreachable,
    Failed,
}

public sealed record PairingOutcome(PairingStatus Status, PairedDevice? Device = null)
{
    public bool Succeeded => Status == PairingStatus.Success;
}

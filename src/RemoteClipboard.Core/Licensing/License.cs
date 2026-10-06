// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Licensing;

public enum LicenseEdition
{
    Personal,
    Business,
}

/// <summary>A purchased license. Only what the buyer agreed to show: no e-mail, no machine data.</summary>
public sealed record License(string Id, string Licensee, LicenseEdition Edition, DateOnly Issued);

public enum LicenseCheckStatus
{
    Valid,
    Malformed,
    UnsupportedVersion,
    BadSignature,
}

public sealed record LicenseCheck(LicenseCheckStatus Status, License? License = null)
{
    public bool IsValid => Status == LicenseCheckStatus.Valid && License is not null;
}

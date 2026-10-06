// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;

namespace RemoteClipboard.Core.Licensing;

/// <summary>
/// Public licensing parameters of the official build. The PUBLIC key is not a secret; its private
/// counterpart is generated and kept by the author with tools/RemoteClipboard.LicenseTool and must never
/// be committed. While the public key is empty, licensing is off (no trial limit), e.g. in forks.
/// </summary>
public static class LicensingConfig
{
    /// <summary>SubjectPublicKeyInfo (base64) printed by <c>LicenseTool keygen</c>.</summary>
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEh8nb/LS6jXKXb74kcYRhfuy1FmRuv35Wg9PDn8oeCZOo/bPpVVo5gvKTnYqbUjFIbxptVlFc/x+2jHGuOsbMrQ==";

    /// <summary>Purchase page; the "Buy" button is hidden while empty.</summary>
    public const string PurchaseUrl = "";

    public const int TrialDays = 14;

    /// <summary>The embedded public key, or null when licensing is off.</summary>
    public static ECDsa? CreatePublicKey()
    {
        if (string.IsNullOrWhiteSpace(PublicKey))
        {
            return null;
        }

        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
        return key;
    }
}

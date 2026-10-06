// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteClipboard.Core.Security;

/// <summary>
/// SHA-256 of a certificate's SubjectPublicKeyInfo. Peers are trusted by pin (exchanged during
/// pairing), not by a CA chain, so self-signed device certificates are safe to use.
/// </summary>
public readonly record struct CertificatePin
{
    private readonly string _hex;

    private CertificatePin(string hex) => _hex = hex;

    public string Hex => _hex ?? string.Empty;

    public static CertificatePin FromCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var spki = certificate.PublicKey.ExportSubjectPublicKeyInfo();
        return new CertificatePin(Convert.ToHexString(SHA256.HashData(spki)));
    }

    public static CertificatePin Parse(string hex)
    {
        if (!TryParse(hex, out var pin))
        {
            throw new FormatException("A certificate pin is 64 hexadecimal characters (SHA-256).");
        }

        return pin;
    }

    public static bool TryParse(string? hex, out CertificatePin pin)
    {
        pin = default;
        if (hex is null || hex.Length != 64)
        {
            return false;
        }

        try
        {
            pin = new CertificatePin(Convert.ToHexString(Convert.FromHexString(hex)));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Constant-time comparison against a presented certificate.</summary>
    public bool Matches(X509Certificate2? certificate)
    {
        if (certificate is null || string.IsNullOrEmpty(_hex))
        {
            return false;
        }

        var presented = FromCertificate(certificate);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(_hex), Convert.FromHexString(presented.Hex));
    }

    /// <summary>Short form for UI verification, e.g. "3F2A-91C0-77B1-0D4E".</summary>
    public string ToShortDisplay()
    {
        var hex = Hex;
        return hex.Length < 16 ? hex : $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
    }

    public override string ToString() => Hex;
}

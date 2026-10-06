// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteClipboard.Core.Licensing;

/// <summary>
/// Offline license keys: <c>RC1-&lt;payload&gt;.&lt;signature&gt;</c>, both base64url. The payload is a small
/// JSON document; the signature is ECDSA P-256 / SHA-256 (IEEE P1363) made with the author's private key,
/// which never leaves the author's computer. The app only embeds the public key, so it can check a key
/// without contacting any server.
/// </summary>
public static class LicenseKeyFormat
{
    public const string Prefix = "RC1-";
    public const int MaxLicenseeLength = 100;
    private const int Version = 1;
    private const int MaxKeyLength = 2048;

    public static string Issue(License license, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(privateKey);
        var licensee = NormalizeLicensee(license.Licensee)
            ?? throw new ArgumentException("Licensee must be 1-100 printable characters.", nameof(license));

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("v", Version);
            json.WriteString("id", license.Id);
            json.WriteString("to", licensee);
            json.WriteString("ed", license.Edition == LicenseEdition.Business ? "business" : "personal");
            json.WriteString("iat", license.Issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        var payload = buffer.ToArray();
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Prefix + Base64Url.EncodeToString(payload) + "." + Base64Url.EncodeToString(signature);
    }

    public static LicenseCheck Verify(string? key, ECDsa publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            return new LicenseCheck(LicenseCheckStatus.Malformed);
        }

        // Keys are often pasted from e-mails with line breaks or spaces.
        var compact = string.Concat(key.Where(c => !char.IsWhiteSpace(c)));
        if (!compact.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return new LicenseCheck(LicenseCheckStatus.Malformed);
        }

        var parts = compact[Prefix.Length..].Split('.');
        if (parts.Length != 2
            || !TryDecode(parts[0], out var payload)
            || !TryDecode(parts[1], out var signature))
        {
            return new LicenseCheck(LicenseCheckStatus.Malformed);
        }

        if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            return new LicenseCheck(LicenseCheckStatus.BadSignature);
        }

        return ParsePayload(payload);
    }

    /// <summary>Trims and validates a licensee name; null when unusable.</summary>
    public static string? NormalizeLicensee(string? licensee)
    {
        var trimmed = licensee?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLicenseeLength || trimmed.Any(char.IsControl)
            ? null
            : trimmed;
    }

    private static LicenseCheck ParsePayload(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number)
            {
                return new LicenseCheck(LicenseCheckStatus.Malformed);
            }

            if (version.GetInt32() != Version)
            {
                return new LicenseCheck(LicenseCheckStatus.UnsupportedVersion);
            }

            var id = root.GetProperty("id").GetString();
            var licensee = NormalizeLicensee(root.GetProperty("to").GetString());
            var edition = root.GetProperty("ed").GetString() switch
            {
                "personal" => (LicenseEdition?)LicenseEdition.Personal,
                "business" => LicenseEdition.Business,
                _ => null,
            };
            var issuedOk = DateOnly.TryParseExact(root.GetProperty("iat").GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var issued);

            return string.IsNullOrWhiteSpace(id) || id.Length > 64 || licensee is null || edition is null || !issuedOk
                ? new LicenseCheck(LicenseCheckStatus.Malformed)
                : new LicenseCheck(LicenseCheckStatus.Valid, new License(id, licensee, edition.Value, issued));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new LicenseCheck(LicenseCheckStatus.Malformed);
        }
    }

    private static bool TryDecode(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length == 0)
        {
            return false;
        }

        try
        {
            bytes = Base64Url.DecodeFromChars(text);
            // Canonical form only: the unused low bits of the last character must be zero, so a given
            // license has exactly one valid spelling.
            return bytes.Length > 0 && string.Equals(Base64Url.EncodeToString(bytes), text, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RemoteClipboard.Core.Devices;

namespace RemoteClipboard.Core.Security;

/// <summary>
/// Creates the per-device TLS identity: a self-signed ECDSA P-256 certificate used for mutual TLS.
/// The private key never leaves the device; only its pin is shared during pairing.
/// </summary>
public static class DeviceCertificateFactory
{
    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";

    public static readonly TimeSpan Validity = TimeSpan.FromDays(3650);

    public static X509Certificate2 Create(DeviceId deviceId, TimeProvider? time = null)
    {
        if (deviceId.IsEmpty)
        {
            throw new ArgumentException("Device id must not be empty.", nameof(deviceId));
        }

        var now = (time ?? TimeProvider.System).GetUtcNow();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=RemoteClipboard {deviceId}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid(ServerAuthOid), new Oid(ClientAuthOid)], critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        return request.CreateSelfSigned(now.AddDays(-1), now.Add(Validity));
    }

    /// <summary>
    /// Serializes certificate + private key as PKCS#12 for storage. The output is a secret: callers
    /// must protect it at rest (DPAPI on Windows) and zero it after use.
    /// </summary>
    public static byte[] ExportWithPrivateKey(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException("Certificate has no private key.", nameof(certificate));
        }

        return certificate.Export(X509ContentType.Pkcs12);
    }

    /// <summary>
    /// Loads a stored identity. Uses a persisted (non-ephemeral) key set because Windows SChannel
    /// cannot use ephemeral keys for TLS server authentication.
    /// </summary>
    public static X509Certificate2 ImportWithPrivateKey(ReadOnlySpan<byte> pkcs12)
    {
        var flags = OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.DefaultKeySet;
        return X509CertificateLoader.LoadPkcs12(pkcs12, password: null, flags);
    }
}

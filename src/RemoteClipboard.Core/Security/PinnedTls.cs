using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace RemoteClipboard.Core.Security;

/// <summary>
/// Mutual-TLS configuration for device-to-device channels. Both ends present their self-signed device
/// certificate; trust is decided exclusively by public-key pin (from pairing). CA chain and host name
/// errors are expected for self-signed certificates and are intentionally not used for trust.
/// Revocation checking is disabled: there is no CA, and it would cause Internet traffic.
/// The TLS version is left to the OS (TLS 1.3 where available, otherwise TLS 1.2).
/// </summary>
public static class PinnedTls
{
    /// <summary>SNI/target host placeholder. Not used for trust.</summary>
    public const string TargetHost = "remote-clipboard.local";

    public static SslServerAuthenticationOptions CreateServerOptions(
        X509Certificate2 localCertificate,
        Func<X509Certificate2, bool> isAuthorizedPeer)
    {
        ArgumentNullException.ThrowIfNull(localCertificate);
        ArgumentNullException.ThrowIfNull(isAuthorizedPeer);

        return new SslServerAuthenticationOptions
        {
            ServerCertificateContext = SslStreamCertificateContext.Create(localCertificate, additionalCertificates: null, offline: true),
            ClientCertificateRequired = true,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            RemoteCertificateValidationCallback = (_, certificate, _, _) => Validate(certificate, isAuthorizedPeer),
        };
    }

    public static SslClientAuthenticationOptions CreateClientOptions(
        X509Certificate2 localCertificate,
        Func<X509Certificate2, bool> isExpectedPeer)
    {
        ArgumentNullException.ThrowIfNull(localCertificate);
        ArgumentNullException.ThrowIfNull(isExpectedPeer);

        return new SslClientAuthenticationOptions
        {
            TargetHost = TargetHost,
            ClientCertificateContext = SslStreamCertificateContext.Create(localCertificate, additionalCertificates: null, offline: true),
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            RemoteCertificateValidationCallback = (_, certificate, _, _) => Validate(certificate, isExpectedPeer),
        };
    }

    public static SslClientAuthenticationOptions CreateClientOptions(X509Certificate2 localCertificate, CertificatePin expectedPeer) =>
        CreateClientOptions(localCertificate, expectedPeer.Matches);

    private static bool Validate(X509Certificate? certificate, Func<X509Certificate2, bool> predicate)
    {
        if (certificate is null)
        {
            return false;
        }

        if (certificate is X509Certificate2 certificate2)
        {
            return predicate(certificate2);
        }

        using var loaded = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return predicate(loaded);
    }
}

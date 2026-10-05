using System.Security.Cryptography.X509Certificates;
using RemoteClipboard.Core.Security;

namespace RemoteClipboard.Core.Devices;

/// <summary>This device's identity: stable id plus the TLS certificate (with private key).</summary>
public sealed class DeviceIdentity : IDisposable
{
    public DeviceIdentity(DeviceId id, X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        Id = id;
        Certificate = certificate;
        Pin = CertificatePin.FromCertificate(certificate);
    }

    public DeviceId Id { get; }

    public X509Certificate2 Certificate { get; }

    public CertificatePin Pin { get; }

    public void Dispose() => Certificate.Dispose();
}

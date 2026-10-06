// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Security;

namespace RemoteClipboard.Core.Tests.Security;

public class CertificatePinTests
{
    [Fact]
    public void Pin_survives_export_and_import()
    {
        var pfx = DeviceCertificateFactory.CreatePkcs12(DeviceId.New());
        using var original = DeviceCertificateFactory.ImportWithPrivateKey(pfx);
        using var reloaded = DeviceCertificateFactory.ImportWithPrivateKey(pfx);

        Assert.True(reloaded.HasPrivateKey);
        Assert.Equal(CertificatePin.FromCertificate(original), CertificatePin.FromCertificate(reloaded));
    }

    [Fact]
    public void Different_devices_have_different_pins()
    {
        using var a = DeviceCertificateFactory.Create(DeviceId.New());
        using var b = DeviceCertificateFactory.Create(DeviceId.New());

        Assert.NotEqual(CertificatePin.FromCertificate(a), CertificatePin.FromCertificate(b));
        Assert.False(CertificatePin.FromCertificate(a).Matches(b));
        Assert.True(CertificatePin.FromCertificate(a).Matches(a));
        Assert.False(CertificatePin.FromCertificate(a).Matches(null));
    }

    [Fact]
    public void Pin_parses_and_formats()
    {
        using var cert = DeviceCertificateFactory.Create(DeviceId.New());
        var pin = CertificatePin.FromCertificate(cert);

        Assert.Equal(64, pin.Hex.Length);
        Assert.Equal(pin, CertificatePin.Parse(pin.Hex.ToLowerInvariant()));
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", pin.ToShortDisplay());
        Assert.False(CertificatePin.TryParse("zz", out _));
        Assert.False(CertificatePin.TryParse(new string('g', 64), out _));
    }

    [Fact]
    public void Default_pin_matches_nothing()
    {
        using var cert = DeviceCertificateFactory.Create(DeviceId.New());

        Assert.False(default(CertificatePin).Matches(cert));
    }

    [Fact]
    public void Empty_device_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DeviceCertificateFactory.Create(default));
    }
}

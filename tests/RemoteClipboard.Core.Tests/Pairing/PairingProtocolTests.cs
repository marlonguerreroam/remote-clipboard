// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Sockets;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Networking;
using RemoteClipboard.Core.Pairing;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Security;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Pairing;

/// <summary>Protocol-level tests on a plain socket pair; pins are injected to simulate what each side sees.</summary>
public sealed class PairingProtocolTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly DeviceIdentity _host = new DeviceIdentityStore(new InMemorySecretStore(), new("m", "host")).LoadOrCreate(out _);
    private readonly DeviceIdentity _joiner = new DeviceIdentityStore(new InMemorySecretStore(), new("m", "joiner")).LoadOrCreate(out _);
    private readonly DeviceIdentity _mitm = new DeviceIdentityStore(new InMemorySecretStore(), new("m", "mitm")).LoadOrCreate(out _);

    public void Dispose()
    {
        _host.Dispose();
        _joiner.Dispose();
        _mitm.Dispose();
    }

    private async Task<(bool HostAccepted, PairingStatus JoinerStatus, PairingAttemptResult? Attempt)> RunAsync(
        string hostCode, string joinerCode, CertificatePin joinerSeesHostPin, CertificatePin hostSeesJoinerPin)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var joinerSocket = new TcpClient();
        await joinerSocket.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, Ct);
        using var hostSocket = await listener.AcceptTcpClientAsync(Ct);

        PairingAttemptResult? attempt = null;
        var info = new LocalDeviceInfo("X", "Test OS");
        var joinerTask = PairingProtocol.RunJoinerAsync(joinerSocket.GetStream(), joinerCode, _joiner, info, 1, joinerSeesHostPin, Ct);
        var request = Assert.IsType<PairingRequestMessage>(await FrameCodec.ReadAsync(hostSocket.GetStream(), Ct));
        var hostTask = PairingProtocol.RunHostAsync(hostSocket.GetStream(), hostCode, _host, info, 2, request, hostSeesJoinerPin,
            ok => (attempt = ok ? PairingAttemptResult.Accepted : PairingAttemptResult.Rejected).Value, Ct);

        var (status, _) = await joinerTask.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        return (await hostTask.WaitAsync(TimeSpan.FromSeconds(30), Ct), status, attempt);
    }

    [Fact]
    public async Task Same_code_and_same_view_of_certificates_succeeds()
    {
        var result = await RunAsync("847291", "847291", _host.Pin, _joiner.Pin);

        Assert.True(result.HostAccepted);
        Assert.Equal(PairingStatus.Success, result.JoinerStatus);
    }

    [Fact]
    public async Task Wrong_code_fails_on_both_sides()
    {
        var result = await RunAsync("847291", "847292", _host.Pin, _joiner.Pin);

        Assert.False(result.HostAccepted);
        Assert.Equal(PairingStatus.WrongCode, result.JoinerStatus);
        Assert.Equal(PairingAttemptResult.Rejected, result.Attempt);
    }

    [Fact]
    public async Task Man_in_the_middle_with_its_own_certificates_is_detected_even_with_the_right_code()
    {
        // The attacker relays messages but terminates TLS itself, so each side sees the attacker's pin.
        var result = await RunAsync("847291", "847291", joinerSeesHostPin: _mitm.Pin, hostSeesJoinerPin: _mitm.Pin);

        Assert.False(result.HostAccepted);
        Assert.NotEqual(PairingStatus.Success, result.JoinerStatus);
    }
}

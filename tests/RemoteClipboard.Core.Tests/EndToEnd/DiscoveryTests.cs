using System.Net;
using System.Net.Sockets;
using System.Text;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Pairing;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.EndToEnd;

public class DiscoveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task PairAsync(TestAgent host, TestAgent joiner)
    {
        var window = host.Agent.OpenPairing();
        Assert.Equal(PairingStatus.Success, (await joiner.Agent.PairWithAsync("127.0.0.1", host.Port, window.Code, Ct)).Status);
        host.Agent.ClosePairing();
        await Eventually.TrueAsync(() => host.IsConnectedTo(joiner) && joiner.IsConnectedTo(host), "both devices are connected");
    }

    [Fact]
    public async Task Devices_see_each_other_and_who_is_ready_to_pair()
    {
        var lan = new TestLan();
        await using var a = TestAgent.Start("PC-A", lan: lan);
        await using var b = TestAgent.Start("PC-B", lan: lan);

        await Eventually.TrueAsync(() => b.Agent.DiscoveredDevices.Any(d => d.Id == a.Agent.DeviceId), "B sees A");
        var seen = b.Agent.DiscoveredDevices.Single(d => d.Id == a.Agent.DeviceId);
        Assert.Equal("PC-A", seen.DisplayName);
        Assert.Equal(a.Port, seen.Port);
        Assert.False(seen.AcceptingPairing);

        a.Agent.OpenPairing();
        await Eventually.TrueAsync(() => b.Agent.DiscoveredDevices.Single(d => d.Id == a.Agent.DeviceId).AcceptingPairing, "B sees that A shows a code");
    }

    [Fact]
    public async Task Paired_devices_leave_the_discovery_list_and_departed_devices_expire()
    {
        var lan = new TestLan();
        await using var a = TestAgent.Start("PC-A", lan: lan);
        await using var b = TestAgent.Start("PC-B", lan: lan);
        await using var c = TestAgent.Start("PC-C", lan: lan);
        await Eventually.TrueAsync(() => b.Agent.DiscoveredDevices.Count == 2, "B sees A and C");

        await PairAsync(a, b);
        Assert.DoesNotContain(b.Agent.DiscoveredDevices, d => d.Id == a.Agent.DeviceId);

        await c.StopAsync();
        await Eventually.TrueAsync(() => b.Agent.DiscoveredDevices.Count == 0, "C expires after leaving", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Reconnects_even_when_both_devices_changed_address()
    {
        var lan = new TestLan();
        await using var a = TestAgent.Start("PC-A", lan: lan);
        await using var b = TestAgent.Start("PC-B", lan: lan);
        await PairAsync(a, b);

        // Both go away and come back on new ports: the stored addresses are stale on both sides.
        await a.StopAsync();
        await b.StopAsync();
        await a.RestartAsync();
        await b.RestartAsync();

        await Eventually.TrueAsync(() => a.IsConnectedTo(b) && b.IsConnectedTo(a), "discovery lets them find each other again", TimeSpan.FromSeconds(20));
        a.Clipboard.UserCopies("encontrados de nuevo");
        await Eventually.TrueAsync(() => b.Clipboard.Text == "encontrados de nuevo", "sync works");
        Assert.Equal(b.Port, a.Peers.Find(b.Agent.DeviceId)!.LastKnownPort);
    }

    [Fact]
    public async Task Spoofed_announcement_cannot_redirect_or_poison_a_paired_device()
    {
        var lan = new TestLan();
        await using var a = TestAgent.Start("PC-A", lan: lan);
        await using var b = TestAgent.Start("PC-B", lan: lan);
        await PairAsync(a, b);
        var stored = a.Peers.Find(b.Agent.DeviceId)!;

        // An attacker on the LAN claims to be B at its own address.
        using var attacker = new TcpListener(IPAddress.Loopback, 0);
        attacker.Start();
        var attackerPort = ((IPEndPoint)attacker.LocalEndpoint).Port;
        await b.StopAsync();
        await Eventually.TrueAsync(() => !a.IsConnectedTo(b), "B is offline");
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var fake = Encoding.UTF8.GetBytes($$"""{"v":1,"t":"announce","id":"{{b.Agent.DeviceId}}","name":"PC-B","os":"Windows 11","port":{{attackerPort}},"pairing":false}""");
        foreach (var target in LanTargets(lan))
        {
            await udp.SendAsync(fake, target, Ct);
        }

        // A may try the attacker's address, but TLS rejects it: no connection, stored address unchanged.
        await Task.Delay(1500, Ct);
        Assert.False(a.IsConnectedTo(b));
        var after = a.Peers.Find(b.Agent.DeviceId)!;
        Assert.Equal(stored.LastKnownHost, after.LastKnownHost);
        Assert.Equal(stored.LastKnownPort, after.LastKnownPort);
        Assert.Equal(stored.CertificatePin, after.CertificatePin);
    }

    [Fact]
    public async Task Malformed_datagrams_are_ignored()
    {
        var lan = new TestLan();
        await using var a = TestAgent.Start("PC-A", lan: lan);
        using var udp = new UdpClient(AddressFamily.InterNetwork);

        string[] junk =
        [
            "not json",
            """{"v":2,"t":"announce","id":"8f0b2a9c-1f6e-4a52-9b8e-2d0c7c5f1a11","port":47800}""",
            """{"v":1,"t":"announce","id":"8f0b2a9c-1f6e-4a52-9b8e-2d0c7c5f1a11","port":0}""",
            """{"v":1,"t":"announce","id":"00000000-0000-0000-0000-000000000000","port":47800}""",
            $$"""{"v":1,"t":"announce","id":"{{a.Agent.DeviceId}}","port":47800}""",
            new string('x', 5000),
        ];
        foreach (var payload in junk)
        {
            foreach (var target in LanTargets(lan))
            {
                await udp.SendAsync(Encoding.UTF8.GetBytes(payload), target, Ct);
            }
        }

        await Task.Delay(800, Ct);
        Assert.Empty(a.Agent.DiscoveredDevices);
    }

    [Fact]
    public async Task Long_untrusted_names_are_sanitized()
    {
        var lan = new TestLan();
        await using var a = TestAgent.Start("PC-A", lan: lan);
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var id = DeviceId.New();
        var name = new string('N', 200) + "\\u0007"; // JSON escape: decodes to the BEL control character
        var payload = Encoding.UTF8.GetBytes($$"""{"v":1,"t":"announce","id":"{{id}}","name":"{{name}}","port":47800}""");
        foreach (var target in LanTargets(lan))
        {
            await udp.SendAsync(payload, target, Ct);
        }

        await Eventually.TrueAsync(() => a.Agent.DiscoveredDevices.Any(d => d.Id == id), "A lists the device");
        var device = a.Agent.DiscoveredDevices.Single(d => d.Id == id);
        Assert.Equal(64, device.DisplayName.Length);
        Assert.DoesNotContain('\u0007', device.DisplayName);
    }

    private static IEnumerable<IPEndPoint> LanTargets(TestLan lan) => lan.Members;
}

// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Pairing;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Security;
using RemoteClipboard.Core.Sync;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.EndToEnd;

/// <summary>Two or three real agents talking TLS over loopback, with fake clipboards.</summary>
public class EndToEndTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task PairAsync(TestAgent host, TestAgent joiner)
    {
        var window = host.Agent.OpenPairing();
        var outcome = await joiner.Agent.PairWithAsync("127.0.0.1", host.Port, window.Code, Ct);
        Assert.Equal(PairingStatus.Success, outcome.Status);
        await Eventually.TrueAsync(() => host.IsConnectedTo(joiner) && joiner.IsConnectedTo(host), "both devices are connected");
    }

    [Fact]
    public async Task Pairing_then_bidirectional_sync()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);

        Assert.Equal("PC-B", a.Peers.Find(b.Agent.DeviceId)!.DisplayName);
        Assert.Equal("PC-A", b.Peers.Find(a.Agent.DeviceId)!.DisplayName);

        a.Clipboard.UserCopies("Hola, este es un texto de prueba.");
        await Eventually.TrueAsync(() => b.Clipboard.Text == "Hola, este es un texto de prueba.", "A → B");

        b.Clipboard.UserCopies("Respuesta desde B ✅ 🚀\r\nsegunda línea, ñandú");
        await Eventually.TrueAsync(() => a.Clipboard.Text == "Respuesta desde B ✅ 🚀\r\nsegunda línea, ñandú", "B → A");
    }

    [Fact]
    public async Task Long_text_is_synchronized()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);

        var text = string.Concat(Enumerable.Repeat("Texto largo con acentos ñ y emojis 🚀 — ", 150_000)); // ~6.6 MB UTF-8
        a.Clipboard.UserCopies(text);

        await Eventually.TrueAsync(() => b.Clipboard.Text == text, "the long text arrives intact", TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Consecutive_changes_end_with_the_latest()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);

        for (var i = 1; i <= 20; i++)
        {
            a.Clipboard.UserCopies($"cambio {i}");
        }

        await Eventually.TrueAsync(() => b.Clipboard.Text == "cambio 20", "the last change wins");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // another app stripped the origin marker
    public async Task Synchronization_does_not_loop(bool markerSurvives)
    {
        await using var a = TestAgent.Start("PC-A", markerSurvives: markerSurvives);
        await using var b = TestAgent.Start("PC-B", markerSurvives: markerSurvives);
        await PairAsync(a, b);

        a.Clipboard.UserCopies("Hola");
        await Eventually.TrueAsync(() => b.Clipboard.Text == "Hola", "A → B");
        await Task.Delay(1000, Ct); // give a loop time to happen

        Assert.Equal(1, a.SentCount);
        Assert.Equal(0, b.SentCount);
        Assert.Equal(0, a.Clipboard.RemoteWrites);
        Assert.Equal(1, b.Clipboard.RemoteWrites);
    }

    [Fact]
    public async Task Wrong_codes_are_rejected_and_burn_the_invitation()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        var window = a.Agent.OpenPairing();
        var wrong = window.Code == "000000" ? "111111" : "000000";

        Assert.Equal(PairingStatus.WrongCode, (await b.Agent.PairWithAsync("127.0.0.1", a.Port, wrong, Ct)).Status);
        Assert.Equal(PairingStatus.WrongCode, (await b.Agent.PairWithAsync("127.0.0.1", a.Port, wrong, Ct)).Status);
        Assert.Equal(PairingStatus.LockedOut, (await b.Agent.PairWithAsync("127.0.0.1", a.Port, wrong, Ct)).Status);

        // Even the right code no longer works.
        Assert.NotEqual(PairingStatus.Success, (await b.Agent.PairWithAsync("127.0.0.1", a.Port, window.Code, Ct)).Status);
        Assert.False(a.Knows(b));
        Assert.False(b.Knows(a));
    }

    [Fact]
    public async Task Pairing_is_refused_when_no_invitation_is_open()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");

        var outcome = await b.Agent.PairWithAsync("127.0.0.1", a.Port, "123456", Ct);

        Assert.NotEqual(PairingStatus.Success, outcome.Status);
        Assert.False(a.Knows(b));
    }

    [Fact]
    public async Task Unpaired_device_cannot_connect_or_inject_clipboard()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var intruder = TestAgent.Start("INTRUDER");

        // The intruder learned A's public pin and address, but A never paired with it.
        intruder.Peers.AddOrUpdate(new PairedDevice(a.Agent.DeviceId, "PC-A", a.Identity.Pin, DateTimeOffset.UtcNow, LastKnownHost: "127.0.0.1", LastKnownPort: a.Port));
        intruder.Clipboard.UserCopies("contenido malicioso");
        await Task.Delay(1500, Ct);

        Assert.False(intruder.IsConnectedTo(a));
        Assert.Empty(a.Agent.Devices);
        Assert.Null(a.Clipboard.Text);
    }

    [Fact]
    public async Task Reconnects_after_restart_on_a_different_port()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);
        var oldPort = b.Port;

        await b.RestartAsync(); // app restart: same identity and peers, new port (like a new IP)
        Assert.NotEqual(oldPort, b.Port);
        await Eventually.TrueAsync(() => a.IsConnectedTo(b) && b.IsConnectedTo(a), "devices reconnect automatically");

        Assert.Equal(b.Port, a.Peers.Find(b.Agent.DeviceId)!.LastKnownPort);
        a.Clipboard.UserCopies("después del reinicio");
        await Eventually.TrueAsync(() => b.Clipboard.Text == "después del reinicio", "sync works after reconnecting");
    }

    [Fact]
    public async Task Remote_device_offline_then_back_online()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);
        var port = b.Port;

        await b.Agent.DisposeAsync(); // remote device switched off
        await Eventually.TrueAsync(() => !a.IsConnectedTo(b), "A notices the device is gone");
        a.Clipboard.UserCopies("copiado mientras B estaba apagado"); // must not throw or block

        await b.RestartAsync(port); // back on the same address
        await Eventually.TrueAsync(() => a.IsConnectedTo(b), "A reconnects by itself");
        a.Clipboard.UserCopies("B volvió");
        await Eventually.TrueAsync(() => b.Clipboard.Text == "B volvió", "sync resumes");
    }

    [Fact]
    public async Task Silent_peer_is_detected_by_heartbeat()
    {
        await using var a = TestAgent.Start("PC-A");
        using var silentCert = DeviceCertificateFactory.Create(DeviceId.New());
        var silentId = DeviceId.New();
        a.Peers.AddOrUpdate(new PairedDevice(silentId, "SILENT", CertificatePin.FromCertificate(silentCert), DateTimeOffset.UtcNow));

        // Completes the handshake, then stops answering, like a machine whose cable was pulled.
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, a.Port, Ct);
        await using var ssl = new SslStream(client.GetStream());
        await ssl.AuthenticateAsClientAsync(PinnedTls.CreateClientOptions(silentCert, a.Identity.Pin), Ct);
        await FrameCodec.WriteAsync(ssl, new HelloMessage(ProtocolLimits.ProtocolVersion, silentId.Value, "SILENT", "Test OS", 1), Ct);
        await FrameCodec.ReadAsync(ssl, Ct);

        await Eventually.TrueAsync(() => a.Agent.Devices.Single().IsConnected, "the silent peer is connected");
        await Eventually.TrueAsync(() => !a.Agent.Devices.Single().IsConnected, "the heartbeat timeout disconnects it", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Disabled_sync_neither_sends_nor_applies()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);

        b.Agent.SyncEnabled = false;
        a.Clipboard.UserCopies("no debe llegar");
        b.Clipboard.UserCopies("tampoco debe salir");
        await Task.Delay(1000, Ct);

        Assert.Equal("no debe llegar", a.Clipboard.Text);
        Assert.Equal("tampoco debe salir", b.Clipboard.Text);
    }

    [Fact]
    public async Task Receive_only_device_applies_but_never_sends()
    {
        await using var server = TestAgent.Start("SERVIDOR");
        await using var pc = TestAgent.Start("PC");
        await PairAsync(server, pc);

        // Changed while connected: must apply immediately.
        server.Agent.SetSyncDirection(pc.Agent.DeviceId, SyncDirection.ReceiveOnly);

        pc.Clipboard.UserCopies("hacia el servidor");
        await Eventually.TrueAsync(() => server.Clipboard.Text == "hacia el servidor", "server receives");
        server.Clipboard.UserCopies("no debe salir del servidor");
        await Task.Delay(800, Ct);
        Assert.Equal("hacia el servidor", pc.Clipboard.Text);
        Assert.Equal(SyncDirection.ReceiveOnly, server.Peers.Find(pc.Agent.DeviceId)!.Direction);
    }

    [Fact]
    public async Task Send_only_device_sends_but_ignores_incoming()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);
        a.Agent.SetSyncDirection(b.Agent.DeviceId, SyncDirection.SendOnly);

        a.Clipboard.UserCopies("sale de A");
        await Eventually.TrueAsync(() => b.Clipboard.Text == "sale de A", "A sends");
        b.Clipboard.UserCopies("A no debe aplicarlo");
        await Task.Delay(800, Ct);
        Assert.Equal("sale de A", a.Clipboard.Text);
    }

    [Fact]
    public async Task Unpairing_cuts_the_connection_and_trust()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await PairAsync(a, b);

        a.Agent.Unpair(b.Agent.DeviceId);

        await Eventually.TrueAsync(() => !b.IsConnectedTo(a), "B is disconnected");
        await Task.Delay(1000, Ct); // B keeps retrying; A must refuse every attempt
        Assert.False(b.IsConnectedTo(a));
        b.Clipboard.UserCopies("ya no vinculado");
        await Task.Delay(500, Ct);
        Assert.NotEqual("ya no vinculado", a.Clipboard.Text);
    }

    [Fact]
    public async Task Three_devices_receive_from_any_device()
    {
        await using var a = TestAgent.Start("PC-A");
        await using var b = TestAgent.Start("PC-B");
        await using var server = TestAgent.Start("SERVIDOR");
        await PairAsync(a, b);
        await PairAsync(server, a);
        await PairAsync(server, b);

        b.Clipboard.UserCopies("para todos");

        await Eventually.TrueAsync(() => a.Clipboard.Text == "para todos" && server.Clipboard.Text == "para todos", "all devices receive");
        await Task.Delay(500, Ct);
        Assert.Equal(1, b.SentCount);
        Assert.Equal(0, a.SentCount);
        Assert.Equal(0, server.SentCount);
    }

    [Fact]
    public async Task Logs_never_contain_clipboard_content_or_pairing_code()
    {
        var logs = new CapturingLoggerFactory();
        await using var a = TestAgent.Start("PC-A", logs);
        await using var b = TestAgent.Start("PC-B", logs);
        var window = a.Agent.OpenPairing();
        Assert.Equal(PairingStatus.Success, (await b.Agent.PairWithAsync("127.0.0.1", a.Port, window.Code, Ct)).Status);
        await Eventually.TrueAsync(() => a.IsConnectedTo(b), "connected");

        const string secret = "Mi contraseña es 123456";
        a.Clipboard.UserCopies(secret);
        await Eventually.TrueAsync(() => b.Clipboard.Text == secret, "synced");

        Assert.NotEmpty(logs.Lines);
        var all = string.Join('\n', logs.Lines);
        Assert.DoesNotContain(secret, all, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)), all, StringComparison.Ordinal);
        Assert.DoesNotContain(window.Code, all, StringComparison.Ordinal);
    }
}

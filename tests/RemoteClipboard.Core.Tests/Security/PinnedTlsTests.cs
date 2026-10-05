using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Security;

namespace RemoteClipboard.Core.Tests.Security;

/// <summary>Real TLS over loopback sockets: validates the pin-based mutual authentication design.</summary>
public sealed class PinnedTlsTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly X509Certificate2 _server = DeviceCertificateFactory.Create(DeviceId.New());
    private readonly X509Certificate2 _client = DeviceCertificateFactory.Create(DeviceId.New());
    private readonly X509Certificate2 _intruder = DeviceCertificateFactory.Create(DeviceId.New());

    public void Dispose()
    {
        _server.Dispose();
        _client.Dispose();
        _intruder.Dispose();
    }

    [Fact]
    public async Task Paired_devices_exchange_clipboard_over_mutual_tls()
    {
        var clientPin = CertificatePin.FromCertificate(_client);
        var serverPin = CertificatePin.FromCertificate(_server);
        var content = ClipboardContent.FromText("Hola, este es un texto de prueba. ✅");

        var (serverResult, clientResult) = await RunHandshakeAsync(
            PinnedTls.CreateServerOptions(_server, clientPin.Matches),
            PinnedTls.CreateClientOptions(_client, serverPin),
            async server =>
            {
                Assert.True(server.IsMutuallyAuthenticated);
                Assert.True(server.IsEncrypted);
                return await FrameCodec.ReadAsync(server, Ct);
            },
            async client =>
            {
                await FrameCodec.WriteAsync(client, new ClipboardUpdateMessage(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, content.Format, content.Data.ToArray()), Ct);
                return null;
            });

        Assert.Null(clientResult.Error);
        Assert.Null(serverResult.Error);
        var received = Assert.IsType<ClipboardUpdateMessage>(serverResult.Value);
        Assert.Equal(content.GetText(), ClipboardContent.FromBytes(received.Format, received.Data).GetText());
    }

    [Fact]
    public async Task Unauthorized_client_certificate_is_rejected_by_server()
    {
        var clientPin = CertificatePin.FromCertificate(_client);
        var serverPin = CertificatePin.FromCertificate(_server);

        var (serverResult, clientResult) = await RunHandshakeAsync(
            PinnedTls.CreateServerOptions(_server, clientPin.Matches),
            PinnedTls.CreateClientOptions(_intruder, serverPin),
            server => Task.FromResult<ProtocolMessage?>(null),
            async client =>
            {
                // TLS 1.3 may report the server's rejection only on the first read.
                return await FrameCodec.ReadAsync(client, Ct);
            });

        // SChannel reports this as IOException, OpenSSL as AuthenticationException.
        Assert.NotNull(serverResult.Error);
        // The client sees either a handshake error or a closed connection, never data.
        Assert.Null(clientResult.Value);
    }

    [Fact]
    public async Task Client_rejects_server_with_unexpected_pin()
    {
        var clientPin = CertificatePin.FromCertificate(_client);
        var wrongServerPin = CertificatePin.FromCertificate(_intruder);

        var (serverResult, clientResult) = await RunHandshakeAsync(
            PinnedTls.CreateServerOptions(_server, clientPin.Matches),
            PinnedTls.CreateClientOptions(_client, wrongServerPin),
            async server => await FrameCodec.ReadAsync(server, Ct),
            client => Task.FromResult<ProtocolMessage?>(null));

        Assert.NotNull(clientResult.Error);
        Assert.Null(serverResult.Value);
    }

    private sealed record Result(ProtocolMessage? Value, Exception? Error);

    private static async Task<(Result Server, Result Client)> RunHandshakeAsync(
        SslServerAuthenticationOptions serverOptions,
        SslClientAuthenticationOptions clientOptions,
        Func<SslStream, Task<ProtocolMessage?>> serverWork,
        Func<SslStream, Task<ProtocolMessage?>> clientWork)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(Ct);
            await using var ssl = new SslStream(socket.GetStream());
            return await CaptureAsync(async () =>
            {
                await ssl.AuthenticateAsServerAsync(serverOptions, Ct);
                return await serverWork(ssl);
            });
        }, Ct);

        var clientTask = Task.Run(async () =>
        {
            using var socket = new TcpClient();
            await socket.ConnectAsync(IPAddress.Loopback, port, Ct);
            await using var ssl = new SslStream(socket.GetStream());
            return await CaptureAsync(async () =>
            {
                await ssl.AuthenticateAsClientAsync(clientOptions, Ct);
                return await clientWork(ssl);
            });
        }, Ct);

        var timeout = TimeSpan.FromSeconds(30);
        return (await serverTask.WaitAsync(timeout, Ct), await clientTask.WaitAsync(timeout, Ct));
    }

    private static async Task<Result> CaptureAsync(Func<Task<ProtocolMessage?>> work)
    {
        try
        {
            return new Result(await work(), null);
        }
#pragma warning disable CA1031 // Tests capture any failure for assertions.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return new Result(null, ex);
        }
    }
}

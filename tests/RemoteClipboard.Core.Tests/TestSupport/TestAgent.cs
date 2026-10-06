using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RemoteClipboard.Core.Agent;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Discovery;
using RemoteClipboard.Core.Networking;

namespace RemoteClipboard.Core.Tests.TestSupport;

/// <summary>A real agent on loopback with a fake clipboard. Identity and peers survive <see cref="RestartAsync"/>.</summary>
internal sealed class TestAgent : IAsyncDisposable
{
    public static readonly ConnectionOptions FastOptions = new()
    {
        BindAddress = IPAddress.Loopback,
        PreferredPort = 0,
        PingInterval = TimeSpan.FromMilliseconds(200),
        IdleTimeout = TimeSpan.FromMilliseconds(1500),
        ConnectTimeout = TimeSpan.FromSeconds(2),
        HandshakeTimeout = TimeSpan.FromSeconds(5),
        PairingTimeout = TimeSpan.FromSeconds(20),
        ReconnectMinDelay = TimeSpan.FromMilliseconds(50),
        ReconnectMaxDelay = TimeSpan.FromMilliseconds(500),
    };

    private readonly InMemorySecretStore _secrets = new();
    private readonly TempDirectory _directory = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly bool _markerSurvives;
    private readonly TestLan? _lan;
    private DiscoveryOptions? _discovery;
    private DeviceIdentity _identity = null!;

    private TestAgent(string name, ILoggerFactory? loggerFactory, bool markerSurvives, TestLan? lan)
    {
        Name = name;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _markerSurvives = markerSurvives;
        _lan = lan;
    }

    public string Name { get; }

    public RemoteClipboardAgent Agent { get; private set; } = null!;

    public FakeClipboard Clipboard { get; private set; } = null!;

    public PeerStore Peers { get; private set; } = null!;

    public DeviceIdentity Identity => _identity;

    public int SentCount { get; private set; }

    public int Port => Agent.ListenPort;

    public static TestAgent Start(string name, ILoggerFactory? loggerFactory = null, bool markerSurvives = true, TestLan? lan = null)
    {
        var agent = new TestAgent(name, loggerFactory, markerSurvives, lan);
        agent.Boot(0);
        return agent;
    }

    public async Task RestartAsync(int port = 0)
    {
        await StopAsync();
        Boot(port);
    }

    /// <summary>Simulates the app being closed or the computer switched off.</summary>
    public async Task StopAsync()
    {
        await Agent.DisposeAsync();
        _identity.Dispose();
        if (_discovery is not null)
        {
            _lan!.Leave(_discovery);
        }
    }

    public bool IsConnectedTo(TestAgent other) =>
        Agent.Devices.Any(d => d.Device.Id == other.Agent.DeviceId && d.IsConnected);

    public bool Knows(TestAgent other) => Peers.Find(other.Agent.DeviceId) is not null;

    public async ValueTask DisposeAsync()
    {
        await Agent.DisposeAsync();
        _identity.Dispose();
        _directory.Dispose();
    }

    private void Boot(int port)
    {
        _identity = new DeviceIdentityStore(_secrets, new MachineBinding("test-machine-" + Name, "test-user")).LoadOrCreate(out _);
        Peers = new PeerStore(_directory.File("peers.json"));
        Clipboard = new FakeClipboard(_markerSurvives);
        _discovery = _lan?.Join();
        Agent = new RemoteClipboardAgent(
            _identity, new LocalDeviceInfo(Name, "Test OS"), Peers, Clipboard, Clipboard,
            FastOptions with { PreferredPort = port }, _loggerFactory, discovery: _discovery);
        Agent.ContentSent += (_, _) => SentCount++;
        Agent.Start();
    }
}

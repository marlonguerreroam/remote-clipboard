using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Security;
using RemoteClipboard.Core.Sync;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Devices;

public class PeerStoreTests
{
    private static PairedDevice NewPeer(string name)
    {
        using var cert = DeviceCertificateFactory.Create(DeviceId.New());
        var id = DeviceId.New();
        return new PairedDevice(id, name, CertificatePin.FromCertificate(cert), DateTimeOffset.UtcNow, SyncDirection.Bidirectional, "192.168.1.30", 47800, "Windows 11");
    }

    [Fact]
    public void Peers_persist_across_instances()
    {
        using var dir = new TempDirectory();
        var peer = NewPeer("PC-B");
        new PeerStore(dir.File("peers.json")).AddOrUpdate(peer);

        var reloaded = new PeerStore(dir.File("peers.json"));

        Assert.Equal(peer, reloaded.Find(peer.Id));
        Assert.Equal(peer, reloaded.FindByPin(peer.CertificatePin));
    }

    [Fact]
    public void Removed_peer_is_no_longer_trusted()
    {
        using var dir = new TempDirectory();
        var store = new PeerStore(dir.File("peers.json"));
        var peer = NewPeer("PC-B");
        store.AddOrUpdate(peer);

        Assert.True(store.Remove(peer.Id));

        Assert.Null(new PeerStore(dir.File("peers.json")).FindByPin(peer.CertificatePin));
    }

    [Fact]
    public void Update_of_an_unpaired_device_never_resurrects_it()
    {
        using var dir = new TempDirectory();
        var store = new PeerStore(dir.File("peers.json"));
        var peer = NewPeer("PC-B");
        store.AddOrUpdate(peer);
        store.Remove(peer.Id);

        // e.g. a handshake that started before the user clicked "Desvincular".
        var result = store.TryUpdate(peer.Id, p => p with { LastKnownPort = 47801 });

        Assert.Null(result);
        Assert.Null(store.Find(peer.Id));
        Assert.Null(new PeerStore(dir.File("peers.json")).Find(peer.Id));
    }

    [Fact]
    public void Update_works_on_the_current_value_so_concurrent_changes_are_kept()
    {
        using var dir = new TempDirectory();
        var store = new PeerStore(dir.File("peers.json"));
        var peer = NewPeer("PC-B");
        store.AddOrUpdate(peer);

        store.TryUpdate(peer.Id, p => p with { Direction = SyncDirection.ReceiveOnly });      // user choice
        store.TryUpdate(peer.Id, p => p with { LastKnownHost = "10.0.0.7", LastKnownPort = 47802 }); // handshake

        var stored = new PeerStore(dir.File("peers.json")).Find(peer.Id)!;
        Assert.Equal(SyncDirection.ReceiveOnly, stored.Direction);
        Assert.Equal("10.0.0.7", stored.LastKnownHost);
        Assert.Equal(47802, stored.LastKnownPort);
    }

    [Fact]
    public void Update_cannot_change_the_device_id()
    {
        using var dir = new TempDirectory();
        var store = new PeerStore(dir.File("peers.json"));
        var peer = NewPeer("PC-B");
        store.AddOrUpdate(peer);

        var updated = store.TryUpdate(peer.Id, p => p with { Id = DeviceId.New() });

        Assert.Equal(peer.Id, updated!.Id);
        Assert.Single(store.All);
    }

    [Fact]
    public void Unreadable_file_starts_empty()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("peers.json"), "{ not json");

        Assert.Empty(new PeerStore(dir.File("peers.json")).All);
    }
}

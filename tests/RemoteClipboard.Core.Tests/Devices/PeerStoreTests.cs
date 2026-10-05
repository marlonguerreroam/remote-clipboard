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
    public void Unreadable_file_starts_empty()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("peers.json"), "{ not json");

        Assert.Empty(new PeerStore(dir.File("peers.json")).All);
    }
}

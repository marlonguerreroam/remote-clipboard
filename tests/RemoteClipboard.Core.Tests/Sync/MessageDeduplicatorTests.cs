using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.Core.Tests.Sync;

public class MessageDeduplicatorTests
{
    [Fact]
    public void Duplicate_message_is_rejected()
    {
        var dedup = new MessageDeduplicator();
        var id = Guid.NewGuid();

        Assert.True(dedup.TryRegister(id));
        Assert.False(dedup.TryRegister(id));
    }

    [Fact]
    public void Memory_is_bounded()
    {
        var dedup = new MessageDeduplicator(capacity: 2);
        var first = Guid.NewGuid();

        dedup.TryRegister(first);
        dedup.TryRegister(Guid.NewGuid());
        dedup.TryRegister(Guid.NewGuid());

        Assert.True(dedup.TryRegister(first)); // evicted, accepted again
    }
}

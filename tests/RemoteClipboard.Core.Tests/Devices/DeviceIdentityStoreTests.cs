using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Devices;

public class DeviceIdentityStoreTests
{
    private static readonly MachineBinding Binding = new("machine-1", "S-1-5-21-1");

    [Fact]
    public void Identity_is_created_once_and_then_stable_across_restarts()
    {
        var secrets = new InMemorySecretStore();

        using var first = new DeviceIdentityStore(secrets, Binding).LoadOrCreate(out var r1);
        using var second = new DeviceIdentityStore(secrets, Binding).LoadOrCreate(out var r2);

        Assert.Equal(DeviceIdentityStore.LoadResult.Created, r1);
        Assert.Equal(DeviceIdentityStore.LoadResult.Loaded, r2);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Pin, second.Pin);
        Assert.True(second.Certificate.HasPrivateKey);
    }

    [Theory]
    [InlineData("machine-2", "S-1-5-21-1")] // cloned VM / disk
    [InlineData("machine-1", "S-1-5-21-2")] // profile copied to another user
    public void Identity_is_regenerated_when_binding_changes(string machine, string user)
    {
        var secrets = new InMemorySecretStore();
        using var original = new DeviceIdentityStore(secrets, Binding).LoadOrCreate(out _);

        using var clone = new DeviceIdentityStore(secrets, new MachineBinding(machine, user)).LoadOrCreate(out var result);

        Assert.Equal(DeviceIdentityStore.LoadResult.RegeneratedAfterBindingChange, result);
        Assert.NotEqual(original.Id, clone.Id);
        Assert.NotEqual(original.Pin, clone.Pin);
    }

    [Fact]
    public void Corrupt_identity_is_replaced()
    {
        var secrets = new InMemorySecretStore();
        secrets.Corrupt(DeviceIdentityStore.SecretName);

        using var identity = new DeviceIdentityStore(secrets, Binding).LoadOrCreate(out var result);

        Assert.Equal(DeviceIdentityStore.LoadResult.RegeneratedAfterBindingChange, result);
        Assert.False(identity.Id.IsEmpty);
    }
}

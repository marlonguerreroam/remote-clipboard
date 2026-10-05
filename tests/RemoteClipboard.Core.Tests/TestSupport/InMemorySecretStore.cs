using RemoteClipboard.Core.Security;

namespace RemoteClipboard.Core.Tests.TestSupport;

internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, byte[]> _secrets = [];

    public void Save(string name, ReadOnlySpan<byte> secret) => _secrets[name] = secret.ToArray();

    public bool TryLoad(string name, out byte[] secret)
    {
        var found = _secrets.TryGetValue(name, out var value);
        secret = found ? value!.ToArray() : [];
        return found;
    }

    public void Delete(string name) => _secrets.Remove(name);

    public void Corrupt(string name) => _secrets[name] = [1, 2, 3];
}

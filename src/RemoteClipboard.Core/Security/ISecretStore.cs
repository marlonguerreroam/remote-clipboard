namespace RemoteClipboard.Core.Security;

/// <summary>
/// Storage for secrets at rest (device private key, future rotation material). The Windows
/// implementation encrypts with DPAPI bound to the current user. Secrets are never written to
/// config files, logs or the repository.
/// </summary>
public interface ISecretStore
{
    void Save(string name, ReadOnlySpan<byte> secret);

    bool TryLoad(string name, out byte[] secret);

    void Delete(string name);
}

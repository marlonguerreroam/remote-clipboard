using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using RemoteClipboard.Core.Security;

namespace RemoteClipboard.Windows.Security;

/// <summary>
/// Stores secrets encrypted with DPAPI (CurrentUser scope): only the same Windows user on the same
/// machine (or with the user's roaming DPAPI master key) can decrypt them. Other users of a shared
/// Windows Server cannot, even with read access to the file.
/// </summary>
/// <remarks>
/// Chosen over Credential Manager because blobs are limited there (CRED_MAX_CREDENTIAL_BLOB_SIZE)
/// and PKCS#12 identities can exceed it; DPAPI is the primitive Credential Manager uses anyway.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    // Not a secret: domain separation so blobs from other apps of the same user are not interchangeable.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RemoteClipboard.SecretStore.v1");

    private readonly string _directory;

    public DpapiSecretStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public void Save(string name, ReadOnlySpan<byte> secret)
    {
        var path = PathFor(name);
        Directory.CreateDirectory(_directory);

        var plain = secret.ToArray();
        try
        {
            var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, protectedBytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public bool TryLoad(string name, out byte[] secret)
    {
        secret = [];
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            secret = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return true;
        }
        catch (CryptographicException)
        {
            // Blob from another user/machine (e.g. copied profile or cloned disk): treat as absent.
            return false;
        }
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid secret name.", nameof(name));
        }

        return Path.Combine(_directory, name + ".bin");
    }
}

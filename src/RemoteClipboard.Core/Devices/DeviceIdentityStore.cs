// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteClipboard.Core.Security;

namespace RemoteClipboard.Core.Devices;

/// <summary>
/// Loads or creates the device identity. The whole record (id, binding, PKCS#12) is a single secret
/// in <see cref="ISecretStore"/> (DPAPI on Windows).
/// </summary>
public sealed class DeviceIdentityStore(ISecretStore secrets, MachineBinding binding, TimeProvider? time = null)
{
    internal const string SecretName = "identity";

    public enum LoadResult
    {
        Loaded,
        Created,
        RegeneratedAfterBindingChange,
    }

    public DeviceIdentity LoadOrCreate(out LoadResult result)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(binding);

        var hadIdentity = false;
        if (secrets.TryLoad(SecretName, out var blob))
        {
            hadIdentity = true;
            try
            {
                var record = JsonSerializer.Deserialize(blob, IdentityJsonContext.Default.IdentityRecord);
                if (record is not null && record.Version == 1
                    && string.Equals(record.MachineId, binding.MachineId, StringComparison.Ordinal)
                    && string.Equals(record.UserId, binding.UserId, StringComparison.Ordinal)
                    && DeviceId.TryParse(record.DeviceId, out var id))
                {
                    var certificate = DeviceCertificateFactory.ImportWithPrivateKey(record.Pkcs12);
                    CryptographicOperations.ZeroMemory(record.Pkcs12);
                    result = LoadResult.Loaded;
                    return new DeviceIdentity(id, certificate);
                }
            }
            catch (Exception ex) when (ex is JsonException or CryptographicException)
            {
                // Corrupt or foreign record: fall through and create a fresh identity.
            }
            finally
            {
                CryptographicOperations.ZeroMemory(blob);
            }
        }

        var identity = Create();
        result = hadIdentity ? LoadResult.RegeneratedAfterBindingChange : LoadResult.Created;
        return identity;
    }

    private DeviceIdentity Create()
    {
        var id = DeviceId.New();
        var pkcs12 = DeviceCertificateFactory.CreatePkcs12(id, time);
        byte[]? json = null;
        try
        {
            var certificate = DeviceCertificateFactory.ImportWithPrivateKey(pkcs12);
            json = JsonSerializer.SerializeToUtf8Bytes(
                new IdentityRecord(1, id.ToString(), binding.MachineId, binding.UserId, pkcs12),
                IdentityJsonContext.Default.IdentityRecord);
            secrets.Save(SecretName, json);
            return new DeviceIdentity(id, certificate);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
            if (json is not null)
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }
    }

    internal sealed record IdentityRecord(int Version, string DeviceId, string MachineId, string UserId, byte[] Pkcs12);
}

[JsonSerializable(typeof(DeviceIdentityStore.IdentityRecord))]
internal sealed partial class IdentityJsonContext : JsonSerializerContext;

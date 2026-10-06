using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteClipboard.Core.Security;
using RemoteClipboard.Core.Storage;
using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.Core.Devices;

/// <summary>
/// Persistent list of paired devices (per user). Pins are public keys' hashes, not secrets, but they are
/// the trust anchor, so the file lives in the user's private profile folder. Thread-safe.
/// </summary>
public sealed class PeerStore
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly Dictionary<DeviceId, PairedDevice> _peers = [];

    public PeerStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        Load();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<PairedDevice> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _peers.Values.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
            }
        }
    }

    public PairedDevice? Find(DeviceId id)
    {
        lock (_gate)
        {
            return _peers.GetValueOrDefault(id);
        }
    }

    public PairedDevice? FindByPin(CertificatePin pin)
    {
        lock (_gate)
        {
            return _peers.Values.FirstOrDefault(p => p.CertificatePin == pin);
        }
    }

    public void AddOrUpdate(PairedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_gate)
        {
            if (_peers.TryGetValue(device.Id, out var existing) && existing == device)
            {
                return;
            }

            _peers[device.Id] = device;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Atomically updates an existing device from its CURRENT stored value. Returns null (and adds nothing)
    /// when the device is no longer paired, so a late handshake can never resurrect an unpaired device or
    /// overwrite a concurrent change (e.g. the sync direction) with a stale snapshot.
    /// </summary>
    public PairedDevice? TryUpdate(DeviceId id, Func<PairedDevice, PairedDevice> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        PairedDevice updated;
        lock (_gate)
        {
            if (!_peers.TryGetValue(id, out var current))
            {
                return null;
            }

            updated = update(current) with { Id = id };
            if (updated == current)
            {
                return current;
            }

            _peers[id] = updated;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    public bool Remove(DeviceId id)
    {
        lock (_gate)
        {
            if (!_peers.Remove(id))
            {
                return false;
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var records = JsonSerializer.Deserialize(File.ReadAllBytes(_path), PeerJsonContext.Default.ListPeerRecord) ?? [];
            foreach (var r in records)
            {
                if (DeviceId.TryParse(r.Id, out var id) && CertificatePin.TryParse(r.Pin, out var pin))
                {
                    _peers[id] = new PairedDevice(id, r.DisplayName, pin, r.PairedAtUtc, r.Direction, r.Host, r.Port, r.OsDescription);
                }
            }
        }
        catch (JsonException)
        {
            // Unreadable file: start empty rather than crash; the user can pair again.
        }
    }

    private void Save()
    {
        var records = _peers.Values.Select(p => new PeerRecord(
            p.Id.ToString(), p.DisplayName, p.CertificatePin.Hex, p.PairedAtUtc, p.Direction, p.LastKnownHost, p.LastKnownPort, p.OsDescription)).ToList();
        AtomicFile.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(records, PeerJsonContext.Default.ListPeerRecord));
    }

    internal sealed record PeerRecord(
        string Id, string DisplayName, string Pin, DateTimeOffset PairedAtUtc, SyncDirection Direction, string? Host, int? Port, string? OsDescription);
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<PeerStore.PeerRecord>))]
internal sealed partial class PeerJsonContext : JsonSerializerContext;

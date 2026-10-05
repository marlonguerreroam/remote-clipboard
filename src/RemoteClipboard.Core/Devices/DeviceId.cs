namespace RemoteClipboard.Core.Devices;

/// <summary>
/// Stable, unique identity of one Remote Clipboard installation (per Windows user profile).
/// Never derived from the computer name, which can change or collide.
/// </summary>
public readonly record struct DeviceId(Guid Value)
{
    public static DeviceId New() => new(Guid.NewGuid());

    public static DeviceId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out DeviceId id)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = new DeviceId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("D");
}

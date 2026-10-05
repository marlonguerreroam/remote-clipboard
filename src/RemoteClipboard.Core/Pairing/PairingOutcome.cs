using RemoteClipboard.Core.Devices;

namespace RemoteClipboard.Core.Pairing;

public enum PairingStatus
{
    Success,
    WrongCode,
    Expired,
    LockedOut,
    NotAccepting,
    Unreachable,
    Failed,
}

public sealed record PairingOutcome(PairingStatus Status, PairedDevice? Device = null)
{
    public bool Succeeded => Status == PairingStatus.Success;
}

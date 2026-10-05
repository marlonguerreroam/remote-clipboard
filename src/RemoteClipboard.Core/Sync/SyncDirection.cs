namespace RemoteClipboard.Core.Sync;

/// <summary>Per-peer synchronization policy. MVP uses <see cref="Bidirectional"/> only.</summary>
public enum SyncDirection
{
    Bidirectional = 0,
    SendOnly = 1,
    ReceiveOnly = 2,
}

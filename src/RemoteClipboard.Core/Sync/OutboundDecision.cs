namespace RemoteClipboard.Core.Sync;

/// <summary>Result of evaluating a local clipboard change for broadcast.</summary>
public enum OutboundDecision
{
    /// <summary>Genuine local change: send to authorized peers.</summary>
    Broadcast = 0,

    /// <summary>Clipboard holds no supported format.</summary>
    SkipUnsupported,

    /// <summary>Empty text: nothing worth syncing.</summary>
    SkipEmpty,

    /// <summary>Larger than the protocol limit.</summary>
    SkipTooLarge,

    /// <summary>Source application asked clipboard monitors to ignore it (e.g. password manager).</summary>
    SkipExcludedByOwner,

    /// <summary>The change was written by Remote Clipboard itself (marker present).</summary>
    SkipRemoteOrigin,

    /// <summary>Identical to the content last synchronized in either direction (echo / re-copy).</summary>
    SkipDuplicate,
}

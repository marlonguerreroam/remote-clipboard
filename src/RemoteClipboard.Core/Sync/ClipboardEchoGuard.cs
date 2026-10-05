using System.Security.Cryptography;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Protocol;

namespace RemoteClipboard.Core.Sync;

/// <summary>
/// Decides whether a local clipboard change must be broadcast, preventing synchronization loops.
/// </summary>
/// <remarks>
/// Three independent layers protect against loops:
/// <list type="number">
/// <item>Remote-origin marker: content written by <see cref="IClipboardWriter"/> carries a private
/// clipboard format, so the resulting change event is recognised and never re-broadcast.</item>
/// <item>Shared-state fingerprint: the guard remembers the last content synchronized in either
/// direction. A local change identical to it (e.g. RDP clipboard redirection or an app re-setting
/// the same text) is not re-sent.</item>
/// <item>No forwarding: devices only send content originated locally; received content is never relayed
/// (enforced by the sync engine, plus message-id de-duplication).</item>
/// </list>
/// Fingerprints are HMAC-SHA256 with a random per-process key, held only in memory, so they cannot be
/// used as an offline oracle for clipboard contents.
/// </remarks>
public sealed class ClipboardEchoGuard
{
    private readonly byte[] _fingerprintKey = RandomNumberGenerator.GetBytes(32);
    private readonly Lock _gate = new();
    private readonly int _maxContentBytes;
    private byte[]? _lastSyncedFingerprint;

    public ClipboardEchoGuard(int maxContentBytes = ProtocolLimits.MaxClipboardBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxContentBytes);
        _maxContentBytes = maxContentBytes;
    }

    /// <summary>
    /// Evaluates a local clipboard change. When the result is <see cref="OutboundDecision.Broadcast"/>
    /// the content becomes the new shared state.
    /// </summary>
    public OutboundDecision EvaluateLocalChange(ClipboardChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.HasRemoteOriginMarker)
        {
            return OutboundDecision.SkipRemoteOrigin;
        }

        if (change.IsExcludedByOwner)
        {
            return OutboundDecision.SkipExcludedByOwner;
        }

        if (change.IsTooLarge)
        {
            return OutboundDecision.SkipTooLarge;
        }

        var content = change.Content;
        if (content is null || content.Format == ClipboardFormat.Unknown)
        {
            return OutboundDecision.SkipUnsupported;
        }

        if (content.IsEmpty)
        {
            return OutboundDecision.SkipEmpty;
        }

        if (content.Length > _maxContentBytes)
        {
            return OutboundDecision.SkipTooLarge;
        }

        var fingerprint = Fingerprint(content);
        lock (_gate)
        {
            if (_lastSyncedFingerprint is not null &&
                CryptographicOperations.FixedTimeEquals(_lastSyncedFingerprint, fingerprint))
            {
                return OutboundDecision.SkipDuplicate;
            }

            _lastSyncedFingerprint = fingerprint;
            return OutboundDecision.Broadcast;
        }
    }

    /// <summary>
    /// Must be called BEFORE writing remote content to the local clipboard, so the change event
    /// it triggers is recognised even if the marker format is stripped by another application.
    /// </summary>
    public void RegisterRemoteContent(ClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var fingerprint = Fingerprint(content);
        lock (_gate)
        {
            _lastSyncedFingerprint = fingerprint;
        }
    }

    private byte[] Fingerprint(ClipboardContent content)
    {
        var span = content.Data.Span;
        var buffer = new byte[span.Length + 1];
        try
        {
            buffer[0] = (byte)content.Format;
            span.CopyTo(buffer.AsSpan(1));
            return HMACSHA256.HashData(_fingerprintKey, buffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}

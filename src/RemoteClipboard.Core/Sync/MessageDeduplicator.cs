namespace RemoteClipboard.Core.Sync;

/// <summary>
/// Bounded memory of recently processed message ids. Drops duplicates caused by reconnections,
/// retransmissions or (future) multi-path/relay delivery.
/// </summary>
public sealed class MessageDeduplicator
{
    private readonly int _capacity;
    private readonly HashSet<Guid> _seen = [];
    private readonly Queue<Guid> _order = new();
    private readonly Lock _gate = new();

    public MessageDeduplicator(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>Returns true the first time an id is seen, false for duplicates.</summary>
    public bool TryRegister(Guid messageId)
    {
        lock (_gate)
        {
            if (!_seen.Add(messageId))
            {
                return false;
            }

            _order.Enqueue(messageId);
            if (_order.Count > _capacity)
            {
                _seen.Remove(_order.Dequeue());
            }

            return true;
        }
    }
}

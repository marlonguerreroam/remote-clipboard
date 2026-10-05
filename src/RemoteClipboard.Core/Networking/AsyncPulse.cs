namespace RemoteClipboard.Core.Networking;

/// <summary>Wakes every waiter at once (network changed, peer disconnected, peer list changed).</summary>
internal sealed class AsyncPulse
{
    private TaskCompletionSource _tcs = New();

    public Task WaitAsync() => Volatile.Read(ref _tcs).Task;

    public void Pulse() => Interlocked.Exchange(ref _tcs, New()).TrySetResult();

    private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

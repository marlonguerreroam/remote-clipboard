// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Tests.TestSupport;

/// <summary>
/// Simulates a UI thread (WPF dispatcher) that synchronously blocks on a task, as Application.OnExit does.
/// Continuations posted to its SynchronizationContext can never run while it is blocked, so any await that
/// captures the context deadlocks: exactly the bug that left the app hung after "Salir".
/// </summary>
internal static class BlockedUiThread
{
    public static bool CompletesWithin(Func<Task> work, TimeSpan timeout)
    {
        var completed = false;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NeverPumpedContext());
            completed = work().Wait(timeout);
        })
        {
            IsBackground = true,
        };
        thread.Start();
        thread.Join(timeout + TimeSpan.FromSeconds(5));
        return completed;
    }

    /// <summary>Queues callbacks for a "UI thread" that is busy blocking: they are never executed.</summary>
    private sealed class NeverPumpedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Dropped on purpose: the owning thread is blocked in Wait().
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("Send on a blocked UI thread would deadlock.");
    }
}

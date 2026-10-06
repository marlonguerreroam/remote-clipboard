// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Pairing;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.EndToEnd;

/// <summary>Regression: the app hung forever on exit (and could not be started again) because shutdown deadlocked on the UI thread.</summary>
public class ShutdownTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Agent_shutdown_never_needs_the_calling_ui_thread()
    {
        var lan = new TestLan();
        var a = TestAgent.Start("PC-A", lan: lan);
        await using var b = TestAgent.Start("PC-B", lan: lan);
        var window = a.Agent.OpenPairing();
        Assert.Equal(PairingStatus.Success, (await b.Agent.PairWithAsync("127.0.0.1", a.Port, window.Code, Ct)).Status);
        await Eventually.TrueAsync(() => a.IsConnectedTo(b), "connected");

        // Busy agent: live connection, discovery and pairing state, like the real app when closed.
        var completed = BlockedUiThread.CompletesWithin(() => a.Agent.DisposeAsync().AsTask(), TimeSpan.FromSeconds(10));

        Assert.True(completed, "Agent shutdown deadlocked when awaited from a blocked UI thread.");
        await a.StopAsync(); // idempotent; releases the remaining test resources
    }
}

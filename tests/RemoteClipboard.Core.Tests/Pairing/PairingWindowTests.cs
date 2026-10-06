// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Pairing;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Pairing;

public class PairingWindowTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Successful_attempt_consumes_the_code()
    {
        var window = new PairingWindow(_time);

        Assert.Equal(PairingAttemptResult.Accepted, window.RegisterAttempt(pakeSucceeded: true));
        Assert.False(window.IsOpen);
        Assert.Equal(PairingAttemptResult.LockedOut, window.RegisterAttempt(pakeSucceeded: true));
    }

    [Fact]
    public void Wrong_code_attempts_lock_the_window()
    {
        var window = new PairingWindow(_time);

        Assert.Equal(PairingAttemptResult.Rejected, window.RegisterAttempt(false));
        Assert.Equal(PairingAttemptResult.Rejected, window.RegisterAttempt(false));
        Assert.Equal(PairingAttemptResult.LockedOut, window.RegisterAttempt(false));

        // Even the right code is refused once locked: the attacker burned the invitation.
        Assert.Equal(PairingAttemptResult.LockedOut, window.RegisterAttempt(true));
        Assert.False(window.IsOpen);
    }

    [Fact]
    public void Window_expires()
    {
        var window = new PairingWindow(_time, TimeSpan.FromMinutes(2));

        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.False(window.IsOpen);
        Assert.Equal(PairingAttemptResult.Expired, window.RegisterAttempt(true));
    }
}

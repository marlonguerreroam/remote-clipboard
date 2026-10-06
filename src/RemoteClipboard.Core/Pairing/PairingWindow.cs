// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Pairing;

public enum PairingAttemptResult
{
    Accepted,
    Rejected,
    Expired,
    LockedOut,
}

/// <summary>
/// Host-side state of one pairing invitation: a single code, short lifetime and a hard limit of
/// failed attempts, after which the code is burned. Bounds an online guessing attacker to
/// <see cref="MaxFailedAttempts"/> guesses out of 10^6 per invitation.
/// </summary>
public sealed class PairingWindow
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(2);
    public const int MaxFailedAttempts = 3;

    private readonly TimeProvider _time;
    private readonly DateTimeOffset _expiresAt;
    private readonly Lock _gate = new();
    private int _failedAttempts;
    private bool _consumed;

    public PairingWindow(TimeProvider time, TimeSpan? lifetime = null, string? code = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        Code = code ?? PairingCode.Generate();
        _expiresAt = time.GetUtcNow() + (lifetime ?? DefaultLifetime);
    }

    /// <summary>The code to display on the host. Never log it.</summary>
    public string Code { get; }

    public DateTimeOffset ExpiresAt => _expiresAt;

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return !_consumed && _failedAttempts < MaxFailedAttempts && _time.GetUtcNow() < _expiresAt;
            }
        }
    }

    /// <summary>
    /// Records the outcome of a PAKE run (the code itself is never compared directly).
    /// <paramref name="pakeSucceeded"/> is true when key confirmation succeeded.
    /// </summary>
    public PairingAttemptResult RegisterAttempt(bool pakeSucceeded)
    {
        lock (_gate)
        {
            if (_consumed || _failedAttempts >= MaxFailedAttempts)
            {
                return PairingAttemptResult.LockedOut;
            }

            if (_time.GetUtcNow() >= _expiresAt)
            {
                return PairingAttemptResult.Expired;
            }

            if (pakeSucceeded)
            {
                _consumed = true; // one code, one pairing.
                return PairingAttemptResult.Accepted;
            }

            _failedAttempts++;
            return _failedAttempts >= MaxFailedAttempts ? PairingAttemptResult.LockedOut : PairingAttemptResult.Rejected;
        }
    }
}

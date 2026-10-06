// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RemoteClipboard.Core.Security;

namespace RemoteClipboard.Core.Licensing;

public enum LicenseState
{
    /// <summary>No license needed (licensing off, or Microsoft Store version: the Store sold it).</summary>
    NotRequired,
    Licensed,
    Trial,
    TrialExpired,
}

/// <summary>
/// License and trial status. Works offline: the key is verified with the embedded public key and both
/// the key and the trial start are kept in the secret store (DPAPI on Windows). When the trial ends
/// without a license, synchronization is disabled; everything else keeps working.
/// </summary>
public sealed class LicenseManager : IDisposable
{
    public const string LicenseSecretName = "license";
    public const string TrialSecretName = "trial";

    private readonly ISecretStore _store;
    private readonly ECDsa? _publicKey;
    private readonly TimeProvider _time;
    private readonly int _trialDays;
    private readonly DateTimeOffset _trialStart;

    public LicenseManager(ISecretStore store, ECDsa? publicKey, TimeProvider time, int trialDays = LicensingConfig.TrialDays)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trialDays);
        _store = store;
        _publicKey = publicKey;
        _time = time;
        _trialDays = trialDays;
        if (publicKey is null)
        {
            return;
        }

        _trialStart = LoadOrStartTrial();
        if (_store.TryLoad(LicenseSecretName, out var saved))
        {
            var check = LicenseKeyFormat.Verify(Encoding.UTF8.GetString(saved), publicKey);
            License = check.IsValid ? check.License : null;
        }
    }

    public License? License { get; private set; }

    public LicenseState State => _publicKey is null ? LicenseState.NotRequired
        : License is not null ? LicenseState.Licensed
        : TrialDaysLeft > 0 ? LicenseState.Trial
        : LicenseState.TrialExpired;

    public bool IsSyncAllowed => State != LicenseState.TrialExpired;

    /// <summary>Whole days left in the trial (0 when expired). A clock moved backwards never adds days.</summary>
    public int TrialDaysLeft
    {
        get
        {
            if (_publicKey is null)
            {
                return 0;
            }

            var left = _trialStart.AddDays(_trialDays) - _time.GetUtcNow();
            return (int)Math.Clamp(Math.Ceiling(left.TotalDays), 0, _trialDays);
        }
    }

    public LicenseCheck Activate(string key)
    {
        if (_publicKey is null)
        {
            return new LicenseCheck(LicenseCheckStatus.Malformed);
        }

        var check = LicenseKeyFormat.Verify(key, _publicKey);
        if (check.IsValid)
        {
            var compact = string.Concat(key.Where(c => !char.IsWhiteSpace(c)));
            _store.Save(LicenseSecretName, Encoding.UTF8.GetBytes(compact));
            License = check.License;
        }

        return check;
    }

    public void Deactivate()
    {
        _store.Delete(LicenseSecretName);
        License = null;
    }

    public void Dispose() => _publicKey?.Dispose();

    private DateTimeOffset LoadOrStartTrial()
    {
        if (_store.TryLoad(TrialSecretName, out var saved)
            && DateTimeOffset.TryParse(Encoding.UTF8.GetString(saved), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start))
        {
            return start;
        }

        var now = _time.GetUtcNow();
        _store.Save(TrialSecretName, Encoding.UTF8.GetBytes(now.ToString("O", CultureInfo.InvariantCulture)));
        return now;
    }
}

// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using RemoteClipboard.Core.Licensing;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Licensing;

public sealed class LicenseTests : IDisposable
{
    private static readonly License Sample = new("L-0001", "Juan Pérez", LicenseEdition.Personal, new DateOnly(2026, 10, 6));

    private readonly ECDsa _privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _publicKey = ECDsa.Create();
    private readonly InMemorySecretStore _store = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    public LicenseTests() => _publicKey.ImportSubjectPublicKeyInfo(_privateKey.ExportSubjectPublicKeyInfo(), out _);

    public void Dispose()
    {
        _privateKey.Dispose();
        _publicKey.Dispose();
    }

    [Fact]
    public void Issued_key_verifies_and_round_trips_every_field()
    {
        var key = LicenseKeyFormat.Issue(Sample with { Edition = LicenseEdition.Business }, _privateKey);

        var check = LicenseKeyFormat.Verify(key, _publicKey);

        Assert.True(check.IsValid);
        Assert.Equal(Sample with { Edition = LicenseEdition.Business }, check.License);
        Assert.StartsWith(LicenseKeyFormat.Prefix, key, StringComparison.Ordinal);
    }

    [Fact]
    public void Key_pasted_with_spaces_and_line_breaks_still_verifies()
    {
        var key = LicenseKeyFormat.Issue(Sample, _privateKey);
        var pasted = "  " + key[..20] + "\r\n" + key[20..40] + " " + key[40..] + "\n";

        Assert.True(LicenseKeyFormat.Verify(pasted, _publicKey).IsValid);
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        var key = LicenseKeyFormat.Issue(Sample, _privateKey);
        var forged = LicenseKeyFormat.Issue(Sample with { Licensee = "Otro" }, _privateKey);
        // Payload of one key with the signature of another.
        var mixed = forged[..forged.IndexOf('.', StringComparison.Ordinal)] + key[key.IndexOf('.', StringComparison.Ordinal)..];

        Assert.Equal(LicenseCheckStatus.BadSignature, LicenseKeyFormat.Verify(mixed, _publicKey).Status);
    }

    [Fact]
    public void Every_altered_character_invalidates_the_key()
    {
        var key = LicenseKeyFormat.Issue(Sample, _privateKey);
        for (var i = LicenseKeyFormat.Prefix.Length; i < key.Length; i++)
        {
            if (key[i] == '.')
            {
                continue;
            }

            var altered = key[..i] + (key[i] == 'A' ? 'B' : 'A') + key[(i + 1)..];
            Assert.False(LicenseKeyFormat.Verify(altered, _publicKey).IsValid, $"position {i}");
        }
    }

    [Fact]
    public void Key_signed_by_another_private_key_is_rejected()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = LicenseKeyFormat.Issue(Sample, attacker);

        Assert.Equal(LicenseCheckStatus.BadSignature, LicenseKeyFormat.Verify(key, _publicKey).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hola")]
    [InlineData("RC1-")]
    [InlineData("RC1-abc")]
    [InlineData("RC1-abc.")]
    [InlineData("RC1-***.***")]
    [InlineData("RC2-abc.def")]
    public void Malformed_keys_are_rejected(string? key) =>
        Assert.Equal(LicenseCheckStatus.Malformed, LicenseKeyFormat.Verify(key, _publicKey).Status);

    [Fact]
    public void Validly_signed_but_unexpected_payloads_are_rejected()
    {
        Assert.Equal(LicenseCheckStatus.UnsupportedVersion, LicenseKeyFormat.Verify(SignRaw("""{"v":2}"""), _publicKey).Status);
        Assert.Equal(LicenseCheckStatus.Malformed, LicenseKeyFormat.Verify(SignRaw("""{"v":1,"id":"x"}"""), _publicKey).Status);
        Assert.Equal(LicenseCheckStatus.Malformed, LicenseKeyFormat.Verify(SignRaw("[1,2]"), _publicKey).Status);
        Assert.Equal(LicenseCheckStatus.Malformed,
            LicenseKeyFormat.Verify(SignRaw("""{"v":1,"id":"x","to":"a","ed":"gold","iat":"2026-10-06"}"""), _publicKey).Status);
    }

    [Fact]
    public void Licensee_names_are_validated() =>
        Assert.Throws<ArgumentException>(() => LicenseKeyFormat.Issue(Sample with { Licensee = "a\u0007b" }, _privateKey));

    [Fact]
    public void Embedded_public_key_is_a_valid_P256_key()
    {
        using var key = LicensingConfig.CreatePublicKey();

        Assert.NotNull(key);
        Assert.Equal(256, key.KeySize);
    }

    [Fact]
    public void Without_public_key_no_license_is_required()
    {
        using var manager = new LicenseManager(_store, null, _time);

        Assert.Equal(LicenseState.NotRequired, manager.State);
        Assert.True(manager.IsSyncAllowed);
        Assert.False(_store.TryLoad(LicenseManager.TrialSecretName, out _)); // nothing written
    }

    [Fact]
    public void Trial_counts_down_and_then_blocks_sync()
    {
        using var manager = NewManager();
        Assert.Equal(LicenseState.Trial, manager.State);
        Assert.Equal(14, manager.TrialDaysLeft);

        _time.Advance(TimeSpan.FromDays(13.5));
        Assert.Equal(1, manager.TrialDaysLeft);
        Assert.True(manager.IsSyncAllowed);

        _time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(LicenseState.TrialExpired, manager.State);
        Assert.False(manager.IsSyncAllowed);
    }

    [Fact]
    public void Trial_start_survives_restarts_and_clock_rollback_adds_no_days()
    {
        using (NewManager())
        {
        }

        _time.Advance(TimeSpan.FromDays(10));
        using (var restarted = NewManager())
        {
            Assert.Equal(4, restarted.TrialDaysLeft);
        }

        _time.Advance(TimeSpan.FromDays(-30)); // user sets the clock back
        using var rolledBack = NewManager();
        Assert.Equal(14, rolledBack.TrialDaysLeft); // capped, never more than the trial length
    }

    [Fact]
    public void Activation_unlocks_an_expired_trial_and_persists()
    {
        using (var manager = NewManager())
        {
            _time.Advance(TimeSpan.FromDays(30));
            Assert.False(manager.IsSyncAllowed);

            var check = manager.Activate(LicenseKeyFormat.Issue(Sample, _privateKey));

            Assert.True(check.IsValid);
            Assert.Equal(LicenseState.Licensed, manager.State);
            Assert.True(manager.IsSyncAllowed);
        }

        using var restarted = NewManager();
        Assert.Equal(LicenseState.Licensed, restarted.State);
        Assert.Equal("Juan Pérez", restarted.License?.Licensee);
    }

    [Fact]
    public void Invalid_activation_changes_nothing_and_a_tampered_saved_key_is_ignored()
    {
        using (var manager = NewManager())
        {
            Assert.Equal(LicenseCheckStatus.BadSignature, manager.Activate("RC1-nope.nope").Status);
            Assert.Equal(LicenseState.Trial, manager.State);
            Assert.False(_store.TryLoad(LicenseManager.LicenseSecretName, out _));
        }

        _store.Save(LicenseManager.LicenseSecretName, Encoding.UTF8.GetBytes("RC1-forged.key"));
        using var restarted = NewManager();
        Assert.Equal(LicenseState.Trial, restarted.State);
    }

    [Fact]
    public void Deactivate_returns_to_the_trial_state()
    {
        using var manager = NewManager();
        manager.Activate(LicenseKeyFormat.Issue(Sample, _privateKey));

        manager.Deactivate();

        Assert.Equal(LicenseState.Trial, manager.State);
        Assert.False(_store.TryLoad(LicenseManager.LicenseSecretName, out _));
    }

    private LicenseManager NewManager()
    {
        var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(_publicKey.ExportSubjectPublicKeyInfo(), out _);
        return new LicenseManager(_store, publicKey, _time);
    }

    private string SignRaw(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var signature = _privateKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return LicenseKeyFormat.Prefix + System.Buffers.Text.Base64Url.EncodeToString(payload) + "." + System.Buffers.Text.Base64Url.EncodeToString(signature);
    }
}

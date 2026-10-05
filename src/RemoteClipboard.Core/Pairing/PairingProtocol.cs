using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement.JPake;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Security;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Networking;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Core.Security;
using BcBigInteger = Org.BouncyCastle.Math.BigInteger;

namespace RemoteClipboard.Core.Pairing;

/// <summary>
/// Pairing over an already-established TLS channel whose certificates are not trusted yet.
/// <list type="number">
/// <item>J-PAKE (RFC 8236, BouncyCastle, NIST 3072-bit group, SHA-256) with the 6-digit code as
/// password: an attacker gets one online guess per run and learns nothing usable offline.</item>
/// <item>Explicit key confirmation: K = HKDF-SHA256(J-PAKE key), MAC = HMAC-SHA256(K, role || hostId ||
/// joinerId || hostPin || joinerPin). Binding both TLS pins defeats a man-in-the-middle, who would
/// present different certificates on each leg.</item>
/// </list>
/// Message flow: J→H Request, Round1 · H→J Round1, Round2 · J→H Round2, Confirm · H→J Confirm, Result.
/// </summary>
public static class PairingProtocol
{
    private static readonly byte[] KdfInfo = Encoding.ASCII.GetBytes("RemoteClipboard-Pairing-v1");
    private static readonly byte[] HostLabel = Encoding.ASCII.GetBytes("host");
    private static readonly byte[] JoinerLabel = Encoding.ASCII.GetBytes("joiner");
    private const int MaxBigIntegerBytes = 1024;

    /// <summary>Host side. <paramref name="registerAttempt"/> applies the attempt limits before answering.</summary>
    public static async Task<bool> RunHostAsync(
        Stream stream,
        string code,
        DeviceIdentity local,
        LocalDeviceInfo localInfo,
        int listenPort,
        PairingRequestMessage request,
        CertificatePin joinerPin,
        Func<bool, PairingAttemptResult> registerAttempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(registerAttempt);

        var verified = false;
        byte[]? key = null;
        try
        {
            var joinerId = new DeviceId(request.DeviceId);
            Require(!joinerId.IsEmpty && joinerId != local.Id && request.ProtocolVersion == ProtocolLimits.ProtocolVersion);

            var round1 = await ReadAsync<PairingRound1Message>(stream, cancellationToken).ConfigureAwait(false);
            Require(round1.ParticipantId == joinerId.ToString());

            var participant = NewParticipant(local.Id, code);
            // BouncyCastle's state machine requires creating our round 1 before validating theirs.
            var ourRound1 = participant.CreateRound1PayloadToSend();
            participant.ValidateRound1PayloadReceived(ToPayload(round1));
            await FrameCodec.WriteAsync(stream, ToMessage(ourRound1), cancellationToken).ConfigureAwait(false);
            await FrameCodec.WriteAsync(stream, ToMessage(participant.CreateRound2PayloadToSend()), cancellationToken).ConfigureAwait(false);

            var round2 = await ReadAsync<PairingRound2Message>(stream, cancellationToken).ConfigureAwait(false);
            participant.ValidateRound2PayloadReceived(ToPayload(round2));
            key = DeriveKey(participant.CalculateKeyingMaterial());

            var confirm = await ReadAsync<PairingConfirmMessage>(stream, cancellationToken).ConfigureAwait(false);
            var expected = Mac(key, JoinerLabel, local.Id, joinerId, local.Pin, joinerPin);
            verified = confirm.Mac is { Length: 32 } && CryptographicOperations.FixedTimeEquals(expected, confirm.Mac);
        }
        catch (Exception ex) when (ex is CryptoException or InvalidOperationException or ArgumentException or FormatException
            or PairingProtocolException or ProtocolException or IOException)
        {
            // Any deviation counts as a failed attempt, so aborting mid-protocol does not give free guesses.
            verified = false;
        }

        var attempt = registerAttempt(verified);
        try
        {
            if (attempt == PairingAttemptResult.Accepted && key is not null)
            {
                var joinerId = new DeviceId(request.DeviceId);
                await FrameCodec.WriteAsync(stream, new PairingConfirmMessage(Mac(key, HostLabel, local.Id, joinerId, local.Pin, joinerPin)), cancellationToken).ConfigureAwait(false);
                await FrameCodec.WriteAsync(stream, new PairingResultMessage(true, null, local.Id.Value, localInfo.DisplayName, localInfo.OsDescription, listenPort), cancellationToken).ConfigureAwait(false);
                return true;
            }

            await FrameCodec.WriteAsync(stream, new PairingResultMessage(false, ReasonFor(attempt), Guid.Empty, string.Empty, string.Empty, 0), cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (IOException)
        {
            // Peer went away after the verdict; an accepted pairing stays accepted (the joiner will reconnect).
            return attempt == PairingAttemptResult.Accepted;
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    /// <summary>Joiner side. Returns the host's public identity on success.</summary>
    public static async Task<(PairingStatus Status, PairingResultMessage? Host)> RunJoinerAsync(
        Stream stream,
        string code,
        DeviceIdentity local,
        LocalDeviceInfo localInfo,
        int listenPort,
        CertificatePin hostPin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(localInfo);

        byte[]? key = null;
        try
        {
            var participant = NewParticipant(local.Id, code);
            await FrameCodec.WriteAsync(stream, new PairingRequestMessage(ProtocolLimits.ProtocolVersion, local.Id.Value, localInfo.DisplayName, localInfo.OsDescription, listenPort), cancellationToken).ConfigureAwait(false);
            await FrameCodec.WriteAsync(stream, ToMessage(participant.CreateRound1PayloadToSend()), cancellationToken).ConfigureAwait(false);

            var first = await FrameCodec.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (first is PairingResultMessage early)
            {
                return (StatusFor(early.Reason), null);
            }

            var round1 = first as PairingRound1Message ?? throw new PairingProtocolException();
            Require(DeviceId.TryParse(round1.ParticipantId, out var hostId) && hostId != local.Id);
            participant.ValidateRound1PayloadReceived(ToPayload(round1));
            var round2 = await ReadAsync<PairingRound2Message>(stream, cancellationToken).ConfigureAwait(false);
            var ourRound2 = participant.CreateRound2PayloadToSend();
            participant.ValidateRound2PayloadReceived(ToPayload(round2));
            key = DeriveKey(participant.CalculateKeyingMaterial());

            await FrameCodec.WriteAsync(stream, ToMessage(ourRound2), cancellationToken).ConfigureAwait(false);
            await FrameCodec.WriteAsync(stream, new PairingConfirmMessage(Mac(key, JoinerLabel, hostId, local.Id, hostPin, local.Pin)), cancellationToken).ConfigureAwait(false);

            var answer = await FrameCodec.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (answer is PairingResultMessage rejected)
            {
                return (StatusFor(rejected.Reason), null);
            }

            var confirm = answer as PairingConfirmMessage ?? throw new PairingProtocolException();
            var expected = Mac(key, HostLabel, hostId, local.Id, hostPin, local.Pin);
            if (confirm.Mac is not { Length: 32 } || !CryptographicOperations.FixedTimeEquals(expected, confirm.Mac))
            {
                // The host knew the code but its certificate differs from what we see: man-in-the-middle.
                return (PairingStatus.Failed, null);
            }

            var result = await ReadAsync<PairingResultMessage>(stream, cancellationToken).ConfigureAwait(false);
            Require(result.Success && result.DeviceId == hostId.Value);
            return (PairingStatus.Success, result);
        }
        catch (Exception ex) when (ex is CryptoException or InvalidOperationException or ArgumentException or FormatException or PairingProtocolException)
        {
            return (PairingStatus.Failed, null);
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    private static JPakeParticipant NewParticipant(DeviceId id, string code)
    {
        if (!PairingCode.TryNormalize(code, out var normalized))
        {
            throw new ArgumentException("Invalid pairing code.", nameof(code));
        }

        return new JPakeParticipant(id.ToString(), normalized.ToCharArray(), JPakePrimeOrderGroups.NIST_3072, new Sha256Digest(), new SecureRandom());
    }

    private static byte[] DeriveKey(BcBigInteger keyingMaterial)
    {
        var ikm = keyingMaterial.ToByteArrayUnsigned();
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt: [], info: KdfInfo);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ikm);
        }
    }

    private static byte[] Mac(byte[] key, byte[] label, DeviceId hostId, DeviceId joinerId, CertificatePin hostPin, CertificatePin joinerPin)
    {
        using var stream = new MemoryStream();
        stream.Write(label);
        stream.Write(hostId.Value.ToByteArray());
        stream.Write(joinerId.Value.ToByteArray());
        stream.Write(Convert.FromHexString(hostPin.Hex));
        stream.Write(Convert.FromHexString(joinerPin.Hex));
        return HMACSHA256.HashData(key, stream.ToArray());
    }

    private static string ReasonFor(PairingAttemptResult attempt) => attempt switch
    {
        PairingAttemptResult.Expired => "expired",
        PairingAttemptResult.LockedOut => "locked",
        _ => "rejected",
    };

    internal static PairingStatus StatusFor(string? reason) => reason switch
    {
        "expired" => PairingStatus.Expired,
        "locked" => PairingStatus.LockedOut,
        "closed" or "busy" => PairingStatus.NotAccepting,
        "rejected" => PairingStatus.WrongCode,
        _ => PairingStatus.Failed,
    };

    private static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
        where T : ProtocolMessage =>
        await FrameCodec.ReadAsync(stream, cancellationToken).ConfigureAwait(false) as T ?? throw new PairingProtocolException();

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new PairingProtocolException();
        }
    }

    private static PairingRound1Message ToMessage(JPakeRound1Payload p) => new(
        p.ParticipantId, p.Gx1.ToByteArrayUnsigned(), p.Gx2.ToByteArrayUnsigned(),
        [.. p.KnowledgeProofForX1.Select(b => b.ToByteArrayUnsigned())],
        [.. p.KnowledgeProofForX2.Select(b => b.ToByteArrayUnsigned())]);

    private static PairingRound2Message ToMessage(JPakeRound2Payload p) => new(
        p.ParticipantId, p.A.ToByteArrayUnsigned(), [.. p.KnowledgeProofForX2s.Select(b => b.ToByteArrayUnsigned())]);

    private static JPakeRound1Payload ToPayload(PairingRound1Message m) => new(
        m.ParticipantId ?? throw new PairingProtocolException(), Big(m.Gx1), Big(m.Gx2), Bigs(m.KnowledgeProofForX1), Bigs(m.KnowledgeProofForX2));

    private static JPakeRound2Payload ToPayload(PairingRound2Message m) => new(
        m.ParticipantId ?? throw new PairingProtocolException(), Big(m.A), Bigs(m.KnowledgeProofForX2s));

    private static BcBigInteger Big(byte[]? bytes) =>
        bytes is { Length: > 0 and <= MaxBigIntegerBytes } ? new BcBigInteger(1, bytes) : throw new PairingProtocolException();

    private static BcBigInteger[] Bigs(byte[][]? values) =>
        values is { Length: > 0 and <= 4 } ? [.. values.Select(Big)] : throw new PairingProtocolException();
}

/// <summary>The peer deviated from the pairing protocol.</summary>
public sealed class PairingProtocolException : Exception
{
    public PairingProtocolException()
        : base("Pairing protocol violation.")
    {
    }

    public PairingProtocolException(string message)
        : base(message)
    {
    }

    public PairingProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

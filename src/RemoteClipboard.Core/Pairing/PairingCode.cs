using System.Security.Cryptography;

namespace RemoteClipboard.Core.Pairing;

/// <summary>
/// Short, human-transferable one-time pairing code (6 digits). On its own it has low entropy, so it
/// is never used as a key or hashed into a verifier: it is the password input of a PAKE (J-PAKE),
/// which only allows one online guess per attempt. See docs/SECURITY.md.
/// </summary>
public static class PairingCode
{
    public const int Length = 6;

    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Accepts user input such as "847 291" or "847-291" and normalizes it to "847291".</summary>
    public static bool TryNormalize(string? input, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        Span<char> digits = stackalloc char[Length];
        var count = 0;
        foreach (var c in input)
        {
            if (c is ' ' or '-' or '\t')
            {
                continue;
            }

            if (c is < '0' or > '9' || count == Length)
            {
                return false;
            }

            digits[count++] = c;
        }

        if (count != Length)
        {
            return false;
        }

        code = new string(digits);
        return true;
    }
}

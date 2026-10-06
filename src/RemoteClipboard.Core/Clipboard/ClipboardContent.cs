// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;

namespace RemoteClipboard.Core.Clipboard;

/// <summary>
/// Immutable clipboard payload. Treated as sensitive: <see cref="ToString"/> never exposes
/// the data, and nothing in the codebase may log <see cref="Data"/>.
/// </summary>
public sealed class ClipboardContent
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    private readonly byte[] _data;

    private ClipboardContent(ClipboardFormat format, byte[] data)
    {
        Format = format;
        _data = data;
    }

    public ClipboardFormat Format { get; }

    public ReadOnlyMemory<byte> Data => _data;

    public int Length => _data.Length;

    public bool IsEmpty => _data.Length == 0;

    /// <summary>Creates text content. Line endings are preserved exactly (CRLF stays CRLF).</summary>
    public static ClipboardContent FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ClipboardContent(ClipboardFormat.Text, Utf8.GetBytes(text));
    }

    /// <summary>Creates content from wire bytes. The array is copied.</summary>
    public static ClipboardContent FromBytes(ClipboardFormat format, ReadOnlySpan<byte> data)
    {
        if (format == ClipboardFormat.Unknown || !Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported clipboard format.");
        }

        return new ClipboardContent(format, data.ToArray());
    }

    public string GetText()
    {
        if (Format != ClipboardFormat.Text)
        {
            throw new InvalidOperationException($"Content is {Format}, not text.");
        }

        return Utf8.GetString(_data);
    }

    // Deliberately content-free: protects against accidental logging/debug output.
    public override string ToString() => $"ClipboardContent({Format})";
}

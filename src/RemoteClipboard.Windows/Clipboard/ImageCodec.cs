// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace RemoteClipboard.Windows.Clipboard;

/// <summary>
/// Conversions between the Windows clipboard bitmap (CF_DIB) and PNG, the image format on the wire,
/// with GDI+. Received images are decoded defensively: PNG only and bounded dimensions.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ImageCodec
{
    /// <summary>8K x 8K: larger images are refused (decoding them could exhaust memory).</summary>
    public const long MaxPixels = 8192L * 8192;

    private const int FileHeaderSize = 14;
    private const uint BiBitfields = 3;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPng(ReadOnlySpan<byte> data) => data.StartsWith(PngSignature);

    /// <summary>Encodes a CF_DIB (BITMAPINFOHEADER + optional masks/palette + pixels) as PNG.</summary>
    /// <exception cref="ArgumentException">The bitmap is malformed or unsupported.</exception>
    public static byte[] DibToPng(byte[] dib)
    {
        if (dib.Length < 40)
        {
            throw new ArgumentException("Bitmap too small.", nameof(dib));
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dib);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(32));
        var palette = colorsUsed != 0 ? colorsUsed : bitCount <= 8 ? 1u << bitCount : 0;
        var masks = compression == BiBitfields && headerSize == 40 ? 12u : 0;
        var pixelOffset = FileHeaderSize + headerSize + masks + (palette * 4);
        if (headerSize < 40 || pixelOffset > FileHeaderSize + (uint)dib.Length)
        {
            throw new ArgumentException("Malformed bitmap header.", nameof(dib));
        }

        // A .bmp file is the DIB preceded by a 14-byte file header.
        var file = new byte[FileHeaderSize + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), (uint)file.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), pixelOffset);
        dib.CopyTo(file, FileHeaderSize);

        using var input = new MemoryStream(file, writable: false);
        using var bitmap = new Bitmap(input);
        CheckSize(bitmap);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>Decodes a received PNG into a 24-bit CF_DIB (transparency flattened onto white).</summary>
    /// <exception cref="ArgumentException">Not a valid PNG, or too large.</exception>
    public static byte[] PngToDib(byte[] png)
    {
        if (!IsPng(png))
        {
            throw new ArgumentException("Not a PNG image.", nameof(png));
        }

        using var input = new MemoryStream(png, writable: false);
        using var source = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
        if (!source.RawFormat.Equals(ImageFormat.Png))
        {
            throw new ArgumentException("Not a PNG image.", nameof(png));
        }

        CheckSize(source);
        using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        }

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Bmp);
        return output.ToArray()[FileHeaderSize..];
    }

    private static void CheckSize(Image image)
    {
        if (image.Width <= 0 || image.Height <= 0 || (long)image.Width * image.Height > MaxPixels)
        {
            throw new ArgumentException("Image dimensions are out of range.");
        }
    }
}

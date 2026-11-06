// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Protocol;
using RemoteClipboard.Windows.Clipboard;
using static RemoteClipboard.Windows.Interop.NativeMethods;

namespace RemoteClipboard.Windows.Tests;

/// <summary>Images and files on the real Windows clipboard (shared with the text tests, so sequential).</summary>
[SupportedOSPlatform("windows")]
[Collection("Clipboard")]
public sealed class ImageAndFileClipboardTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly WindowsClipboard _clipboard = new();
    private readonly string _work = Directory.CreateTempSubdirectory("rc-win-tests-").FullName;

    public ImageAndFileClipboardTests()
    {
        _clipboard.ReceivedFiles = new ReceivedFilesStore(Path.Combine(_work, "received"));
        _clipboard.Start();
    }

    public void Dispose()
    {
        _clipboard.Dispose();
        Directory.Delete(_work, recursive: true);
    }

    [Fact]
    public async Task Png_copied_by_an_app_is_sent_as_is()
    {
        var png = SamplePng(64, 48);
        RawClipboard.SetFormats((RawClipboard.Format("PNG"), png));

        var snapshot = await _clipboard.ReadAsync(Ct);

        Assert.Equal(ClipboardFormat.Image, snapshot!.Content!.Format);
        Assert.Equal(png, snapshot.Content.Data.ToArray());
    }

    [Fact]
    public async Task Classic_bitmap_is_converted_to_png()
    {
        RawClipboard.SetFormats((CF_DIB, SampleDib(80, 60)));

        var snapshot = await _clipboard.ReadAsync(Ct);

        var content = snapshot!.Content!;
        Assert.Equal(ClipboardFormat.Image, content.Format);
        using var image = Decode(content.Data.ToArray());
        Assert.Equal(new Size(80, 60), image.Size);
        Assert.Equal(Color.FromArgb(255, 200, 30, 30).ToArgb(), image.GetPixel(5, 5).ToArgb());
    }

    [Fact]
    public async Task Received_image_is_pasteable_by_any_app_and_marked_as_remote()
    {
        var png = SamplePng(40, 30);

        await _clipboard.WriteRemoteContentAsync(ClipboardContent.FromBytes(ClipboardFormat.Image, png), Ct);

        Assert.Equal(png, RawClipboard.GetData(RawClipboard.Format("PNG")));
        var dib = RawClipboard.GetData(CF_DIB)!;
        Assert.Equal(40, BitConverter.ToInt32(dib, 4)); // biWidth
        Assert.True((await _clipboard.ReadAsync(Ct))!.HasRemoteOriginMarker);
    }

    [Fact]
    public async Task Something_that_is_not_a_png_is_refused()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _clipboard.WriteRemoteContentAsync(
            ClipboardContent.FromBytes(ClipboardFormat.Image, [1, 2, 3, 4, 5, 6, 7, 8, 9]), Ct));
    }

    [Fact]
    public async Task Text_wins_over_an_image_copied_together()
    {
        var text = "celdas de Excel";
        var bytes = new byte[(text.Length + 1) * 2];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        RawClipboard.SetFormats((CF_UNICODETEXT, bytes), (CF_DIB, SampleDib(10, 10)));

        var snapshot = await _clipboard.ReadAsync(Ct);

        Assert.Equal(text, snapshot!.Content!.GetText());
    }

    [Fact]
    public async Task Images_are_not_read_when_turned_off()
    {
        _clipboard.ReadImages = false;
        RawClipboard.SetFormats((RawClipboard.Format("PNG"), SamplePng(8, 8)));

        var snapshot = await _clipboard.ReadAsync(Ct);

        Assert.Null(snapshot!.Content);
    }

    [Fact]
    public async Task Files_and_folders_copied_in_explorer_are_archived()
    {
        var file = Path.Combine(_work, "informe ñ.txt");
        File.WriteAllText(file, "contenido");
        var folder = Directory.CreateDirectory(Path.Combine(_work, "Carpeta")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "dato.bin"), [9, 8, 7]);
        RawClipboard.SetFormats((CF_HDROP, DropFiles.Build([file, folder])));

        var snapshot = await _clipboard.ReadAsync(Ct);

        Assert.Equal(ClipboardFormat.FileList, snapshot!.Content!.Format);
        var target = Directory.CreateDirectory(Path.Combine(_work, "check")).FullName;
        var top = FileArchive.Extract(snapshot.Content.Data, target, ProtocolLimits.MaxFileListBytes);
        Assert.Equal("contenido", File.ReadAllText(top[0]));
        Assert.Equal([9, 8, 7], File.ReadAllBytes(Path.Combine(top[1], "dato.bin")));
    }

    [Fact]
    public async Task Received_files_are_pasteable_copies_marked_as_from_another_computer()
    {
        var source = Directory.CreateDirectory(Path.Combine(_work, "origen")).FullName;
        var file = Path.Combine(source, "programa.exe");
        File.WriteAllBytes(file, [0x4D, 0x5A, 1, 2]);
        var archive = FileArchive.Create([file], ProtocolLimits.MaxFileListBytes)!;

        await _clipboard.WriteRemoteContentAsync(ClipboardContent.FromBytes(ClipboardFormat.FileList, archive), Ct);

        var pasted = Assert.Single(RawClipboard.GetFiles());
        Assert.StartsWith(_clipboard.ReceivedFiles.Root, pasted, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([0x4D, 0x5A, 1, 2], File.ReadAllBytes(pasted));
        Assert.Contains("ZoneId=3", File.ReadAllText(pasted + ":Zone.Identifier"), StringComparison.Ordinal);
        Assert.Equal(1, BitConverter.ToInt32(RawClipboard.GetData(RawClipboard.Format("Preferred DropEffect"))!)); // copy
    }

    private static byte[] SamplePng(int width, int height)
    {
        using var bitmap = Sample(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static byte[] SampleDib(int width, int height)
    {
        using var bitmap = Sample(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Bmp);
        return stream.ToArray()[14..]; // CF_DIB = .bmp without its file header
    }

    private static Bitmap Sample(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(200, 30, 30));
        graphics.FillRectangle(Brushes.Blue, width / 2, height / 2, width / 2, height / 2);
        return bitmap;
    }

    private static Bitmap Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return new Bitmap(stream);
    }
}

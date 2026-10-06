// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace RemoteClipboard.App.Tray;

/// <summary>Tray icons from the application icon: full color when syncing, grayscale when idle or paused.</summary>
internal static partial class TrayIconFactory
{
    private static readonly Uri IconUri = new("pack://application:,,,/RemoteClipboard;component/Assets/app.ico");

    public static Icon CreateActive()
    {
        using var stream = System.Windows.Application.GetResourceStream(IconUri)!.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public static Icon CreateIdle(Icon active)
    {
        ArgumentNullException.ThrowIfNull(active);
        using var color = active.ToBitmap();
        using var gray = new Bitmap(color.Width, color.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(gray))
        using (var attributes = new ImageAttributes())
        {
            // Luminance grayscale, slightly faded.
            attributes.SetColorMatrix(new ColorMatrix(
            [
                [0.30f, 0.30f, 0.30f, 0, 0],
                [0.59f, 0.59f, 0.59f, 0, 0],
                [0.11f, 0.11f, 0.11f, 0, 0],
                [0, 0, 0, 0.85f, 0],
                [0, 0, 0, 0, 1],
            ]));
            g.DrawImage(color, new Rectangle(0, 0, color.Width, color.Height), 0, 0, color.Width, color.Height, GraphicsUnit.Pixel, attributes);
        }

        var handle = gray.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);
}

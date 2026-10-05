using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RemoteClipboard.App.Tray;

/// <summary>Draws the tray icon at runtime (no binary assets in the repository yet): a clipboard on a colored tile.</summary>
internal static partial class TrayIconFactory
{
    public static Icon Create(Color tile)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var tileBrush = new SolidBrush(tile);
            using var tilePath = RoundedRect(new RectangleF(1, 1, 30, 30), 7);
            g.FillPath(tileBrush, tilePath);

            using var white = new SolidBrush(Color.White);
            using var board = RoundedRect(new RectangleF(9, 8, 14, 18), 2.5f);
            g.FillPath(white, board);
            using var clip = new SolidBrush(tile);
            using var clipPath = RoundedRect(new RectangleF(12.5f, 5.5f, 7, 5), 1.5f);
            g.FillPath(white, clipPath);
            g.FillRectangle(clip, 12, 14, 8, 2);
            g.FillRectangle(clip, 12, 19, 6, 2);
        }

        var handle = bitmap.GetHicon();
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

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);
}

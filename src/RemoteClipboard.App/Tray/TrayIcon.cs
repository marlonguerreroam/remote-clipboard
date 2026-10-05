using System.Drawing;
using System.Windows.Forms;

namespace RemoteClipboard.App.Tray;

/// <summary>System tray presence. WinForms NotifyIcon is the supported tray API for desktop .NET apps.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;

    public TrayIcon(Action onExit)
    {
        ArgumentNullException.ThrowIfNull(onExit);

        _menu = new ContextMenuStrip();
        _menu.Items.Add(new ToolStripMenuItem("Remote Clipboard — estructura inicial (Fase 0)") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Salir", image: null, (_, _) => onExit()));

        _icon = new NotifyIcon
        {
            // Placeholder until the product icon is added (Phase 2).
            Icon = SystemIcons.Application,
            Text = "Remote Clipboard",
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}

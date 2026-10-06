using System.Drawing;
using System.Windows.Forms;
using RemoteClipboard.App.Services;

namespace RemoteClipboard.App.Tray;

/// <summary>System tray presence: status at a glance and quick actions. WinForms NotifyIcon is the supported API.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly AppController _controller;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _syncItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly Icon _activeIcon = TrayIconFactory.CreateActive();
    private readonly Icon _idleIcon;

    public TrayIcon(AppController controller)
    {
        _controller = controller;
        _idleIcon = TrayIconFactory.CreateIdle(_activeIcon);
        _statusItem = new ToolStripMenuItem { Enabled = false };
        var open = new ToolStripMenuItem("Abrir Remote Clipboard", null, (_, _) => controller.ShowMainWindow());
        open.Font = new Font(open.Font, System.Drawing.FontStyle.Bold);
        _syncItem = new ToolStripMenuItem("Sincronización activa", null, (_, _) => controller.SetSyncEnabled(!controller.Agent.SyncEnabled));
        _autoStartItem = new ToolStripMenuItem("Iniciar con Windows", null, (_, _) => controller.SetAutoStart(!AppController.IsAutoStartEnabled));

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            open,
            new ToolStripMenuItem("Vincular dispositivo…", null, (_, _) => controller.ShowPairing()),
            new ToolStripMenuItem("Configuración…", null, (_, _) => controller.ShowSettings()),
            new ToolStripSeparator(),
            _syncItem,
            _autoStartItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Salir", null, (_, _) => AppController.Exit()),
        ]);

        _icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                controller.ShowMainWindow();
            }
        };
        Update();
    }

    public void Update()
    {
        var connected = _controller.Agent.Devices.Count(d => d.IsConnected);
        var enabled = _controller.Agent.SyncEnabled;
        var status = !enabled ? "Sincronización en pausa"
            : connected == 0 ? "Sin dispositivos conectados"
            : connected == 1 ? "1 dispositivo conectado" : $"{connected} dispositivos conectados";

        _statusItem.Text = status;
        _syncItem.Checked = enabled;
        _autoStartItem.Checked = AppController.IsAutoStartEnabled;
        _icon.Icon = enabled && connected > 0 ? _activeIcon : _idleIcon;
        _icon.Text = Truncate($"Remote Clipboard — {status}", 63); // NotifyIcon limit
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _activeIcon.Dispose();
        _idleIcon.Dispose();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}

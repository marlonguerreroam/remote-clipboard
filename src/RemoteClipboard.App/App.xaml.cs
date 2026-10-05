using System.Windows;
using RemoteClipboard.App.Tray;
using RemoteClipboard.Windows.Storage;

namespace RemoteClipboard.App;

/// <summary>
/// Composition root. The agent runs inside the interactive user's session (also inside RDP sessions),
/// as that user, never elevated, and at most once per user. Phase 1 wires clipboard, networking and sync here.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "WPF owns the Application lifetime; fields are disposed in OnExit.")]
public partial class App : Application
{
    private SingleInstanceLock? _instanceLock;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstanceLock.TryAcquire(AppPaths.DataDirectory, out _instanceLock))
        {
            // Already running for this user (possibly in another session of the same user).
            Shutdown();
            return;
        }

        _tray = new TrayIcon(onExit: Shutdown);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}

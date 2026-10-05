using System.Windows;
using RemoteClipboard.App.Services;
using RemoteClipboard.Windows.Storage;

namespace RemoteClipboard.App;

/// <summary>
/// Entry point. The agent runs inside the interactive user's session (also inside RDP sessions),
/// as that user, never elevated, and at most once per user.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "WPF owns the Application lifetime; fields are disposed in OnExit.")]
public partial class App : Application
{
    private SingleInstanceLock? _instanceLock;
    private AppController? _controller;
    private ActivationSignal? _activation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstanceLock.TryAcquire(AppPaths.DataDirectory, out _instanceLock))
        {
            // Already running for this user: bring its window to front (when it runs in this session).
            ActivationSignal.TrySignalRunningInstance();
            Shutdown();
            return;
        }

        try
        {
            _controller = AppController.Create();
            _controller.Start(showWindow: !e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase));
            _activation = new ActivationSignal(() => Dispatcher.BeginInvoke(_controller.ShowMainWindow));
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                $"Remote Clipboard no pudo iniciarse.\n\n{ex.Message}",
                "Remote Clipboard", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activation?.Dispose();
        _controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}

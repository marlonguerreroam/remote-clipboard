// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

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

        if (!AcquireInstanceLock(restarting: e.Args.Contains("--restart", StringComparer.OrdinalIgnoreCase)))
        {
            // Already running for this user: bring its window to front (when it runs in this session).
            if (!ActivationSignal.TrySignalRunningInstance())
            {
                MessageBox.Show(
                    "Remote Clipboard ya se está ejecutando para tu usuario en otra sesión de Windows.",
                    "Remote Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        try
        {
            _controller = AppController.Create();
            _controller.Start(showWindow: !e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)
                || e.Args.Contains("--restart", StringComparer.OrdinalIgnoreCase));
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

    private bool AcquireInstanceLock(bool restarting)
    {
        // After "restart", the previous instance may still be shutting down: wait for it (max 15 s).
        var deadline = DateTime.UtcNow + (restarting ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
        while (true)
        {
            if (SingleInstanceLock.TryAcquire(AppPaths.DataDirectory, out _instanceLock))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(200);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activation?.Dispose();
        if (_controller is { } controller)
        {
            controller.DisposeUi();

            // OnExit runs on (and blocks) the UI thread. Waiting here for an async shutdown whose
            // continuation needs the UI thread deadlocks: the process then stays alive invisibly, holding
            // the single-instance lock, and the app can no longer be started. So the shutdown runs on the
            // thread pool, bounded in time; whatever is left is ended with the process.
            var shutdown = Task.Run(async () => await controller.DisposeAsync().ConfigureAwait(false));
            shutdown.Wait(TimeSpan.FromSeconds(5));
        }

        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}

// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using RemoteClipboard.App.Tray;
using RemoteClipboard.App.Views;
using RemoteClipboard.Core.Agent;
using RemoteClipboard.Core.Configuration;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Discovery;
using RemoteClipboard.Core.Logging;
using RemoteClipboard.Core.Networking;
using RemoteClipboard.Core.Sync;
using RemoteClipboard.Windows.Clipboard;
using RemoteClipboard.Windows.Platform;
using RemoteClipboard.Windows.Security;
using RemoteClipboard.Windows.Storage;

namespace RemoteClipboard.App.Services;

/// <summary>Builds the object graph and coordinates tray, windows and the agent.</summary>
internal sealed class AppController : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly DeviceIdentity _identity;
    private readonly WindowsClipboard _clipboard;
    private readonly string _settingsPath;
    private AppSettings _settings;
    private TrayIcon? _tray;
    private MainWindow? _mainWindow;
    private PairingWindow? _pairingWindow;
    private SettingsWindow? _settingsWindow;

    private AppController(ILoggerFactory loggerFactory, DeviceIdentity identity, WindowsClipboard clipboard, RemoteClipboardAgent agent, AppSettings settings, string settingsPath)
    {
        _loggerFactory = loggerFactory;
        _identity = identity;
        _clipboard = clipboard;
        Agent = agent;
        _settings = settings;
        _settingsPath = settingsPath;
        DisplayName = settings.DisplayName ?? Environment.MachineName;
    }

    public RemoteClipboardAgent Agent { get; }

    /// <summary>Name announced to other devices (as configured when the agent started).</summary>
    public string DisplayName { get; }

    public AppSettings Settings => _settings;

    public ThemeManager Theme { get; } = new();

    public string DeviceIdText => _identity.Id.ToString();

    public static string Version =>
        typeof(AppController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public string OsDescription { get; } = WindowsSystemInfo.OsDescription;

    public string Fingerprint => _identity.Pin.ToShortDisplay();

    public string LocalAddressesText
    {
        get
        {
            var addresses = NetworkInfo.LocalIPv4Addresses();
            var ip = addresses.Count > 0 ? string.Join(", ", addresses) : "sin red";
            return $"{ip} · puerto {Agent.ListenPort}";
        }
    }

    public static bool IsAutoStartEnabled => AutoStart.IsEnabled;

    public static AppController Create()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new FileLoggerProvider(AppPaths.LogsDirectory)));

        var settingsPath = Path.Combine(AppPaths.DataDirectory, "settings.json");
        var settings = AppSettings.Load(settingsPath);

        var binding = new MachineBinding(WindowsSystemInfo.MachineGuid, WindowsSystemInfo.CurrentUserSid);
        var identity = new DeviceIdentityStore(new DpapiSecretStore(AppPaths.SecretsDirectory), binding).LoadOrCreate(out var identityResult);
        var startupLog = loggerFactory.CreateLogger("Startup");
        Log.IdentityLoaded(startupLog, identity.Id, identityResult.ToString());

        var peers = new PeerStore(Path.Combine(AppPaths.DataDirectory, "peers.json"));
        var clipboard = new WindowsClipboard(loggerFactory.CreateLogger<WindowsClipboard>());
        var agent = new RemoteClipboardAgent(
            identity,
            new LocalDeviceInfo(settings.DisplayName ?? Environment.MachineName, WindowsSystemInfo.OsDescription),
            peers,
            clipboard,
            clipboard,
            new ConnectionOptions { PreferredPort = settings.PreferredPort },
            loggerFactory,
            discovery: new DiscoveryOptions { Enabled = settings.DiscoveryEnabled })
        {
            SyncEnabled = settings.SyncEnabled,
        };

        var controller = new AppController(loggerFactory, identity, clipboard, agent, settings, settingsPath);
        controller.ApplyFirstRunDefaults();
        return controller;
    }

    public void Start(bool showWindow)
    {
        Theme.Apply(_settings.Theme);
        Agent.Start();
        _tray = new TrayIcon(this);
        Agent.StateChanged += (_, _) => OnUi(RefreshUi);
        Agent.Paired += (_, device) => OnUi(() => _tray?.Notify("Dispositivo vinculado", $"{device.DisplayName} ya comparte el portapapeles con este equipo."));
        RefreshUi();
        if (showWindow)
        {
            ShowMainWindow();
        }
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(this);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    public void ShowPairing()
    {
        if (_pairingWindow is null)
        {
            _pairingWindow = new PairingWindow(this) { Owner = _mainWindow?.IsVisible == true ? _mainWindow : null };
            _pairingWindow.Closed += (_, _) => _pairingWindow = null;
        }

        _pairingWindow.Show();
        _pairingWindow.Activate();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this) { Owner = _mainWindow?.IsVisible == true ? _mainWindow : null };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Saves new preferences. Theme applies live; name, LAN visibility and port need a restart.
    /// Returns true when a restart is required for everything to take effect.
    /// </summary>
    public bool SaveSettings(AppSettings updated, bool autoStart)
    {
        ArgumentNullException.ThrowIfNull(updated);
        var restartRequired =
            updated.DisplayName != _settings.DisplayName
            || updated.DiscoveryEnabled != _settings.DiscoveryEnabled
            || updated.PreferredPort != _settings.PreferredPort;

        SaveSettings(updated);
        Theme.Apply(updated.Theme);
        if (autoStart != IsAutoStartEnabled)
        {
            SetAutoStart(autoStart);
        }

        return restartRequired;
    }

    public void SetSyncDirection(DeviceId id, SyncDirection direction) => Agent.SetSyncDirection(id, direction);

    public static void OpenLogsFolder()
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogsDirectory}\"") { UseShellExecute = true });
    }

    /// <summary>Starts a new instance (which waits for this one to release the lock) and exits.</summary>
    public static void Restart()
    {
        if (Environment.ProcessPath is { } path)
        {
            Process.Start(new ProcessStartInfo(path, "--restart") { UseShellExecute = false });
        }

        Exit();
    }

    public void SetSyncEnabled(bool enabled)
    {
        Agent.SyncEnabled = enabled;
        SaveSettings(_settings with { SyncEnabled = enabled });
    }

    public void SetAutoStart(bool enabled)
    {
        if (enabled && Environment.ProcessPath is { } path)
        {
            AutoStart.Enable(path);
        }
        else
        {
            AutoStart.Disable();
        }

        RefreshUi();
    }

    public void Unpair(DeviceId id) => Agent.Unpair(id);

    public static void Exit() => Application.Current.Shutdown();

    /// <summary>Releases UI-thread resources (tray icon, theme hooks). Call on the UI thread, before <see cref="DisposeAsync"/>.</summary>
    public void DisposeUi()
    {
        _tray?.Dispose();
        _tray = null;
        Theme.Dispose();
    }

    /// <summary>
    /// Stops networking, clipboard and logging. Never resumes on the UI thread, so it can be waited on
    /// from a thread pool thread while the UI thread is shutting down.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Agent.DisposeAsync().ConfigureAwait(false);
        _clipboard.Dispose();
        _identity.Dispose();
        _loggerFactory.Dispose();
    }

    private void ApplyFirstRunDefaults()
    {
        if (_settings.AutoStartConfigured)
        {
            // Moved from the portable build to the installed one (or reinstalled elsewhere): keep the
            // user's start-with-Windows choice working by pointing it to this copy.
            if (Environment.ProcessPath is { } current)
            {
                AutoStart.RepairPath(current);
            }

            return;
        }

        // Desktop: start with Windows by default. Windows Server: each user opts in explicitly.
        if (!WindowsSystemInfo.IsServer && Environment.ProcessPath is { } path)
        {
            AutoStart.Enable(path);
        }

        SaveSettings(_settings with { AutoStartConfigured = true });
    }

    private void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        try
        {
            settings.Save(_settingsPath);
        }
        catch (IOException)
        {
            // Preferences are not critical; keep running with in-memory values.
        }
    }

    private void RefreshUi()
    {
        _tray?.Update();
        _mainWindow?.ViewModel.Refresh();
        _pairingWindow?.RefreshDiscovered();
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}

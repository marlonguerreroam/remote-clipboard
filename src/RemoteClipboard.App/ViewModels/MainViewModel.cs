using System.Collections.ObjectModel;
using RemoteClipboard.App.Services;

namespace RemoteClipboard.App.ViewModels;

internal sealed class MainViewModel : ObservableObject
{
    private readonly AppController _controller;
    private bool _syncEnabled;
    private bool _autoStart;

    public MainViewModel(AppController controller)
    {
        _controller = controller;
        Refresh();
    }

    public ObservableCollection<DeviceItem> Devices { get; } = [];

    public bool HasDevices => Devices.Count > 0;

    public bool SyncEnabled
    {
        get => _syncEnabled;
        set
        {
            if (Set(ref _syncEnabled, value))
            {
                _controller.SetSyncEnabled(value);
                RaiseStatus();
            }
        }
    }

    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (Set(ref _autoStart, value))
            {
                _controller.SetAutoStart(value);
            }
        }
    }

    public string StatusTitle => !SyncEnabled ? "En pausa" : ConnectedCount > 0 ? "Protegido" : "Esperando dispositivos";

    public string StatusSubtitle => !SyncEnabled
        ? "La sincronización está desactivada"
        : ConnectedCount switch
        {
            0 when Devices.Count == 0 => "Vincula un dispositivo para empezar",
            0 => "Ningún dispositivo conectado; reintentando automáticamente",
            1 => "Sincronización activa con 1 dispositivo · cifrado TLS",
            var n => $"Sincronización activa con {n} dispositivos · cifrado TLS",
        };

    public bool IsHealthy => SyncEnabled && ConnectedCount > 0;

    public string ThisDeviceName => _controller.DisplayName;

    public string ThisDeviceDetail => $"{_controller.OsDescription} · {_controller.LocalAddressesText}";

    public string ThisDeviceFingerprint => $"Huella {_controller.Fingerprint}";

    private int ConnectedCount => Devices.Count(d => d.IsConnected);

    public void Refresh()
    {
        Devices.Clear();
        foreach (var status in _controller.Agent.Devices)
        {
            Devices.Add(new DeviceItem(status));
        }

        _syncEnabled = _controller.Agent.SyncEnabled;
        _autoStart = AppController.IsAutoStartEnabled;
        Raise(nameof(SyncEnabled));
        Raise(nameof(AutoStart));
        Raise(nameof(HasDevices));
        Raise(nameof(ThisDeviceDetail));
        RaiseStatus();
    }

    private void RaiseStatus()
    {
        Raise(nameof(StatusTitle));
        Raise(nameof(StatusSubtitle));
        Raise(nameof(IsHealthy));
    }
}

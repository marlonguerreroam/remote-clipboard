using RemoteClipboard.App.Services;
using RemoteClipboard.Core.Agent;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Sync;

namespace RemoteClipboard.App.ViewModels;

internal sealed class DeviceItem : ObservableObject
{
    private readonly AppController _controller;
    private SyncDirection _direction;
    private bool _showOptions;

    public DeviceItem(DeviceStatus status, AppController controller, bool showOptions)
    {
        _controller = controller;
        Id = status.Device.Id;
        Name = status.Device.DisplayName;
        Os = status.Device.OsDescription ?? "Windows";
        IsConnected = status.IsConnected;
        Address = status.Address ?? "dirección desconocida";
        _direction = status.Device.Direction;
        _showOptions = showOptions;
    }

    public DeviceId Id { get; }

    public string Name { get; }

    public string Os { get; }

    public bool IsConnected { get; }

    public string Address { get; }

    public string StatusText => IsConnected ? "Conectado" : "Desconectado";

    public string Detail => IsConnected ? $"{Os} · {Address}" : $"{Os} · última dirección {Address}";

    public string DirectionText => Direction switch
    {
        SyncDirection.SendOnly => "Solo enviar",
        SyncDirection.ReceiveOnly => "Solo recibir",
        _ => "Enviar y recibir",
    };

    public SyncDirection Direction
    {
        get => _direction;
        set
        {
            if (Set(ref _direction, value))
            {
                _controller.SetSyncDirection(Id, value);
                Raise(nameof(DirectionText));
            }
        }
    }

    public bool ShowOptions
    {
        get => _showOptions;
        set => Set(ref _showOptions, value);
    }
}

using RemoteClipboard.Core.Agent;
using RemoteClipboard.Core.Devices;

namespace RemoteClipboard.App.ViewModels;

internal sealed class DeviceItem(DeviceStatus status)
{
    public DeviceId Id { get; } = status.Device.Id;

    public string Name { get; } = status.Device.DisplayName;

    public string Os { get; } = status.Device.OsDescription ?? "Windows";

    public bool IsConnected { get; } = status.IsConnected;

    public string StatusText => IsConnected ? "Conectado" : "Desconectado";

    public string Address { get; } = status.Address ?? "Dirección desconocida";

    public string Detail => IsConnected ? $"{Os} · {Address}" : $"{Os} · última dirección {Address}";
}

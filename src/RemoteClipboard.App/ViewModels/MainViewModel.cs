// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using RemoteClipboard.App.Services;
using RemoteClipboard.Core.Licensing;

namespace RemoteClipboard.App.ViewModels;

internal sealed class MainViewModel : ObservableObject
{
    private readonly AppController _controller;
    private bool _syncEnabled;

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

    public string StatusTitle => !CanSync ? "Prueba finalizada" : !SyncEnabled ? "En pausa" : ConnectedCount > 0 ? "Protegido" : "Esperando dispositivos";

    public string StatusSubtitle => !CanSync
        ? "Activa una licencia para seguir sincronizando (Acerca de → Licencia)"
        : !SyncEnabled
        ? "La sincronización está desactivada"
        : ConnectedCount switch
        {
            0 when Devices.Count == 0 => "Vincula un dispositivo para empezar",
            0 => "Ningún dispositivo conectado; reintentando automáticamente",
            1 => "Sincronización activa con 1 dispositivo · cifrado TLS",
            var n => $"Sincronización activa con {n} dispositivos · cifrado TLS",
        };

    public bool IsHealthy => SyncEnabled && ConnectedCount > 0;

    public string AgentStatusTitle => SyncEnabled ? "Activo" : "En pausa";

    public string AgentStatusSubtitle => _controller.Licensing.State switch
    {
        LicenseState.Trial => $"Prueba: {DaysText(_controller.Licensing.TrialDaysLeft)}",
        LicenseState.TrialExpired => "Prueba finalizada",
        _ => SyncEnabled ? "Listo para sincronizar" : "Sincronización desactivada",
    };

    /// <summary>False when the trial ended without a license: the sync switch is disabled.</summary>
    public bool CanSync => _controller.Licensing.IsSyncAllowed;

    public bool ShowLicense => _controller.Licensing.State != LicenseState.NotRequired;

    public bool IsLicensed => _controller.Licensing.State == LicenseState.Licensed;

    public bool ShowActivation => ShowLicense && !IsLicensed;

    public bool CanBuy => ShowActivation && !string.IsNullOrEmpty(LicensingConfig.PurchaseUrl);

    public string LicenseTitle => _controller.Licensing.State switch
    {
        LicenseState.Licensed => $"Licencia {(_controller.Licensing.License!.Edition == LicenseEdition.Business ? "empresarial" : "personal")}",
        LicenseState.Trial => $"Prueba gratuita · {DaysText(_controller.Licensing.TrialDaysLeft)}",
        LicenseState.TrialExpired => "La prueba gratuita ha terminado",
        _ => string.Empty,
    };

    public string LicenseDetail => _controller.Licensing.State switch
    {
        LicenseState.Licensed => $"A nombre de {_controller.Licensing.License!.Licensee} · emitida el {_controller.Licensing.License.Issued:dd/MM/yyyy}. ¡Gracias por tu compra!",
        LicenseState.Trial => "Todo funciona durante la prueba. Para seguir sincronizando después, activa una licencia.",
        LicenseState.TrialExpired => "La sincronización está desactivada. Activa una licencia para volver a usarla; tus equipos vinculados se conservan.",
        _ => string.Empty,
    };

    public string ThisDeviceName => _controller.DisplayName;

    public string ThisDeviceDetail => $"{_controller.OsDescription} · {_controller.LocalAddressesText}";

    public string ThisDeviceFingerprint => $"Huella {_controller.Fingerprint}";

    private static string DaysText(int days) => days == 1 ? "queda 1 día" : $"quedan {days} días";

    private int ConnectedCount => Devices.Count(d => d.IsConnected);

    public void Refresh()
    {
        // Keep expanded option panels open across refreshes (connection changes happen any time).
        var expanded = Devices.Where(d => d.ShowOptions).Select(d => d.Id).ToHashSet();
        Devices.Clear();
        foreach (var status in _controller.Agent.Devices)
        {
            Devices.Add(new DeviceItem(status, _controller, expanded.Contains(status.Device.Id)));
        }

        _syncEnabled = _controller.Agent.SyncEnabled;
        Raise(nameof(SyncEnabled));
        Raise(nameof(ThisDeviceName));
        Raise(nameof(HasDevices));
        Raise(nameof(ThisDeviceDetail));
        RaiseStatus();
    }

    private void RaiseStatus()
    {
        Raise(nameof(StatusTitle));
        Raise(nameof(StatusSubtitle));
        Raise(nameof(IsHealthy));
        Raise(nameof(AgentStatusTitle));
        Raise(nameof(AgentStatusSubtitle));
        Raise(nameof(CanSync));
        Raise(nameof(ShowLicense));
        Raise(nameof(IsLicensed));
        Raise(nameof(ShowActivation));
        Raise(nameof(CanBuy));
        Raise(nameof(LicenseTitle));
        Raise(nameof(LicenseDetail));
    }
}

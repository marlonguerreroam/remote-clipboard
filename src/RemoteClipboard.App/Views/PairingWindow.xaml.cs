using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RemoteClipboard.App.Services;
using System.Globalization;
using RemoteClipboard.Core.Devices;
using RemoteClipboard.Core.Discovery;
using RemoteClipboard.Core.Networking;
using RemoteClipboard.Core.Pairing;
using RemoteClipboard.Core.Protocol;

namespace RemoteClipboard.App.Views;

public sealed partial class PairingWindow : Window
{
    private readonly AppController _controller;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private PairingWindowState? _window;

    internal PairingWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        controller.Theme.Attach(this);
        _timer.Tick += (_, _) => UpdateExpiry();
        _controller.Agent.Paired += OnPairedAsHost;
        _controller.Agent.DiscoveredDevicesChanged += OnDiscoveredChanged;
        Closed += OnClosed;
        OnShowMode(this, new RoutedEventArgs());
    }

    private void OnShowMode(object sender, RoutedEventArgs e)
    {
        SetMode(showCode: true);
        if (_window is null || !_window.Window.IsOpen)
        {
            OpenNewCode();
        }
    }

    private void OnEnterMode(object sender, RoutedEventArgs e)
    {
        SetMode(showCode: false);
        _controller.Agent.ClosePairing();
        _window = null;
        _timer.Stop();
        _controller.Agent.RefreshDiscovery();
        RefreshDiscovered();
        AddressBox.Focus();
    }

    /// <summary>Devices found on the LAN; those showing a code first.</summary>
    internal void RefreshDiscovered()
    {
        var devices = _controller.Agent.DiscoveredDevices
            .OrderByDescending(d => d.AcceptingPairing)
            .ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        DiscoveredList.ItemsSource = devices;
        DiscoveryEmpty.Text = !_controller.Agent.IsDiscoveryEnabled
            ? "La visibilidad en la red está desactivada (Configuración). Escribe la dirección manualmente."
            : devices.Count == 0
                ? "Buscando… Si el otro equipo no aparece, pulsa “Mostrar código” en él o escribe su dirección."
                : string.Empty;
        DiscoveryEmpty.Visibility = DiscoveryEmpty.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDiscoveredClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DiscoveredDevice device)
        {
            return;
        }

        AddressBox.Text = device.Port == ProtocolLimits.DefaultTcpPort
            ? device.Address
            : string.Create(CultureInfo.InvariantCulture, $"{device.Address}:{device.Port}");
        CodeBox.Focus();
    }

    private void OnNewCode(object sender, RoutedEventArgs e) => OpenNewCode();

    private void OpenNewCode()
    {
        var window = _controller.Agent.OpenPairing();
        _window = new PairingWindowState(window);
        CodeText.Text = $"{window.Code[..3]} {window.Code[3..]}";
        AddressText.Text = $"Este equipo: {_controller.DisplayName}\n{_controller.LocalAddressesText}";
        ShowResult(string.Empty, success: true);
        _timer.Start();
        UpdateExpiry();
    }

    private void UpdateExpiry()
    {
        if (_window is null)
        {
            return;
        }

        var remaining = _window.Window.ExpiresAt - DateTimeOffset.UtcNow;
        if (!_window.Window.IsOpen || remaining <= TimeSpan.Zero)
        {
            ExpiryText.Text = "El código ya no es válido. Genera uno nuevo.";
            CodeText.Opacity = 0.35;
            _timer.Stop();
            return;
        }

        CodeText.Opacity = 1;
        ExpiryText.Text = $"Expira en {remaining:m\\:ss}";
    }

    private async void OnPair(object sender, RoutedEventArgs e)
    {
        if (!EndpointParser.TryParse(AddressBox.Text, out var host, out var port))
        {
            ShowResult("Introduce una dirección válida, por ejemplo 192.168.1.20.", success: false);
            return;
        }

        if (!PairingCode.TryNormalize(CodeBox.Text, out var code))
        {
            ShowResult("El código tiene 6 dígitos.", success: false);
            return;
        }

        PairButton.IsEnabled = false;
        ShowResult("Vinculando…", success: true);
        try
        {
            var outcome = await _controller.Agent.PairWithAsync(host, port, code, CancellationToken.None).ConfigureAwait(true);
            if (outcome.Succeeded)
            {
                Succeeded(outcome.Device!);
                return;
            }

            ShowResult(outcome.Status switch
            {
                PairingStatus.WrongCode => "Código incorrecto. Revisa el código del otro equipo.",
                PairingStatus.LockedOut => "Demasiados intentos. Genera un código nuevo en el otro equipo.",
                PairingStatus.Expired => "El código expiró. Genera uno nuevo en el otro equipo.",
                PairingStatus.NotAccepting => "El otro equipo no está mostrando un código de vinculación.",
                PairingStatus.Unreachable => "No se pudo conectar. Comprueba la dirección, que ambos estén en la misma red y el firewall.",
                _ => "No se pudo verificar el otro equipo. Inténtalo de nuevo.",
            }, success: false);
        }
        finally
        {
            PairButton.IsEnabled = true;
        }
    }

    private void OnCodeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnPair(sender, e);
        }
    }

    private void OnDiscoveredChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshDiscovered);

    private void OnPairedAsHost(object? sender, PairedDevice device) =>
        Dispatcher.BeginInvoke(() => Succeeded(device));

    private void Succeeded(PairedDevice device)
    {
        _timer.Stop();
        ShowResult($"✓ Vinculado con {device.DisplayName}. El portapapeles ya se comparte.", success: true);
        var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        close.Tick += (_, _) =>
        {
            close.Stop();
            Close();
        };
        close.Start();
    }

    private void SetMode(bool showCode)
    {
        ShowPanel.Visibility = showCode ? Visibility.Visible : Visibility.Collapsed;
        EnterPanel.Visibility = showCode ? Visibility.Collapsed : Visibility.Visible;
        ShowModeButton.Style = (Style)FindResource(showCode ? "PrimaryButton" : "GhostButton");
        EnterModeButton.Style = (Style)FindResource(showCode ? "GhostButton" : "PrimaryButton");
        ShowResult(string.Empty, success: true);
    }

    private void ShowResult(string text, bool success)
    {
        ResultText.Text = text;
        ResultText.Foreground = (Brush)FindResource(success ? "Success" : "Danger");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _controller.Agent.Paired -= OnPairedAsHost;
        _controller.Agent.DiscoveredDevicesChanged -= OnDiscoveredChanged;
        _controller.Agent.ClosePairing();
    }

    private sealed record PairingWindowState(Core.Pairing.PairingWindow Window);
}

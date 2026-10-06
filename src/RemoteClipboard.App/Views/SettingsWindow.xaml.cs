using System.Globalization;
using System.Windows;
using RemoteClipboard.App.Services;
using RemoteClipboard.Core.Configuration;

namespace RemoteClipboard.App.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly AppTheme _originalTheme;
    private bool _saved;

    internal SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        controller.Theme.Attach(this);

        var settings = controller.Settings;
        _originalTheme = settings.Theme;
        NameBox.Text = settings.DisplayName ?? string.Empty;
        NameHint.Text = $"Vacío = nombre del equipo ({Environment.MachineName}).";
        DiscoveryBox.IsChecked = settings.DiscoveryEnabled;
        PortBox.Text = settings.PreferredPort.ToString(CultureInfo.InvariantCulture);
        AutoStartBox.IsChecked = AppController.IsAutoStartEnabled;
        (settings.Theme switch
        {
            AppTheme.Light => ThemeLight,
            AppTheme.Dark => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;
        AboutText.Text = $"Remote Clipboard {AppController.Version}\nID del dispositivo: {controller.DeviceIdText}\nHuella de la clave: {controller.Fingerprint}";
        Closed += (_, _) =>
        {
            if (!_saved)
            {
                _controller.Theme.Apply(_originalTheme); // undo the live preview
            }
        };
    }

    private AppTheme SelectedTheme =>
        ThemeDark.IsChecked == true ? AppTheme.Dark : ThemeLight.IsChecked == true ? AppTheme.Light : AppTheme.System;

    private void OnThemePreview(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            _controller.Theme.Apply(SelectedTheme);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1024 or > 65535)
        {
            ErrorText.Text = "El puerto debe ser un número entre 1024 y 65535.";
            return;
        }

        var updated = _controller.Settings with
        {
            DisplayName = AppSettings.NormalizeDisplayName(NameBox.Text),
            DiscoveryEnabled = DiscoveryBox.IsChecked == true,
            PreferredPort = port,
            Theme = SelectedTheme,
        };

        _saved = true;
        var restartRequired = _controller.SaveSettings(updated, AutoStartBox.IsChecked == true);
        if (restartRequired)
        {
            var answer = MessageBox.Show(this,
                "Algunos cambios (nombre, visibilidad en la red o puerto) se aplican al reiniciar Remote Clipboard.\n\n¿Reiniciar ahora?",
                "Reiniciar Remote Clipboard", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Yes)
            {
                AppController.Restart();
                return;
            }
        }

        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnOpenLogs(object sender, RoutedEventArgs e) => AppController.OpenLogsFolder();
}

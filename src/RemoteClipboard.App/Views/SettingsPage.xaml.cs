// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RemoteClipboard.App.Services;
using RemoteClipboard.Core.Configuration;

namespace RemoteClipboard.App.Views;

/// <summary>
/// Settings page of the main window. The theme previews live while choosing; leaving the page without
/// saving restores the saved theme.
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    private AppController? _controller;
    private bool _loading;

    public SettingsPage() => InitializeComponent();

    private AppController Controller => _controller ?? throw new InvalidOperationException("SettingsPage is not attached.");

    internal void Attach(AppController controller) => _controller = controller;

    /// <summary>Shows the saved values. Call when the page is opened.</summary>
    internal void Load()
    {
        var settings = Controller.Settings;
        _loading = true;
        NameBox.Text = settings.DisplayName ?? string.Empty;
        NameHint.Text = $"Vacío = nombre del equipo ({Environment.MachineName}).";
        DiscoveryBox.IsChecked = settings.DiscoveryEnabled;
        PortBox.Text = settings.PreferredPort.ToString(CultureInfo.InvariantCulture);
        AutoStartBox.IsChecked = AppController.IsAutoStartEnabled;
        AutoStartBox.Visibility = AppController.CanManageAutoStart ? Visibility.Visible : Visibility.Collapsed;
        AutoStartManagedText.Visibility = AppController.CanManageAutoStart ? Visibility.Collapsed : Visibility.Visible;
        (settings.Theme switch
        {
            AppTheme.Light => ThemeLight,
            AppTheme.Dark => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;
        _loading = false;
        ShowMessage(string.Empty, success: true);
    }

    /// <summary>Undoes an unsaved theme preview. Call when the page is left or the window closes.</summary>
    internal void Leave()
    {
        if (_controller is not null && SelectedTheme != _controller.Settings.Theme)
        {
            _controller.Theme.Apply(_controller.Settings.Theme);
        }
    }

    private AppTheme SelectedTheme =>
        ThemeDark.IsChecked == true ? AppTheme.Dark : ThemeLight.IsChecked == true ? AppTheme.Light : AppTheme.System;

    private void OnThemePreview(object sender, RoutedEventArgs e)
    {
        if (!_loading && _controller is not null)
        {
            _controller.Theme.Apply(SelectedTheme);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1024 or > 65535)
        {
            ShowMessage("El puerto debe ser un número entre 1024 y 65535.", success: false);
            return;
        }

        var updated = Controller.Settings with
        {
            DisplayName = AppSettings.NormalizeDisplayName(NameBox.Text),
            DiscoveryEnabled = DiscoveryBox.IsChecked == true,
            PreferredPort = port,
            Theme = SelectedTheme,
        };

        var restartRequired = Controller.SaveSettings(updated, AutoStartBox.IsChecked == true);
        ShowMessage("Cambios guardados.", success: true);
        if (restartRequired)
        {
            var answer = MessageBox.Show(Window.GetWindow(this)!,
                "Algunos cambios (nombre, visibilidad en la red o puerto) se aplican al reiniciar Remote Clipboard.\n\n¿Reiniciar ahora?",
                "Reiniciar Remote Clipboard", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Yes)
            {
                AppController.Restart();
            }
        }
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        Leave();
        Load();
    }

    private void ShowMessage(string text, bool success)
    {
        MessageText.Text = text;
        MessageText.Foreground = (Brush)FindResource(success ? "Success" : "Danger");
    }
}

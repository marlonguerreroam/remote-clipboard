// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RemoteClipboard.App.Services;
using RemoteClipboard.App.ViewModels;

namespace RemoteClipboard.App.Views;

internal enum MainPage
{
    Devices,
    Settings,
    About,
}

public sealed partial class MainWindow : Window
{
    private const string SourceUrl = "https://github.com/marlonguerreroam/remote-clipboard";

    private readonly AppController _controller;
    private MainPage _page = MainPage.Devices;

    internal MainWindow(AppController controller)
    {
        _controller = controller;
        ViewModel = new MainViewModel(controller);
        DataContext = ViewModel;
        InitializeComponent();
        controller.Theme.Attach(this);
        SettingsView.Attach(controller);
        AboutVersion.Text = $"Remote Clipboard {AppController.Version}";
        AboutDeviceId.Text = controller.DeviceIdText;
        AboutFingerprint.Text = controller.Fingerprint;
        Closed += (_, _) => SettingsView.Leave();
    }

    internal MainViewModel ViewModel { get; }

    internal void ShowPage(MainPage page) =>
        (page switch
        {
            MainPage.Settings => NavSettings,
            MainPage.About => NavAbout,
            _ => NavDevices,
        }).IsChecked = true;

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || DevicesPage is null)
        {
            return; // the first item is checked while the XAML is still loading
        }

        var page = sender == NavSettings ? MainPage.Settings : sender == NavAbout ? MainPage.About : MainPage.Devices;
        if (_page == MainPage.Settings && page != MainPage.Settings)
        {
            SettingsView.Leave();
        }

        if (page == MainPage.Settings)
        {
            SettingsView.Load();
        }

        _page = page;
        DevicesPage.Visibility = page == MainPage.Devices ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = page == MainPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == MainPage.About ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPairClick(object sender, RoutedEventArgs e) => _controller.ShowPairing();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowPage(MainPage.Settings);

    private void OnOpenLogs(object sender, RoutedEventArgs e) => AppController.OpenLogsFolder();

    // Opened only when the user clicks: the app itself never contacts the Internet.
    private void OnOpenSource(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(SourceUrl) { UseShellExecute = true })?.Dispose();

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DeviceItem device } button)
        {
            return;
        }

        var unpair = new MenuItem
        {
            Header = "Desvincular",
            Icon = new TextBlock { Text = "", Style = (Style)FindResource("Glyph"), FontSize = 13 },
        };
        unpair.SetResourceReference(ForegroundProperty, "Danger");
        unpair.Click += (_, _) => ConfirmUnpair(device);

        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(unpair);
        menu.IsOpen = true;
    }

    private void ConfirmUnpair(DeviceItem device)
    {
        var answer = MessageBox.Show(this,
            $"¿Desvincular «{device.Name}»?\n\nDejará de compartir el portapapeles con este equipo hasta que lo vincules de nuevo.",
            "Desvincular dispositivo", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            _controller.Unpair(device.Id);
        }
    }
}

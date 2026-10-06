using System.Windows;
using RemoteClipboard.App.Services;
using RemoteClipboard.App.ViewModels;

namespace RemoteClipboard.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly AppController _controller;

    internal MainWindow(AppController controller)
    {
        _controller = controller;
        ViewModel = new MainViewModel(controller);
        DataContext = ViewModel;
        InitializeComponent();
        controller.Theme.Attach(this);
    }

    internal MainViewModel ViewModel { get; }

    private void OnPairClick(object sender, RoutedEventArgs e) => _controller.ShowPairing();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _controller.ShowSettings();

    private void OnUnpairClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceItem device)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"¿Desvincular «{device.Name}»?\n\nDejará de compartir el portapapeles con este equipo hasta que lo vincules de nuevo.",
            "Desvincular dispositivo", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            _controller.Unpair(device.Id);
        }
    }
}

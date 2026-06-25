using System.Windows;
using PrinterScanner.App.Services;
using PrinterScanner.App.ViewModels;

namespace PrinterScanner.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private bool hasLoaded;

    public MainWindow()
    {
        InitializeComponent();

        var settingsService       = new SettingsService();
        var logService            = new FileLogService(settingsService.AppDataDirectory);
        var ipRangeService        = new IpRangeService();
        var networkScannerService = new NetworkScannerService(ipRangeService, logService);
        var printerIpService      = new PrinterIpService();
        var driverService         = new DriverService();
        var deviceNameService     = new DeviceNameService(settingsService.AppDataDirectory);
        var printerWindowsService = new PrinterWindowsService();

        viewModel = new MainViewModel(
            settingsService,
            ipRangeService,
            networkScannerService,
            logService,
            printerIpService,
            driverService,
            deviceNameService,
            printerWindowsService);

        DataContext = viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (hasLoaded)
        {
            return;
        }

        try
        {
            hasLoaded = true;
            await viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Falha durante a inicializacao da tela:{Environment.NewLine}{ex.Message}",
                "Erro ao carregar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }
}

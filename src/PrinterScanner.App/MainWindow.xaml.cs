using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PrinterScanner.App.Services;
using PrinterScanner.App.ViewModels;
using PrinterScanner.App.Views;

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
        var utilitariosService    = new UtilitariosService();

        viewModel = new MainViewModel(
            settingsService,
            ipRangeService,
            networkScannerService,
            logService,
            printerIpService,
            driverService,
            deviceNameService,
            printerWindowsService,
            utilitariosService);

        DataContext = viewModel;
    }

    private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null) return;

        switch (cell.Column?.DisplayIndex)
        {
            case 0: // IP / Porta
                e.Handled = true;
                if (viewModel.SelectedDevice?.IsUsbDevice == true)
                    viewModel.ChangeUsbPortCommand.Execute(null);
                else
                    viewModel.ChangeIpCommand.Execute(null);
                break;

            case 8: // Offline
                e.Handled = true;
                viewModel.ToggleWorkOfflineCommand.Execute(null);
                break;

            case 9: // Fila
                e.Handled = true;
                viewModel.ResumePrintQueueCommand.Execute(null);
                break;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T t) return t;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void BtnAcoes_Click(object sender, RoutedEventArgs e)
    {
        var existing = OwnedWindows.OfType<ActionsWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }

        var win = new ActionsWindow(viewModel)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        win.Show();
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

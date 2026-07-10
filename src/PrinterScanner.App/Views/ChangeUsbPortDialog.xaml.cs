using System.Windows;
using PrinterScanner.App.Services;

namespace PrinterScanner.App.Views;

public partial class ChangeUsbPortDialog : Window
{
    private readonly ChangeUsbPortViewModel vm;

    public ChangeUsbPortDialog(
        string printerName, string currentPort,
        PrinterWindowsService printerWindowsService)
    {
        InitializeComponent();
        vm = new ChangeUsbPortViewModel(printerName, currentPort, printerWindowsService);
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await vm.LoadPortsAsync();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var success = await vm.ApplyAsync();
        if (success && IsVisible) DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

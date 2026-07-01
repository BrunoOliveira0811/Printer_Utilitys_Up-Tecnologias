using System.Windows;
using PrinterScanner.App.Services;

namespace PrinterScanner.App.Views;

public partial class ChangeIpDialog : Window
{
    private readonly ChangeIpViewModel vm;

    public ChangeIpDialog(string currentIp, string currentMask, string currentGateway, PrinterIpService printerIpService)
    {
        InitializeComponent();
        vm = new ChangeIpViewModel(currentIp, currentMask, currentGateway, printerIpService);
        DataContext = vm;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var success = await vm.ApplyAsync();
        if (success && IsVisible) DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        vm.RequestCancel();
        DialogResult = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        vm.RequestCancel();
        base.OnClosing(e);
    }
}

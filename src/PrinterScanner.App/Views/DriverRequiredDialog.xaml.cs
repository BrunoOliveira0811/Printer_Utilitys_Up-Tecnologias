using System.Windows;

namespace PrinterScanner.App.Views;

public partial class DriverRequiredDialog : Window
{
    public DriverRequiredDialog()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)   => DialogResult = false;
    private void OpenUtils_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

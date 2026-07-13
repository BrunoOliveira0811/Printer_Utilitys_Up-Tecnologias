using System.Windows;
using PrinterScanner.App.ViewModels;

namespace PrinterScanner.App.Views;

public partial class ActionsWindow : Window
{
    private readonly MainViewModel _vm;

    public ActionsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void BtnAlterarIp_Click(object sender, RoutedEventArgs e)        => Close();
    private void BtnInstalarDriver_Click(object sender, RoutedEventArgs e)   => Close();
    private void BtnUtilitarios_Click(object sender, RoutedEventArgs e)      => Close();
    private void BtnSalvarRelatorio_Click(object sender, RoutedEventArgs e)  => Close();
    private void BtnLimparFila_Click(object sender, RoutedEventArgs e)       => Close();
    private void BtnLimparSpooler_Click(object sender, RoutedEventArgs e)    => Close();
    private void BtnPausarFila_Click(object sender, RoutedEventArgs e)       => Close();
    private void BtnAlterarPortaUsb_Click(object sender, RoutedEventArgs e) => Close();
    private void BtnImpressaoTeste_Click(object sender, RoutedEventArgs e)   => Close();

    private void BtnBuscaAmpliada_Click(object sender, RoutedEventArgs e)
    {
        var owner = Owner ?? System.Windows.Application.Current.MainWindow;
        var existing = owner.OwnedWindows.OfType<BuscaAmpliadaWindow>().FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
        }
        else
        {
            var win = new BuscaAmpliadaWindow(_vm) { Owner = owner };
            win.Show();
        }
        Close();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrinterScanner.App.Views;

public partial class UtilitariosWindow : Window
{
    private readonly UtilitariosViewModel viewModel;

    public UtilitariosWindow(UtilitariosViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (viewModel.SelectedUtilitario is not null)
            viewModel.Launch();
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e) => Close();
}

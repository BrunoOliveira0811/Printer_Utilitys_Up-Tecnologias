using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using PrinterScanner.App.ViewModels;

namespace PrinterScanner.App.Views;

public partial class BuscaAmpliadaWindow : Window
{
    private readonly MainViewModel vm;

    public BuscaAmpliadaWindow(MainViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;

        vm.BuscaAmpliadaLog.CollectionChanged += OnLogChanged;
        Closed += (_, _) => vm.BuscaAmpliadaLog.CollectionChanged -= OnLogChanged;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ = vm.BroadcastScanAsync();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }

    private void BtnBuscarNovamente_Click(object sender, RoutedEventArgs e)
    {
        // O Command já inicia a busca; o log é limpo em BroadcastScanAsync
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e) => Close();
}

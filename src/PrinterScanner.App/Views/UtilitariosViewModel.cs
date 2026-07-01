using System.Collections.ObjectModel;
using System.Windows;
using PrinterScanner.App.Infrastructure;
using PrinterScanner.App.Models;
using PrinterScanner.App.Services;

namespace PrinterScanner.App.Views;

public sealed class UtilitariosViewModel : ObservableObject
{
    private readonly UtilitariosService service;
    private UtilitarioItem? selectedUtilitario;

    public UtilitariosViewModel(UtilitariosService service)
    {
        this.service = service;

        Utilitarios  = new ObservableCollection<UtilitarioItem>(service.GetUtilitarios());
        AbrirCommand = new RelayCommand(_ => Launch(), _ => selectedUtilitario is not null);
    }

    public ObservableCollection<UtilitarioItem> Utilitarios { get; }
    public RelayCommand AbrirCommand { get; }

    public bool IsEmpty         => Utilitarios.Count == 0;
    public bool HasUtilitarios  => Utilitarios.Count > 0;

    public UtilitarioItem? SelectedUtilitario
    {
        get => selectedUtilitario;
        set
        {
            SetProperty(ref selectedUtilitario, value);
            AbrirCommand.RaiseCanExecuteChanged();
        }
    }

    public void Launch()
    {
        if (selectedUtilitario is null) return;
        try
        {
            service.Launch(selectedUtilitario);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Nao foi possivel abrir o utilitario:\n{ex.Message}",
                "Erro ao abrir",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

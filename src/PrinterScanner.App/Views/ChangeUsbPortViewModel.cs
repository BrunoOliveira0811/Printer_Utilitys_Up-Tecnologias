using System.Collections.ObjectModel;
using PrinterScanner.App.Infrastructure;
using PrinterScanner.App.Services;

namespace PrinterScanner.App.Views;

public sealed class ChangeUsbPortViewModel : ObservableObject
{
    private readonly PrinterWindowsService printerWindowsService;
    private string selectedPort;
    private bool isLoading;
    private string statusMessage = string.Empty;
    private bool hasStatus;

    public ChangeUsbPortViewModel(
        string printerName, string currentPort,
        PrinterWindowsService printerWindowsService)
    {
        this.printerWindowsService = printerWindowsService;
        PrinterName  = printerName;
        CurrentPort  = currentPort;
        selectedPort = currentPort;
        AvailablePorts = [];
    }

    public string PrinterName  { get; }
    public string CurrentPort  { get; }
    public ObservableCollection<string> AvailablePorts { get; }

    public string SelectedPort
    {
        get => selectedPort;
        set { SetProperty(ref selectedPort, value); RaisePropertyChanged(nameof(CanApply)); }
    }

    public bool IsLoading
    {
        get => isLoading;
        private set { SetProperty(ref isLoading, value); RaisePropertyChanged(nameof(CanApply)); }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set { SetProperty(ref statusMessage, value); HasStatus = !string.IsNullOrEmpty(value); }
    }

    public bool HasStatus
    {
        get => hasStatus;
        private set => SetProperty(ref hasStatus, value);
    }

    public bool CanApply =>
        !IsLoading &&
        !string.IsNullOrEmpty(SelectedPort) &&
        SelectedPort != CurrentPort;

    public async Task LoadPortsAsync()
    {
        IsLoading = true;
        try
        {
            var ports = await printerWindowsService.GetAvailableUsbPortsAsync();
            AvailablePorts.Clear();
            foreach (var p in ports)
                AvailablePorts.Add(p);
            if (!AvailablePorts.Contains(CurrentPort))
                AvailablePorts.Insert(0, CurrentPort);
            SelectedPort = CurrentPort;
        }
        finally { IsLoading = false; }
    }

    public async Task<bool> ApplyAsync()
    {
        IsLoading = true;
        StatusMessage = $"Alterando porta para {SelectedPort}...";
        try
        {
            var ok = await printerWindowsService.ChangeUsbPortAsync(PrinterName, SelectedPort);
            StatusMessage = ok
                ? $"Porta alterada para {SelectedPort} com sucesso."
                : "Falha ao alterar a porta. Verifique as permissoes de administrador.";
            return ok;
        }
        finally { IsLoading = false; }
    }
}

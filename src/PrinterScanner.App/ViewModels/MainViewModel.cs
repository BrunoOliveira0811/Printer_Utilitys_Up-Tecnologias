using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using PrinterScanner.App.Infrastructure;
using PrinterScanner.App.Models;
using PrinterScanner.App.Services;
using PrinterScanner.App.Views;

namespace PrinterScanner.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService settingsService;
    private readonly IpRangeService ipRangeService;
    private readonly NetworkScannerService networkScannerService;
    private readonly FileLogService logService;
    private readonly PrinterIpService printerIpService;
    private readonly DriverService driverService;
    private readonly UtilitariosService utilitariosService;
    private readonly RelayCommand startScanCommand;
    private readonly RelayCommand cancelScanCommand;
    private readonly RelayCommand clearResultsCommand;
    private readonly RelayCommand changeIpCommand;
    private readonly RelayCommand installDriverCommand;
    private readonly RelayCommand refreshInterfacesCommand;
    private readonly RelayCommand openPrinterControlCommand;
    private readonly RelayCommand openUtilitariosCommand;
    private readonly RelayCommand saveReportCommand;
    private readonly RelayCommand toggleDarkModeCommand;
    private readonly DeviceNameService deviceNameService;
    private readonly PrinterWindowsService printerWindowsService;
    private bool isDarkMode;
    private PrinterDevice? selectedDevice;
    private NetworkInterfaceInfo? selectedNetworkInterface;
    private readonly ObservableCollection<PrinterDevice> devices = new();
    private readonly ObservableCollection<NetworkInterfaceInfo> availableInterfaces = new();
    private CancellationTokenSource? scanCancellationSource;
    private AppSettings currentSettings = new();
    private ScanMode selectedScanMode = ScanMode.SubredeAtual;
    private string startIp = string.Empty;
    private string endIp = string.Empty;
    private string cidrNotation = string.Empty;
    private string snmpCommunitiesText = "public";
    private string searchText = string.Empty;
    private string statusMessage = "Pronto para iniciar a varredura.";
    private double progressValue;
    private string progressText = "0%";
    private bool isScanning;

    public MainViewModel(
        SettingsService settingsService,
        IpRangeService ipRangeService,
        NetworkScannerService networkScannerService,
        FileLogService logService,
        PrinterIpService printerIpService,
        DriverService driverService,
        DeviceNameService deviceNameService,
        PrinterWindowsService printerWindowsService,
        UtilitariosService utilitariosService)
    {
        this.settingsService = settingsService;
        this.ipRangeService = ipRangeService;
        this.networkScannerService = networkScannerService;
        this.logService = logService;
        this.printerIpService      = printerIpService;
        this.driverService         = driverService;
        this.deviceNameService     = deviceNameService;
        this.printerWindowsService = printerWindowsService;
        this.utilitariosService    = utilitariosService;

        DevicesView = CollectionViewSource.GetDefaultView(devices);
        DevicesView.Filter = FilterDevice;

        startScanCommand         = new RelayCommand(_ => _ = StartScanAsync(), _ => !IsScanning);
        cancelScanCommand        = new RelayCommand(_ => CancelScan(), _ => IsScanning);
        clearResultsCommand      = new RelayCommand(_ => ClearResults(), _ => devices.Count > 0 && !IsScanning);
        changeIpCommand          = new RelayCommand(_ => OpenChangeIpDialog(), _ => selectedDevice is not null && !IsScanning);
        installDriverCommand     = new RelayCommand(_ => _ = InstallDriverAsync(), _ => CanInstallDriver());
        refreshInterfacesCommand    = new RelayCommand(_ => LoadInterfaces(), _ => !IsScanning);
        openPrinterControlCommand   = new RelayCommand(_ => OpenPrinterControl());
        openUtilitariosCommand      = new RelayCommand(_ => OpenUtilitarios());
        saveReportCommand           = new RelayCommand(_ => _ = SaveReportAsync(), _ => devices.Count > 0 && !IsScanning);
        toggleDarkModeCommand       = new RelayCommand(_ => ToggleDarkMode());
    }

    public ICollectionView DevicesView { get; }
    public IEnumerable<NetworkInterfaceInfo> AvailableInterfaces => availableInterfaces;

    public RelayCommand StartScanCommand => startScanCommand;

    public RelayCommand CancelScanCommand => cancelScanCommand;

    public RelayCommand ClearResultsCommand => clearResultsCommand;

    public RelayCommand ChangeIpCommand => changeIpCommand;

    public RelayCommand InstallDriverCommand     => installDriverCommand;
    public RelayCommand RefreshInterfacesCommand  => refreshInterfacesCommand;
    public RelayCommand OpenPrinterControlCommand => openPrinterControlCommand;
    public RelayCommand OpenUtilitariosCommand    => openUtilitariosCommand;
    public RelayCommand SaveReportCommand         => saveReportCommand;
    public RelayCommand ToggleDarkModeCommand     => toggleDarkModeCommand;

    public bool IsDarkMode
    {
        get => isDarkMode;
        private set
        {
            if (SetProperty(ref isDarkMode, value))
                RaisePropertyChanged(nameof(DarkModeButtonText));
        }
    }

    public string DarkModeButtonText => isDarkMode ? "Modo Claro" : "Modo Escuro";

    public PrinterDevice? SelectedDevice
    {
        get => selectedDevice;
        set
        {
            SetProperty(ref selectedDevice, value);
            changeIpCommand.RaiseCanExecuteChanged();
            installDriverCommand.RaiseCanExecuteChanged();
        }
    }

    public NetworkInterfaceInfo? SelectedNetworkInterface
    {
        get => selectedNetworkInterface;
        set => SetProperty(ref selectedNetworkInterface, value);
    }

    public IEnumerable<ScanMode> ScanModes => Enum.GetValues<ScanMode>();

    public ScanMode SelectedScanMode
    {
        get => selectedScanMode;
        set => SetProperty(ref selectedScanMode, value);
    }

    public string StartIp
    {
        get => startIp;
        set => SetProperty(ref startIp, value);
    }

    public string EndIp
    {
        get => endIp;
        set => SetProperty(ref endIp, value);
    }

    public string CidrNotation
    {
        get => cidrNotation;
        set => SetProperty(ref cidrNotation, value);
    }

    public string SnmpCommunitiesText
    {
        get => snmpCommunitiesText;
        set => SetProperty(ref snmpCommunitiesText, value);
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                DevicesView.Refresh();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public double ProgressValue
    {
        get => progressValue;
        private set => SetProperty(ref progressValue, value);
    }

    public string ProgressText
    {
        get => progressText;
        private set => SetProperty(ref progressText, value);
    }

    public bool IsScanning
    {
        get => isScanning;
        private set
        {
            if (SetProperty(ref isScanning, value))
            {
                UpdateCommands();
            }
        }
    }

    public async Task InitializeAsync()
    {
        currentSettings = await settingsService.LoadSettingsAsync();
        SnmpCommunitiesText = string.Join("; ", currentSettings.SnmpCommunities);
        await deviceNameService.LoadAsync();
        ApplyTheme(currentSettings.DarkModeEnabled);
        IsDarkMode = currentSettings.DarkModeEnabled;

        ProgressValue = 0;
        ProgressText = "0%";
        StatusMessage = "Aplicativo pronto. Clique em Iniciar para buscar impressoras.";

        LoadInterfaces();
        RefreshExportState();
    }

    public async Task StartScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        if (!ipRangeService.HasActiveNetworkInterface())
        {
            MessageBox.Show(
                "Nenhuma interface de rede ativa foi encontrada. Conecte-se por Wi-Fi ou Ethernet e tente novamente.",
                "Rede indisponivel",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsScanning = true;
            scanCancellationSource = new CancellationTokenSource();
            ProgressValue = 0;
            ProgressText = "0%";
            StatusMessage = "Preparando varredura...";
            devices.Clear();

            currentSettings.SnmpCommunities = ParseCommunities(SnmpCommunitiesText);
            await settingsService.SaveSettingsAsync(currentSettings);

            var request = BuildRequest();
            var progress = new Progress<ScanProgress>(info =>
            {
                ProgressValue = info.Percent;
                ProgressText = $"{Math.Round(info.Percent, 0)}%";
                StatusMessage = $"Varrendo {info.CurrentIp} ({info.ProcessedCount}/{info.TotalCount})";
            });

            await networkScannerService.ScanAsync(
                request,
                progress,
                device => Application.Current.Dispatcher.Invoke(() => UpsertDevice(device)),
                scanCancellationSource.Token);

            StatusMessage = $"Varredura concluida. {devices.Count} impressora(s) encontrada(s).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Varredura cancelada pelo usuario.";
        }
        catch (InvalidOperationException ex)
        {
            logService.LogError("Validacao da varredura falhou.", ex);
            StatusMessage = ex.Message;
            MessageBox.Show(
                ex.Message,
                "Validacao da varredura",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            logService.LogError("Erro ao iniciar a varredura.", ex);
            StatusMessage = "A varredura falhou. Consulte o log local para detalhes.";
            MessageBox.Show(
                "Ocorreu um erro durante a varredura. Consulte o log local para detalhes.",
                "Falha na varredura",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            scanCancellationSource?.Dispose();
            scanCancellationSource = null;
            RefreshExportState();
        }
    }

    private void CancelScan()
    {
        scanCancellationSource?.Cancel();
    }

    private void ClearResults()
    {
        foreach (var d in devices) d.PropertyChanged -= OnDevicePropertyChanged;
        devices.Clear();
        SearchText = string.Empty;
        ProgressValue = 0;
        ProgressText = "0%";
        StatusMessage = "Aplicativo pronto. Clique em Iniciar para buscar impressoras.";
        RefreshExportState();
    }

    private ScanRequest BuildRequest()
    {
        string? preferredMask = null;
        string? selectedLocalIp = null;

        if (SelectedScanMode == ScanMode.SubredeAtual)
        {
            selectedLocalIp = selectedNetworkInterface?.IpAddress;
            preferredMask   = ipRangeService.GetCurrentSubnet(selectedLocalIp).SubnetMask.ToString();
        }

        return SelectedScanMode switch
        {
            ScanMode.SubredeAtual => new ScanRequest
            {
                Mode = SelectedScanMode,
                SelectedLocalIP = selectedLocalIp,
                Communities = currentSettings.SnmpCommunities,
                TimeoutMilliseconds = currentSettings.TimeoutMilliseconds,
                MaxConcurrency = currentSettings.MaxConcurrentScans,
                PreferredSubnetMask = preferredMask
            },
            ScanMode.FaixaManual => BuildManualRequest(preferredMask),
            ScanMode.Cidr => BuildCidrRequest(preferredMask),
            _ => throw new InvalidOperationException("Modo de varredura invalido.")
        };
    }

    private ScanRequest BuildManualRequest(string? preferredMask)
    {
        if (string.IsNullOrWhiteSpace(StartIp) || string.IsNullOrWhiteSpace(EndIp))
        {
            throw new InvalidOperationException("Informe o IP inicial e o IP final para a varredura manual.");
        }

        ipRangeService.ExpandManualRange(StartIp, EndIp);

        return new ScanRequest
        {
            Mode = SelectedScanMode,
            StartIp = StartIp,
            EndIp = EndIp,
            Communities = currentSettings.SnmpCommunities,
            TimeoutMilliseconds = currentSettings.TimeoutMilliseconds,
            MaxConcurrency = currentSettings.MaxConcurrentScans,
            PreferredSubnetMask = preferredMask
        };
    }

    private ScanRequest BuildCidrRequest(string? preferredMask)
    {
        if (string.IsNullOrWhiteSpace(CidrNotation))
        {
            throw new InvalidOperationException("Informe a rede no formato CIDR.");
        }

        ipRangeService.ExpandCidr(CidrNotation);

        return new ScanRequest
        {
            Mode = SelectedScanMode,
            CidrNotation = CidrNotation,
            Communities = currentSettings.SnmpCommunities,
            TimeoutMilliseconds = currentSettings.TimeoutMilliseconds,
            MaxConcurrency = currentSettings.MaxConcurrentScans,
            PreferredSubnetMask = preferredMask
        };
    }

    private List<string> ParseCommunities(string text)
    {
        var items = text.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Count == 0)
        {
            items.Add("public");
        }

        return items;
    }

    private void UpsertDevice(PrinterDevice device)
    {
        // Aplica nome salvo pelo MAC antes de subscrever para não disparar re-save desnecessário
        if (!string.IsNullOrWhiteSpace(device.MacAddress))
        {
            var saved = deviceNameService.GetName(device.MacAddress);
            if (saved is not null) device.DeviceName = saved;
        }

        var existing = devices.FirstOrDefault(current =>
            (!string.IsNullOrWhiteSpace(device.MacAddress) &&
             string.Equals(current.MacAddress, device.MacAddress, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(current.IpAddress, device.IpAddress, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            device.PropertyChanged += OnDevicePropertyChanged;
            devices.Add(device);
        }
        else
        {
            existing.PropertyChanged -= OnDevicePropertyChanged;
            var index = devices.IndexOf(existing);
            device.PropertyChanged += OnDevicePropertyChanged;
            devices[index] = device;
        }

        ResortDevices();
        RefreshExportState();
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PrinterDevice device || e.PropertyName != nameof(PrinterDevice.DeviceName)) return;
        if (string.IsNullOrWhiteSpace(device.MacAddress)) return;
        _ = PersistDeviceNameAsync(device.MacAddress, device.DeviceName, device.IpAddress);
    }

    private async Task PersistDeviceNameAsync(string mac, string name, string ip)
    {
        await deviceNameService.SetNameAsync(mac, name);
        await printerWindowsService.RenameInstalledPrinterAsync(ip, name);
    }

    private void ResortDevices()
    {
        var sorted = SortDevices(devices);
        foreach (var d in devices) d.PropertyChanged -= OnDevicePropertyChanged;
        devices.Clear();
        foreach (var d in sorted)
        {
            d.PropertyChanged += OnDevicePropertyChanged;
            devices.Add(d);
        }
    }

    private static List<PrinterDevice> SortDevices(IEnumerable<PrinterDevice> source)
    {
        return source
            .OrderBy(item => ParseIpForSorting(item.IpAddress))
            .ToList();
    }

    private static uint ParseIpForSorting(string ipAddress)
    {
        try
        {
            return IpRangeService.ToUInt32(IpRangeService.ParseIpv4(ipAddress));
        }
        catch
        {
            return uint.MaxValue;
        }
    }

    private bool FilterDevice(object item)
    {
        if (item is not PrinterDevice device)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var term = SearchText.Trim();
        return (device.IpAddress?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (device.DeviceName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (device.MacAddress?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (device.SysDescription?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void RefreshExportState()
    {
        DevicesView.Refresh();
        UpdateCommands();
    }

    private void LoadInterfaces()
    {
        var interfaces = ipRangeService.GetActiveInterfaces();
        availableInterfaces.Clear();
        foreach (var iface in interfaces)
            availableInterfaces.Add(iface);

        // Mantém seleção anterior se ainda existir; senão escolhe a primeira
        if (selectedNetworkInterface is not null)
        {
            var match = availableInterfaces.FirstOrDefault(i => i.IpAddress == selectedNetworkInterface.IpAddress);
            SelectedNetworkInterface = match ?? availableInterfaces.FirstOrDefault();
        }
        else
        {
            SelectedNetworkInterface = availableInterfaces.FirstOrDefault();
        }
    }

    private void UpdateCommands()
    {
        startScanCommand.RaiseCanExecuteChanged();
        cancelScanCommand.RaiseCanExecuteChanged();
        clearResultsCommand.RaiseCanExecuteChanged();
        changeIpCommand.RaiseCanExecuteChanged();
        installDriverCommand.RaiseCanExecuteChanged();
        refreshInterfacesCommand.RaiseCanExecuteChanged();
        saveReportCommand.RaiseCanExecuteChanged();
    }

    private bool CanInstallDriver() => !IsScanning;

    private async Task InstallDriverAsync()
    {
        var confirm = MessageBox.Show(
            "Iniciar a instalacao do driver de impressora?\n\nO instalador solicitara permissao de administrador.",
            "Instalar driver",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        StatusMessage = "Iniciando instalacao do driver...";
        var (success, message) = await driverService.InstallEmbeddedDriverAsync();

        MessageBox.Show(
            message,
            success ? "Instalacao concluida" : "Erro na instalacao",
            MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Error);

        StatusMessage = success ? "Driver instalado com sucesso." : $"Falha: {message}";
    }

    private async Task SaveReportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title    = "Salvar relatorio de varredura",
            Filter   = "Arquivo de texto (*.txt)|*.txt",
            FileName = $"impressoras_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt"
        };

        if (dialog.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("RELATORIO DE VARREDURA DE IMPRESSORAS");
        sb.AppendLine($"Data/Hora : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Total     : {devices.Count} impressora(s) encontrada(s)");
        sb.AppendLine(new string('=', 120));
        sb.AppendLine();
        sb.AppendFormat(
            "{0,-17} {1,-30} {2,-20} {3,-16} {4,-16} {5,-14} {6,-5} {7,-5} {8,-5} {9,-5} {10}",
            "IP", "Nome", "MAC", "Mascara", "Gateway", "DHCP", "SNMP", "9100", "515", "631", "Descricao");
        sb.AppendLine();
        sb.AppendLine(new string('-', 120));

        foreach (var d in devices)
        {
            sb.AppendFormat(
                "{0,-17} {1,-30} {2,-20} {3,-16} {4,-16} {5,-14} {6,-5} {7,-5} {8,-5} {9,-5} {10}",
                d.IpAddress,
                Clip(d.DeviceName, 28),
                d.MacAddress,
                d.DisplaySubnetMask,
                d.DisplayGateway,
                d.DhcpStatus,
                d.SnmpResponded ? "Sim" : "Nao",
                d.Port9100Open  ? "Sim" : "Nao",
                d.Port515Open   ? "Sim" : "Nao",
                d.Port631Open   ? "Sim" : "Nao",
                d.SysDescription ?? string.Empty);
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), Encoding.UTF8);
        StatusMessage = $"Relatorio salvo: {dialog.FileName}";
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        ApplyTheme(IsDarkMode);
        currentSettings.DarkModeEnabled = IsDarkMode;
        _ = settingsService.SaveSettingsAsync(currentSettings);
    }

    private static void ApplyTheme(bool dark)
    {
        var theme = dark ? "Dark" : "Light";
        var dict  = new System.Windows.ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Resources/Themes/{theme}.xaml")
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(dict);
    }

    private void OpenUtilitarios()
    {
        var vm     = new UtilitariosViewModel(utilitariosService);
        var dialog = new UtilitariosWindow(vm) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    private static void OpenPrinterControl()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "explorer.exe",
            Arguments       = "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}",
            UseShellExecute = true
        });
    }

    private void OpenChangeIpDialog()
    {
        if (selectedDevice is null) return;

        var dialog = new ChangeIpDialog(
            selectedDevice.IpAddress,
            selectedDevice.SubnetMask ?? string.Empty,
            selectedDevice.Gateway ?? string.Empty,
            printerIpService)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            var vm = (ChangeIpViewModel)dialog.DataContext;
            selectedDevice.IpAddress = vm.NewIp;
            RefreshExportState();
        }
    }
}
